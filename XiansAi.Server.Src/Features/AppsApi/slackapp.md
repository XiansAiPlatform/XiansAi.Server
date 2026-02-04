# Slack App Proxy - Detailed Implementation Guide

## Overview

The Slack App Proxy enables bidirectional communication between Slack and XiansAi agent activations. It handles incoming Slack Events API webhooks and sends outgoing messages back to Slack using the Web API.

## Slack Integration Architecture

### Communication Flow

#### Incoming Flow (Slack → Agent)
```
Slack User sends message
    ↓
Slack Events API
    ↓ [POST webhook]
/api/apps/slack/events/{appInstanceId}
    ↓ [Verify signature]
SlackProxy.ProcessIncomingWebhookAsync()
    ↓ [Parse & Filter]
Transform to ChatOrDataRequest
    ↓
MessageService.ProcessIncomingMessage()
    ↓
Agent Workflow (receives message via Temporal Signal)
```

#### Outgoing Flow (Agent → Slack)
```
Agent Workflow
    ↓ [ProcessOutgoingMessage]
MessageService.ProcessOutgoingMessage()
    ↓ [Message event published]
AppMessageRouterService (filters by origin pattern)
    ↓
SlackProxy.SendOutgoingMessageAsync()
    ↓ [Transform & POST]
Slack Web API (chat.postMessage)
    ↓
Message appears in Slack
```

## Implementation Details

### 1. Slack Event Types

The Slack Events API sends different types of events:

#### URL Verification Challenge
When you configure the webhook URL in Slack, they send a challenge to verify ownership:

```json
{
  "type": "url_verification",
  "challenge": "3eZbrw1aBm2rZgRNFdxV2595E9CY3gmdALWMmHkvFXO7tYXAYM8P"
}
```

**Required Response**: Echo back the challenge value with `200 OK` and `text/plain` content type.

#### Event Callback
When a user interacts with your app, Slack sends event callbacks:

```json
{
  "type": "event_callback",
  "team_id": "T1234567890",
  "event": {
    "type": "message",
    "subtype": null,
    "user": "U1234567890",
    "text": "Hello, bot!",
    "ts": "1234567890.123456",
    "channel": "C1234567890",
    "thread_ts": "1234567890.123456"
  },
  "event_time": 1234567890
}
```

**Important Events to Handle**:
- `message` - User sends a message
- `app_mention` - User mentions your bot
- `message.channels` - Message in a channel where bot is present
- `message.im` - Direct message to bot

**Events to Ignore**:
- `message` with `subtype: "bot_message"` - Prevents echo loops
- `message` with `subtype: "message_changed"` - Edits
- `message` with `subtype: "message_deleted"` - Deletions

### 2. SlackProxy Implementation

#### File: `/Features/AppsApi/Proxies/SlackProxy.cs`

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Shared.Services;
using Shared.Repositories;
using Features.AppsApi.Models;
using Features.AppsApi.Repositories;

namespace Features.AppsApi.Proxies;

public class SlackProxy : IAppProxy
{
    private readonly ILogger<SlackProxy> _logger;
    private readonly IMessageService _messageService;
    private readonly HttpClient _httpClient;

    public string PlatformId => "slack";

    public SlackProxy(
        ILogger<SlackProxy> logger,
        IMessageService messageService,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _messageService = messageService;
        _httpClient = httpClientFactory.CreateClient("SlackApi");
    }

    /// <summary>
    /// Process incoming webhook from Slack Events API
    /// </summary>
    public async Task<AppProxyResult> ProcessIncomingWebhookAsync(
        AppProxyIncomingContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing Slack webhook for app instance {AppInstanceId}", 
                context.AppInstanceId);

            // Parse the Slack payload
            var payload = ParseSlackPayload(context.RawBody);
            if (payload == null)
            {
                return AppProxyResult.BadRequest("Invalid Slack payload");
            }

            // Handle URL verification challenge
            if (payload.Type == SlackEventType.UrlVerification)
            {
                return HandleUrlVerification(payload);
            }

            // Handle event callbacks
            if (payload.Type == SlackEventType.EventCallback)
            {
                // Respond immediately to avoid Slack 3-second timeout
                // Process event asynchronously
                _ = Task.Run(async () => 
                {
                    try
                    {
                        await ProcessEventCallbackAsync(context, payload, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Slack event callback");
                    }
                }, cancellationToken);

                return AppProxyResult.Ok();
            }

            _logger.LogWarning("Unhandled Slack event type: {Type}", payload.Type);
            return AppProxyResult.Ok(); // Ack the event even if we don't handle it
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Slack webhook");
            return AppProxyResult.Error("Internal error processing webhook", 500);
        }
    }

    /// <summary>
    /// Send outgoing message to Slack using Web API
    /// </summary>
    public async Task<AppProxyResult> SendOutgoingMessageAsync(
        AppProxyOutgoingContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending message to Slack for app instance {AppInstanceId}", 
                context.AppInstanceId);

            // Extract Slack-specific metadata from message
            var slackMetadata = ExtractSlackMetadata(context.Message);
            
            // Get bot token from app instance configuration
            var botToken = GetBotToken(context.AppInstance);
            if (string.IsNullOrEmpty(botToken))
            {
                return AppProxyResult.Error("Slack bot token not configured", 500);
            }

            // Build Slack message
            var slackMessage = BuildSlackMessage(context.Message, slackMetadata);

            // Send to Slack API
            var response = await PostToSlackApiAsync(
                "chat.postMessage", 
                botToken, 
                slackMessage, 
                cancellationToken);

            if (response.Ok)
            {
                _logger.LogInformation("Successfully sent message to Slack channel {Channel}", 
                    slackMetadata.Channel);
                return AppProxyResult.Ok();
            }
            else
            {
                _logger.LogError("Slack API error: {Error}", response.Error);
                return AppProxyResult.Error($"Slack API error: {response.Error}", 500);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to Slack");
            return AppProxyResult.Error("Failed to send message to Slack", 500);
        }
    }

    /// <summary>
    /// Verify Slack webhook signature
    /// </summary>
    public async Task<bool> VerifyWebhookAuthenticationAsync(
        HttpContext httpContext,
        AppProxyConfiguration config)
    {
        try
        {
            var signingSecret = config.GetValue<string>("signingSecret");
            if (string.IsNullOrEmpty(signingSecret))
            {
                _logger.LogWarning("Slack signing secret not configured");
                return false;
            }

            // Get Slack signature headers
            if (!httpContext.Request.Headers.TryGetValue("X-Slack-Signature", out var signature) ||
                !httpContext.Request.Headers.TryGetValue("X-Slack-Request-Timestamp", out var timestamp))
            {
                _logger.LogWarning("Missing Slack signature headers");
                return false;
            }

            // Check timestamp to prevent replay attacks (must be within 5 minutes)
            if (!IsValidTimestamp(timestamp.ToString()))
            {
                _logger.LogWarning("Slack request timestamp is too old");
                return false;
            }

            // Read request body
            httpContext.Request.EnableBuffering();
            using var reader = new StreamReader(httpContext.Request.Body, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            httpContext.Request.Body.Position = 0; // Reset for subsequent reads

            // Compute expected signature
            var expectedSignature = ComputeSlackSignature(signingSecret, timestamp.ToString(), body);

            // Compare signatures (constant-time comparison to prevent timing attacks)
            var isValid = SlowEquals(signature.ToString(), expectedSignature);

            if (!isValid)
            {
                _logger.LogWarning("Slack signature verification failed");
            }

            return isValid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying Slack webhook signature");
            return false;
        }
    }

    #region Private Helper Methods

    private SlackWebhookPayload? ParseSlackPayload(string rawBody)
    {
        try
        {
            // Handle potentially double-encoded JSON
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(rawBody);
            
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var innerJson = jsonElement.GetString();
                return JsonSerializer.Deserialize<SlackWebhookPayload>(innerJson!);
            }

            return JsonSerializer.Deserialize<SlackWebhookPayload>(rawBody);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Slack payload");
            return null;
        }
    }

    private AppProxyResult HandleUrlVerification(SlackWebhookPayload payload)
    {
        _logger.LogInformation("Responding to Slack URL verification challenge");
        
        return new AppProxyResult
        {
            IsSuccess = true,
            HttpStatusCode = 200,
            Data = new
            {
                ContentType = "text/plain",
                Content = payload.Challenge
            }
        };
    }

    private async Task ProcessEventCallbackAsync(
        AppProxyIncomingContext context,
        SlackWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var slackEvent = payload.Event;
        if (slackEvent == null)
        {
            _logger.LogWarning("Event is null in event_callback");
            return;
        }

        // Filter out bot messages to prevent loops
        if (slackEvent.Subtype == "bot_message")
        {
            _logger.LogDebug("Ignoring bot message to prevent loop");
            return;
        }

        // Filter out message edits and deletions
        if (slackEvent.Subtype == "message_changed" || slackEvent.Subtype == "message_deleted")
        {
            _logger.LogDebug("Ignoring message edit/deletion event");
            return;
        }

        // Only process message events
        if (slackEvent.Type != "message" && slackEvent.Type != "app_mention")
        {
            _logger.LogDebug("Ignoring non-message event: {EventType}", slackEvent.Type);
            return;
        }

        // Extract message details
        var messageText = slackEvent.Text;
        if (string.IsNullOrWhiteSpace(messageText))
        {
            _logger.LogDebug("Message text is empty, ignoring");
            return;
        }

        // Build ChatOrDataRequest
        var chatRequest = BuildChatRequest(context, slackEvent, payload);

        // Send to agent via MessageService
        var result = await _messageService.ProcessIncomingMessage(chatRequest, MessageType.Chat);
        
        if (result.IsSuccess)
        {
            _logger.LogInformation("Successfully sent Slack message to agent workflow");
        }
        else
        {
            _logger.LogError("Failed to send Slack message to agent: {Error}", result.ErrorMessage);
        }
    }

    private ChatOrDataRequest BuildChatRequest(
        AppProxyIncomingContext context,
        SlackEvent slackEvent,
        SlackWebhookPayload payload)
    {
        var appInstance = context.AppInstance;
        var mappingConfig = appInstance.MappingConfig;

        // Determine participantId based on mapping configuration
        var participantId = DetermineParticipantId(slackEvent, mappingConfig);

        // Determine scope based on mapping configuration
        var scope = DetermineScope(slackEvent, mappingConfig);

        // Build the request
        return new ChatOrDataRequest
        {
            WorkflowId = appInstance.WorkflowId,
            ParticipantId = participantId,
            Text = slackEvent.Text,
            Data = new
            {
                slack = new
                {
                    userId = slackEvent.User,
                    channel = slackEvent.Channel,
                    threadTs = slackEvent.ThreadTs,
                    ts = slackEvent.Ts,
                    teamId = payload.TeamId,
                    eventType = slackEvent.Type,
                    originalPayload = payload
                }
            },
            Scope = scope,
            Origin = $"app:slack:{context.AppInstanceId}",
            Type = MessageType.Chat,
            RequestId = Guid.NewGuid().ToString()
        };
    }

    private string DetermineParticipantId(SlackEvent slackEvent, AppInstanceMappingConfig mappingConfig)
    {
        return mappingConfig.ParticipantIdSource switch
        {
            "userId" => slackEvent.User ?? mappingConfig.DefaultParticipantId ?? "unknown",
            "channelId" => slackEvent.Channel ?? mappingConfig.DefaultParticipantId ?? "unknown",
            "threadId" => slackEvent.ThreadTs ?? slackEvent.Channel ?? mappingConfig.DefaultParticipantId ?? "unknown",
            _ => slackEvent.User ?? mappingConfig.DefaultParticipantId ?? "unknown"
        };
    }

    private string? DetermineScope(SlackEvent slackEvent, AppInstanceMappingConfig mappingConfig)
    {
        if (string.IsNullOrEmpty(mappingConfig.ScopeSource))
        {
            return mappingConfig.DefaultScope;
        }

        return mappingConfig.ScopeSource switch
        {
            "channelId" => slackEvent.Channel,
            "threadId" => slackEvent.ThreadTs,
            _ => mappingConfig.DefaultScope
        };
    }

    private SlackMetadata ExtractSlackMetadata(ConversationMessage message)
    {
        var metadata = new SlackMetadata();

        if (message.Data is JsonElement jsonElement && jsonElement.ValueKind == JsonValueKind.Object)
        {
            if (jsonElement.TryGetProperty("slack", out var slackData))
            {
                metadata.Channel = slackData.TryGetProperty("channel", out var channel) 
                    ? channel.GetString() 
                    : null;
                    
                metadata.ThreadTs = slackData.TryGetProperty("threadTs", out var threadTs) 
                    ? threadTs.GetString() 
                    : null;
            }
        }

        return metadata;
    }

    private string? GetBotToken(AppInstance appInstance)
    {
        if (appInstance.Configuration.TryGetValue("botToken", out var token))
        {
            return token?.ToString();
        }
        return null;
    }

    private object BuildSlackMessage(ConversationMessage message, SlackMetadata metadata)
    {
        var slackMessage = new Dictionary<string, object>
        {
            ["channel"] = metadata.Channel ?? throw new InvalidOperationException("Slack channel is required"),
            ["text"] = message.Text ?? "No message text"
        };

        // Include thread_ts to maintain conversation threading
        if (!string.IsNullOrEmpty(metadata.ThreadTs))
        {
            slackMessage["thread_ts"] = metadata.ThreadTs;
        }

        // If message contains rich formatting in Data field, include blocks
        if (message.Data != null)
        {
            if (message.Data is JsonElement jsonElement)
            {
                if (jsonElement.TryGetProperty("blocks", out var blocks))
                {
                    slackMessage["blocks"] = blocks;
                }
            }
        }

        return slackMessage;
    }

    private async Task<SlackApiResponse> PostToSlackApiAsync(
        string method, 
        string botToken, 
        object payload, 
        CancellationToken cancellationToken)
    {
        var url = $"https://slack.com/api/{method}";
        
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Authorization", $"Bearer {botToken}");
        
        var jsonContent = JsonSerializer.Serialize(payload);
        request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Deserialize<SlackApiResponse>(responseBody) 
            ?? new SlackApiResponse { Ok = false, Error = "Failed to parse Slack API response" };
    }

    private string ComputeSlackSignature(string signingSecret, string timestamp, string body)
    {
        // Slack signature format: v0:{timestamp}:{body}
        var sigBaseString = $"v0:{timestamp}:{body}";
        
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sigBaseString));
        
        return "v0=" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }

    private bool IsValidTimestamp(string timestamp)
    {
        if (!long.TryParse(timestamp, out var requestTime))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var difference = Math.Abs(now - requestTime);

        // Request must be within 5 minutes
        return difference < 300;
    }

    private bool SlowEquals(string a, string b)
    {
        // Constant-time string comparison to prevent timing attacks
        if (a.Length != b.Length)
        {
            return false;
        }

        var diff = 0;
        for (var i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }

        return diff == 0;
    }

    #endregion
}
```

### 3. Slack Data Models

#### File: `/Features/AppsApi/Models/SlackModels.cs`

```csharp
using System.Text.Json.Serialization;

namespace Features.AppsApi.Models;

public static class SlackEventType
{
    public const string UrlVerification = "url_verification";
    public const string EventCallback = "event_callback";
}

public class SlackWebhookPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("challenge")]
    public string? Challenge { get; set; }

    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("event")]
    public SlackEvent? Event { get; set; }

    [JsonPropertyName("event_time")]
    public long? EventTime { get; set; }

    [JsonPropertyName("authorizations")]
    public List<SlackAuthorization>? Authorizations { get; set; }
}

public class SlackEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }

    [JsonPropertyName("thread_ts")]
    public string? ThreadTs { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("channel_type")]
    public string? ChannelType { get; set; }

    [JsonPropertyName("event_ts")]
    public string? EventTs { get; set; }
}

public class SlackAuthorization
{
    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }
}

public class SlackApiResponse
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }

    [JsonPropertyName("message")]
    public SlackMessage? Message { get; set; }
}

public class SlackMessage
{
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("bot_id")]
    public string? BotId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }

    [JsonPropertyName("thread_ts")]
    public string? ThreadTs { get; set; }

    [JsonPropertyName("blocks")]
    public List<object>? Blocks { get; set; }
}

public class SlackMetadata
{
    public string? Channel { get; set; }
    public string? ThreadTs { get; set; }
}
```

### 4. Endpoint Configuration

#### File: `/Features/AppsApi/Endpoints/AppProxyEndpoints.cs`

```csharp
public static void MapAppProxyEndpoints(this WebApplication app)
{
    var appsGroup = app.MapGroup("/api/apps")
        .WithTags("AppsAPI - Webhooks");

    // Slack Events API endpoint
    appsGroup.MapPost("/slack/events/{appInstanceId}", async (
        string appInstanceId,
        HttpContext httpContext,
        [FromServices] IAppProxyService appProxyService,
        [FromServices] IAppInstanceRepository appInstanceRepository,
        CancellationToken cancellationToken) =>
    {
        try
        {
            // Load app instance configuration
            var appInstance = await appInstanceRepository.GetByIdAsync(appInstanceId);
            if (appInstance == null || !appInstance.IsActive)
            {
                return Results.NotFound("App instance not found or inactive");
            }

            // Verify webhook signature
            var slackProxy = appProxyService.GetProxy("slack");
            var isAuthentic = await slackProxy.VerifyWebhookAuthenticationAsync(
                httpContext, 
                new AppProxyConfiguration(appInstance.Configuration));

            if (!isAuthentic)
            {
                return Results.Unauthorized();
            }

            // Read request body
            using var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8);
            var rawBody = await reader.ReadToEndAsync();

            // Build context
            var context = new AppProxyIncomingContext
            {
                AppInstanceId = appInstanceId,
                AppInstance = appInstance,
                HttpContext = httpContext,
                RawBody = rawBody,
                Headers = httpContext.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
                QueryParams = httpContext.Request.Query.ToDictionary(q => q.Key, q => q.Value.ToString())
            };

            // Process webhook
            var result = await slackProxy.ProcessIncomingWebhookAsync(context, cancellationToken);

            // Return appropriate response
            if (result.IsSuccess)
            {
                if (result.Data != null)
                {
                    // For URL verification, return challenge
                    return Results.Content(
                        result.Data.ToString() ?? "", 
                        "text/plain", 
                        Encoding.UTF8, 
                        result.HttpStatusCode ?? 200);
                }
                return Results.Ok();
            }
            else
            {
                return Results.Problem(result.ErrorMessage, statusCode: result.HttpStatusCode ?? 500);
            }
        }
        catch (Exception ex)
        {
            return Results.Problem($"Error processing Slack webhook: {ex.Message}", statusCode: 500);
        }
    })
    .WithName("Slack Events Webhook")
    .AllowAnonymous() // Webhook authentication is done via signature verification
    .WithOpenApi(operation =>
    {
        operation.Summary = "Slack Events API webhook endpoint";
        operation.Description = @"Receives webhooks from Slack Events API.
        
This endpoint handles:
- URL verification challenges
- Message events
- App mention events
- Other Slack events configured in the app

The endpoint verifies the Slack signature before processing events.";
        return operation;
    });

    // Additional Slack endpoints for interactive components, slash commands, etc.
    // can be added here following similar patterns
}
```

### 5. Message Router Service

#### File: `/Features/AppsApi/Services/AppMessageRouterService.cs`

```csharp
public class AppMessageRouterService : BackgroundService
{
    private readonly ILogger<AppMessageRouterService> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IMessageEventPublisher _messageEventPublisher;

    public AppMessageRouterService(
        ILogger<AppMessageRouterService> logger,
        IServiceProvider serviceProvider,
        IMessageEventPublisher messageEventPublisher)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _messageEventPublisher = messageEventPublisher;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("App Message Router Service started");

        // Subscribe to outgoing message events
        await _messageEventPublisher.SubscribeToOutgoingMessagesAsync(
            async (message) => await RouteOutgoingMessageAsync(message, stoppingToken),
            stoppingToken);
    }

    private async Task RouteOutgoingMessageAsync(ConversationMessage message, CancellationToken cancellationToken)
    {
        try
        {
            // Check if message is from an app proxy (origin format: "app:{platformId}:{appInstanceId}")
            if (string.IsNullOrEmpty(message.Origin) || !message.Origin.StartsWith("app:"))
            {
                return; // Not an app-routed message
            }

            var originParts = message.Origin.Split(':');
            if (originParts.Length != 3)
            {
                _logger.LogWarning("Invalid app origin format: {Origin}", message.Origin);
                return;
            }

            var platformId = originParts[1];
            var appInstanceId = originParts[2];

            _logger.LogInformation("Routing outgoing message to {Platform} app {AppInstanceId}", 
                platformId, appInstanceId);

            // Create a scope to resolve scoped services
            using var scope = _serviceProvider.CreateScope();
            var appProxyService = scope.ServiceProvider.GetRequiredService<IAppProxyService>();
            var appInstanceRepository = scope.ServiceProvider.GetRequiredService<IAppInstanceRepository>();

            // Load app instance
            var appInstance = await appInstanceRepository.GetByIdAsync(appInstanceId);
            if (appInstance == null || !appInstance.IsActive)
            {
                _logger.LogWarning("App instance {AppInstanceId} not found or inactive", appInstanceId);
                return;
            }

            // Get the appropriate proxy
            var proxy = appProxyService.GetProxy(platformId);
            if (proxy == null)
            {
                _logger.LogWarning("No proxy found for platform {PlatformId}", platformId);
                return;
            }

            // Build outgoing context
            var context = new AppProxyOutgoingContext
            {
                AppInstanceId = appInstanceId,
                AppInstance = appInstance,
                Message = message,
                WorkflowId = message.WorkflowId,
                ParticipantId = message.ParticipantId
            };

            // Send via proxy
            var result = await proxy.SendOutgoingMessageAsync(context, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Successfully sent message to {Platform}", platformId);
            }
            else
            {
                _logger.LogError("Failed to send message to {Platform}: {Error}", 
                    platformId, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error routing outgoing message");
        }
    }
}
```

## Configuration & Setup

### 1. Create Slack App

1. **Go to [Slack API](https://api.slack.com/apps)**
2. **Click "Create New App" → "From scratch"**
3. **Configure Bot Token Scopes** (OAuth & Permissions):
   ```
   - channels:history (Read messages in public channels)
   - channels:read (View basic channel info)
   - chat:write (Send messages)
   - im:history (Read direct messages)
   - im:read (View basic DM info)
   - im:write (Send DMs)
   - app_mentions:read (Read when bot is mentioned)
   ```

4. **Subscribe to Events** (Event Subscriptions):
   - Enable Events
   - Set Request URL: `https://your-server.com/api/apps/slack/events/{appInstanceId}`
   - Subscribe to bot events:
     ```
     - message.channels (Messages in channels where bot is present)
     - message.im (Direct messages to bot)
     - app_mention (When bot is mentioned)
     ```

5. **Install App to Workspace**
   - Note the **Bot User OAuth Token** (starts with `xoxb-`)
   - Note the **Signing Secret** (from Basic Information)

### 2. Create App Instance in XiansAi

```http
POST /api/v1/admin/tenants/{tenantId}/apps
Content-Type: application/json
Authorization: Bearer {admin_token}

{
  "platformId": "slack",
  "name": "Customer Support Bot",
  "agentName": "SupportAgent",
  "activationName": "LiveSupport",
  "configuration": {
    "botToken": "xoxb-your-bot-token",
    "signingSecret": "your-signing-secret",
    "appId": "A1234567890",
    "teamId": "T1234567890"
  },
  "mappingConfig": {
    "participantIdSource": "userId",
    "scopeSource": "channelId",
    "defaultParticipantId": "unknown"
  },
  "isActive": true
}
```

Response:
```json
{
  "id": "65f8a3b2e9c1234567890abc",
  "platformId": "slack",
  "name": "Customer Support Bot",
  "webhookUrl": "https://your-server.com/api/apps/slack/events/65f8a3b2e9c1234567890abc",
  "status": "active"
}
```

### 3. Configure Slack App with Webhook URL

1. Go back to your Slack App settings
2. Navigate to **Event Subscriptions**
3. Update **Request URL** with the `webhookUrl` from the response above
4. Slack will send a verification challenge - the endpoint will automatically respond

## Agent Workflow Integration

### Receiving Messages from Slack

Messages from Slack arrive via the standard messaging signal:

```csharp
[Workflow]
public class SupportAgentWorkflow
{
    [WorkflowRun]
    public async Task RunAsync()
    {
        // Listen for incoming messages
        await Workflow.WaitConditionAsync(() => hasNewMessage);
    }

    [Signal("inbound_chat_or_data")]
    public async Task HandleIncomingMessageAsync(InboundMessagePayload payload)
    {
        // payload.Origin will be "app:slack:{appInstanceId}"
        // payload.Data will contain Slack-specific metadata:
        // {
        //   "slack": {
        //     "userId": "U1234567890",
        //     "channel": "C1234567890",
        //     "threadTs": "1234567890.123456",
        //     "ts": "1234567890.123456",
        //     "teamId": "T1234567890"
        //   }
        // }

        var slackData = payload.Data?.slack;
        var userId = slackData?.userId;
        var channel = slackData?.channel;
        var threadTs = slackData?.threadTs;

        // Process the message
        var response = await ProcessUserMessageAsync(payload.Text);

        // Send response back to Slack
        await SendToSlackAsync(response, channel, threadTs);
    }

    private async Task SendToSlackAsync(string text, string channel, string? threadTs)
    {
        // Use the agent API to send outbound message
        var chatRequest = new ChatOrDataRequest
        {
            WorkflowId = Workflow.Info.WorkflowId,
            ParticipantId = currentParticipantId,
            Text = text,
            Data = new
            {
                slack = new
                {
                    channel = channel,
                    threadTs = threadTs
                }
            },
            Origin = $"app:slack:{appInstanceId}", // IMPORTANT: Include origin for routing
            Type = MessageType.Chat
        };

        await agentApiClient.SendOutboundChatAsync(chatRequest);
    }
}
```

### Rich Messaging with Blocks

For rich formatting, include Slack blocks in the Data field:

```csharp
private async Task SendRichMessageToSlackAsync(string text, string channel, string? threadTs)
{
    var chatRequest = new ChatOrDataRequest
    {
        WorkflowId = Workflow.Info.WorkflowId,
        ParticipantId = currentParticipantId,
        Text = text,
        Data = new
        {
            slack = new
            {
                channel = channel,
                threadTs = threadTs
            },
            blocks = new[]
            {
                new
                {
                    type = "section",
                    text = new
                    {
                        type = "mrkdwn",
                        text = "*Order Status Update*\nYour order #12345 is out for delivery!"
                    }
                },
                new
                {
                    type = "actions",
                    elements = new[]
                    {
                        new
                        {
                            type = "button",
                            text = new { type = "plain_text", text = "Track Package" },
                            url = "https://tracking.example.com/12345"
                        }
                    }
                }
            }
        },
        Origin = $"app:slack:{appInstanceId}",
        Type = MessageType.Chat
    };

    await agentApiClient.SendOutboundChatAsync(chatRequest);
}
```

## Quick Start Testing Guide

This guide walks you through testing the Slack integration end-to-end using the API.

### Prerequisites

- XiansAi Server running locally or on a server
- Admin API key for authentication
- Slack workspace with admin access
- ngrok (for local testing) or a public URL

### Step 1: Create Slack App

1. **Go to https://api.slack.com/apps**
2. **Click "Create New App" → "From scratch"**
3. **Name your app** (e.g., "XiansAI Test Bot")
4. **Select your workspace**

### Step 2: Configure Slack App Permissions

1. **Navigate to "OAuth & Permissions"**
2. **Add Bot Token Scopes:**
   ```
   - channels:history
   - channels:read
   - chat:write
   - im:history
   - im:read
   - im:write
   - app_mentions:read
   ```
3. **Install App to Workspace**
4. **Copy the "Bot User OAuth Token"** (starts with `xoxb-`)

### Step 3: Get Signing Secret

1. **Navigate to "Basic Information"**
2. **Scroll to "App Credentials"**
3. **Copy the "Signing Secret"**

### Step 4: Get Incoming Webhook URL (Optional, for outbound messages)

If you want the agent to send messages back to Slack using Incoming Webhooks:

1. **Go to your Slack App → "Incoming Webhooks"**
2. **Activate Incoming Webhooks** (toggle on)
3. **Click "Add New Webhook to Workspace"**
4. **Select a channel** (you can change this later in code)
5. **Copy the Webhook URL** (e.g., `https://hooks.slack.com/services/T00/B00/XXXX`)

> **Alternative:** Use `botToken` instead for more features (threading, dynamic channels)

### Step 5: Create Integration via Admin API

```bash
# Set your variables
export ADMIN_API_KEY="your-admin-api-key"
export TENANT_ID="your-tenant-id"
export BASE_URL="http://localhost:5001"  # or your server URL
export SLACK_BOT_TOKEN="xoxb-..."
export SLACK_SIGNING_SECRET="your-signing-secret"
export SLACK_INCOMING_WEBHOOK="https://hooks.slack.com/services/..."  # Optional

# Create the integration
curl -X POST "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "platformId": "slack",
    "name": "Support Bot",
    "description": "Customer support bot for Slack",
    "agentName": "SupportAgent",
    "activationName": "LiveSupport",
    "configuration": {
      "signingSecret": "'${SLACK_SIGNING_SECRET}'",
      "botToken": "'${SLACK_BOT_TOKEN}'",
      "incomingWebhookUrl": "'${SLACK_INCOMING_WEBHOOK}'"
    },
    "mappingConfig": {
      "participantIdSource": "userId",
      "scopeSource": "channelId",
      "defaultParticipantId": "unknown"
    },
    "isEnabled": true
  }'
```

**Response:**
```json
{
  "id": "65f8a3b2e9c1234567890abc",
  "webhookUrl": "http://localhost:5001/api/apps/slack/events/65f8a3b2e9c1234567890abc",
  "platformId": "slack",
  "name": "Support Bot",
  ...
}
```

**Save the `webhookUrl` and `id` for next steps.**

### Step 6: Expose Local Server (if testing locally)

```bash
# Install ngrok if you haven't
# brew install ngrok  # macOS
# or download from https://ngrok.com

# Start ngrok tunnel
ngrok http 5001

# Copy the HTTPS URL (e.g., https://abc123.ngrok.io)
# Your webhook URL becomes: https://abc123.ngrok.io/api/apps/slack/events/{integrationId}
```

### Step 7: Configure Slack Event Subscriptions (REQUIRED - DO THIS BEFORE STEP 7)

1. **Go to your Slack App → "Event Subscriptions"**
2. **Enable Events**
3. **Set Request URL:**
   ```
   https://abc123.ngrok.io/api/apps/slack/events/65f8a3b2e9c1234567890abc
   ```
4. **Slack will send verification challenge** - endpoint will automatically respond
5. **You should see "Verified ✓"**

6. **Subscribe to bot events:**
   - `message.channels`
   - `message.im`
   - `app_mention`

7. **Save Changes**

### Step 8: Enable App Home Messages (Required for DMs)

Before you can send direct messages to your bot, you need to enable the Messages tab:

1. **Go to your Slack App → "App Home"**
2. **Scroll to "Show Tabs"**
3. **Enable "Messages Tab":**
   - ✅ Check "Allow users to send Slash commands and messages from the messages tab"
4. **Save Changes**
5. **Reinstall your app** (Slack will prompt you if needed)

> **Note:** Without this setting, users get the error: *"Sending messages to this app has been turned off."*

### Step 9: Test Incoming Messages (Slack → Agent)

**Test 1: Direct Message**
1. Open Slack
2. Find your bot in Apps section
3. Click on the bot to open the Messages tab
4. Send a DM: `Hello, bot!`

**Check Server Logs:**
```bash
# You should see:
[SlackWebhookHandler] Processing Slack event for integration 65f8a3b2e9c...
[MessageService] Processing inbound message for WorkflowId `tenant:SupportAgent:Supervisor Workflow:LiveSupport`
[SlackWebhookHandler] Successfully sent Slack message to agent workflow
```

**Test 2: Channel Mention**
1. Invite bot to a channel: `/invite @YourBot`
2. Mention bot: `@YourBot help me`

**Verify via API:**
```bash
# Get message history
curl "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/messaging/history?\
agentName=SupportAgent&\
activationName=LiveSupport&\
participantId=U1234567890&\
page=1&\
pageSize=10" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}"
```

### Step 9: Test Outgoing Messages (Agent → Slack)

The system automatically routes outgoing messages to Slack via the `AppMessageRouterService` background service.

**Requirements for Outgoing Messages:**
- `incomingWebhookUrl` OR `botToken` in integration configuration
- `origin` field must match pattern: `app:slack:{integrationId}`
- Message `data` must include Slack metadata (channel, threadTs)

**Option A: Test via Admin API**

```bash
# Get the integration ID and Slack metadata from an incoming message
export INTEGRATION_ID="65f8a3b2e9c1234567890abc"  # From Step 4 response
export PARTICIPANT_ID="U1234567890"  # Slack user ID from incoming message
export CHANNEL_ID="C1234567890"      # Slack channel ID from incoming message
export THREAD_TS="1234567890.123456" # Optional: for threading

# Send message from agent (simulating agent workflow response)
curl -X POST "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/messaging/send" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "agentName": "SupportAgent",
    "activationName": "LiveSupport",
    "participantId": "'${PARTICIPANT_ID}'",
    "text": "Hello from the agent! I received your message.",
    "data": {
      "slack": {
        "channel": "'${CHANNEL_ID}'",
        "threadTs": "'${THREAD_TS}'"
      }
    },
    "origin": "app:slack:'${INTEGRATION_ID}'"
  }'
```

**What Happens:**
1. Message is saved with `Direction.Outgoing`
2. `MessageEventPublisher` publishes the message event
3. `AppMessageRouterService` receives the event
4. Filters by `origin` starting with "app:"
5. Extracts platform and integration ID
6. Routes to `SlackWebhookHandler.SendMessageToSlackAsync()`
7. Sends to Slack via incoming webhook URL or Bot API
8. Message appears in Slack channel/thread

**Check Logs:**
```bash
[AppMessageRouterService] Routing outgoing message to slack integration 65f8a3b2e9c...
[SlackWebhookHandler] Sending message to Slack for integration 65f8a3b2e9c...
[SlackWebhookHandler] Successfully sent message to Slack via incoming webhook
```

**Note:** The incoming webhook URL is simpler but can't reply to threads. For threading support, use `botToken` instead.

**Option B: Test from Agent Workflow**

In your agent workflow code:
```csharp
// Extract Slack metadata from incoming message
var incomingData = JsonSerializer.Deserialize<JsonElement>(payload.Data);
var slackChannel = incomingData.GetProperty("slack").GetProperty("channel").GetString();
var slackThreadTs = incomingData.GetProperty("slack").GetProperty("threadTs").GetString();

// Respond back to Slack
var response = new ChatOrDataRequest
{
    WorkflowId = workflowId,
    ParticipantId = payload.ParticipantId,  // Slack user ID
    Text = "I received your message!",
    Data = new
    {
        slack = new
        {
            channel = slackChannel,        // Required
            threadTs = slackThreadTs       // Optional: for threading
        }
    },
    Origin = payload.Origin,  // CRITICAL: Use same origin from incoming message
    Type = MessageType.Chat
};

await agentApiClient.ProcessOutboundChatAsync(response);
```

**Automatic Routing & Metadata Preservation:**
The system now includes **complete auto-preservation** - you don't need to manually set anything!

When you respond to a message, the system automatically:
1. **Copies `origin`** from the last incoming message in the thread
2. **Copies `data` (Slack metadata)** if not provided - includes channel, threadTs, etc.
3. `AppMessageRouterService` detects the origin pattern (`app:slack:{integrationId}`)
4. Routes to `SlackWebhookHandler`
5. Sends to Slack via incoming webhook or Bot API

**Ultra-Simplified Agent Code:**
```csharp
// Minimal code required - everything else is automatic:
var response = new ChatOrDataRequest
{
    WorkflowId = workflowId,
    ParticipantId = payload.ParticipantId,
    Text = "I received your message!"
    // Origin is auto-populated from last incoming message ✨
    // Data (Slack metadata) is auto-populated from last incoming message ✨
};

await agentApiClient.ProcessOutboundChatAsync(response);
```

**What's Automatically Preserved:**
- ✅ Origin (`app:slack:{integrationId}`)
- ✅ Slack channel ID
- ✅ Slack thread timestamp (for threading)
- ✅ Platform-specific metadata

**Agent only needs to provide:**
- Text (the response message)
- WorkflowId and ParticipantId (usually already available in context)

The system handles all the routing automatically! 🚀

### Step 11: Verify Configuration

**Test integration:**
```bash
curl -X POST "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations/${INTEGRATION_ID}/test" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}"
```

**Response:**
```json
{
  "isSuccessful": true,
  "message": "Slack configuration is valid. Configure the webhook URL in your Slack app settings.",
  "details": {
    "hasIncomingWebhookUrl": false,
    "hasSigningSecret": true,
    "hasBotToken": true
  }
}
```

### Step 12: Monitor & Debug

**View Integration Logs:**
```bash
# Check server logs
tail -f /var/log/xiansai-server/app.log

# Filter for Slack events
tail -f /var/log/xiansai-server/app.log | grep Slack
```

**Common Issues:**

1. **"Sending messages to this app has been turned off"**
   - **Solution**: Go to Slack App → App Home → Enable "Messages Tab"
   - Check "Allow users to send Slash commands and messages from the messages tab"
   - Reinstall the app after enabling

2. **"Verification Failed" in Slack**
   - Check signing secret is correct
   - Verify webhook URL is accessible (use ngrok for local testing)
   - Check server logs for signature verification errors
   - Ensure you're using the HTTPS URL from ngrok, not http://localhost

3. **Messages Not Arriving at Server**
   - Verify integration is enabled: `isEnabled: true`
   - Check bot has proper OAuth scopes (see Step 2)
   - Ensure bot is invited to channel (for channel messages): `/invite @YourBot`
   - Check ngrok is still running (sessions expire after 2 hours on free plan)
   - Verify webhook URL in Slack matches your integration ID

4. **Outbound Messages Not Sending**
   - Verify EITHER `incomingWebhookUrl` OR `botToken` is configured
   - Check server logs for `AppMessageRouterService started`
   - Verify `Origin` field format exactly: `app:slack:{integrationId}`
   - Ensure bot has `chat:write` scope (if using botToken)
   - For incoming webhook, ensure URL is valid and not expired
   - Check server logs for "Routing outgoing message to slack"

5. **Bot Not Appearing in Slack**
   - App must be installed to workspace
   - Check OAuth installation completed successfully
   - Reinstall app if settings changed

### Management Commands

**List all integrations:**
```bash
curl "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}"
```

**Get specific integration:**
```bash
curl "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations/${INTEGRATION_ID}" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}"
```

**Disable integration:**
```bash
curl -X POST "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations/${INTEGRATION_ID}/disable" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}"
```

**Enable integration:**
```bash
curl -X POST "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations/${INTEGRATION_ID}/enable" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}"
```

**Delete integration:**
```bash
curl -X DELETE "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations/${INTEGRATION_ID}" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}"
```

### Quick Test Script

Save this as `test-slack-integration.sh`:

```bash
#!/bin/bash

# Configuration
BASE_URL="${BASE_URL:-http://localhost:5001}"
ADMIN_API_KEY="${ADMIN_API_KEY:?Please set ADMIN_API_KEY}"
TENANT_ID="${TENANT_ID:?Please set TENANT_ID}"
SLACK_SIGNING_SECRET="${SLACK_SIGNING_SECRET:?Please set SLACK_SIGNING_SECRET}"
SLACK_BOT_TOKEN="${SLACK_BOT_TOKEN:?Please set SLACK_BOT_TOKEN}"

echo "Creating Slack integration..."
RESPONSE=$(curl -s -X POST "${BASE_URL}/api/v1/admin/tenants/${TENANT_ID}/integrations" \
  -H "Authorization: Bearer ${ADMIN_API_KEY}" \
  -H "Content-Type: application/json" \
  -d '{
    "platformId": "slack",
    "name": "Test Bot",
    "agentName": "TestAgent",
    "activationName": "TestActivation",
    "configuration": {
      "signingSecret": "'"${SLACK_SIGNING_SECRET}"'",
      "botToken": "'"${SLACK_BOT_TOKEN}"'"
    },
    "mappingConfig": {
      "participantIdSource": "userId",
      "scopeSource": "channelId"
    },
    "isEnabled": true
  }')

INTEGRATION_ID=$(echo $RESPONSE | jq -r '.id')
WEBHOOK_URL=$(echo $RESPONSE | jq -r '.webhookUrl')

echo ""
echo "✅ Integration Created!"
echo "Integration ID: ${INTEGRATION_ID}"
echo "Webhook URL: ${WEBHOOK_URL}"
echo ""
echo "Next steps:"
echo "1. If testing locally, run: ngrok http 5001"
echo "2. Configure this URL in Slack Event Subscriptions:"
echo "   https://your-ngrok-url/api/apps/slack/events/${INTEGRATION_ID}"
echo "3. Send a test message to your bot in Slack"
```

**Usage:**
```bash
export ADMIN_API_KEY="your-key"
export TENANT_ID="your-tenant"
export SLACK_SIGNING_SECRET="your-secret"
export SLACK_BOT_TOKEN="xoxb-your-token"

chmod +x test-slack-integration.sh
./test-slack-integration.sh
```

---

## Testing

### 1. Unit Tests

```csharp
[Fact]
public async Task ProcessIncomingWebhook_ShouldHandleUrlVerification()
{
    // Arrange
    var context = new AppProxyIncomingContext
    {
        RawBody = @"{
            ""type"": ""url_verification"",
            ""challenge"": ""test_challenge_123""
        }"
    };

    // Act
    var result = await _slackProxy.ProcessIncomingWebhookAsync(context);

    // Assert
    Assert.True(result.IsSuccess);
    Assert.Equal(200, result.HttpStatusCode);
    Assert.Equal("test_challenge_123", result.Data);
}

[Fact]
public async Task VerifyWebhookAuthentication_ShouldValidateSignature()
{
    // Arrange
    var signingSecret = "test_secret";
    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
    var body = @"{""type"":""event_callback""}";
    
    var signature = ComputeSlackSignature(signingSecret, timestamp, body);

    var httpContext = CreateMockHttpContext(signature, timestamp, body);
    var config = new AppProxyConfiguration(new Dictionary<string, object>
    {
        ["signingSecret"] = signingSecret
    });

    // Act
    var isValid = await _slackProxy.VerifyWebhookAuthenticationAsync(httpContext, config);

    // Assert
    Assert.True(isValid);
}
```

### 2. Integration Test with Real Slack Events

1. **Use ngrok for local testing**:
   ```bash
   ngrok http 5001
   ```

2. **Update Slack app Request URL** with ngrok URL:
   ```
   https://abc123.ngrok.io/api/apps/slack/events/{appInstanceId}
   ```

3. **Send test messages in Slack**:
   - Direct message to bot
   - Mention bot in channel
   - Send message in channel where bot is present

4. **Verify in logs**:
   ```
   [SlackProxy] Processing Slack webhook for app instance {appInstanceId}
   [SlackProxy] Successfully sent Slack message to agent workflow
   [AppMessageRouterService] Routing outgoing message to slack app {appInstanceId}
   [SlackProxy] Successfully sent message to Slack channel {channel}
   ```

## Troubleshooting

### Common Issues

#### 1. Slack Retries Events (sends same event multiple times)

**Cause**: Your endpoint takes longer than 3 seconds to respond.

**Solution**: Implemented in the code - respond immediately with 200 OK, then process asynchronously:

```csharp
// Respond immediately
context.Response.StatusCode = HttpStatusCode.OK;

// Process in background
_ = Task.Run(async () => await ProcessEventCallbackAsync(...));
```

#### 2. Bot Echoes Own Messages (Infinite Loop)

**Cause**: Bot's own messages trigger the webhook, creating an infinite loop.

**Solution**: Reliable bot detection implemented using only trusted indicators:

```csharp
// The system checks TWO reliable indicators:
1. slackEvent.Subtype == "bot_message"  // Slack explicitly marks bot messages
2. slackEvent.BotId != null             // Bot ID field is present
```

**Why only 2 checks?**
- ❌ `app_id` - Can appear in user messages within app context
- ❌ `authorizations.is_bot` - Refers to APP's authorization, not message sender
- ❌ `user == null` - Can be null in legitimate DM scenarios
- ✅ `subtype` and `bot_id` are the ONLY reliable indicators

**If you still see loops:**
- Check server logs for "Bot detected" entries
- Verify which check caught it (subtype or bot_id)
- Check the Slack event payload in logs to debug
- Ensure you're subscribed to the right events in Slack (message.channels, message.im, app_mention)

#### 3. Signature Verification Fails

**Causes**:
- Wrong signing secret
- Request timestamp too old
- Body has been modified

**Debug**:
```csharp
_logger.LogDebug("Expected signature: {Expected}, Received: {Received}", 
    expectedSignature, receivedSignature);
_logger.LogDebug("Timestamp: {Timestamp}, Current time: {Now}", 
    timestamp, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
```

#### 4. Messages Not Routing Back to Slack

**Cause**: Origin field not set correctly in outbound message.

**Solution**: Always include origin in outbound messages:
```csharp
Origin = $"app:slack:{appInstanceId}"
```

## Security Best Practices

1. **Always Verify Signatures**: Never process webhooks without verification
2. **Check Timestamp**: Prevent replay attacks by validating request age
3. **Encrypt Tokens**: Store bot tokens encrypted in database
4. **Use HTTPS**: Always use HTTPS for webhook endpoints
5. **Rate Limiting**: Implement rate limits per app instance
6. **Log Suspicious Activity**: Log failed signature verifications
7. **Rotate Tokens**: Periodically rotate bot tokens and signing secrets

## Performance Considerations

1. **Async Processing**: Respond to Slack within 3 seconds, process asynchronously
2. **Connection Pooling**: Use `HttpClient` from `IHttpClientFactory`
3. **Retry Logic**: Implement exponential backoff for Slack API calls
4. **Message Batching**: Batch multiple messages when possible
5. **Caching**: Cache app instance configurations

## Future Enhancements

1. **Interactive Components**: Handle button clicks, select menus, modals
2. **Slash Commands**: Support custom slash commands
3. **File Uploads**: Handle file attachments from Slack
4. **Threaded Conversations**: Better thread management
5. **User Mentions**: Parse and handle @mentions in messages
6. **Reactions**: Support emoji reactions
7. **Message Updates**: Edit previously sent messages
8. **Presence**: Handle user presence events

---

**Document Version**: 1.0  
**Last Updated**: 2026-02-04  
**Author**: Architecture Team  
**Status**: Implementation Ready
