using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Features.AppsApi.Models;
using Shared.Services;
using Shared.Repositories;
using Shared.Auth;

namespace Features.AppsApi.Handlers;

/// <summary>
/// Handles Slack webhook events and interactive components
/// </summary>
public interface ISlackWebhookHandler
{
    /// <summary>
    /// Process Slack Events API webhook
    /// </summary>
    Task<IResult> ProcessEventsWebhookAsync(
        AppIntegration integration,
        string rawBody,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Process Slack Interactive Components webhook
    /// </summary>
    Task<IResult> ProcessInteractiveWebhookAsync(
        AppIntegration integration,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify Slack webhook signature
    /// </summary>
    Task<bool> VerifySignatureAsync(
        AppIntegration integration,
        string body,
        HttpContext httpContext);

    /// <summary>
    /// Send outgoing message to Slack via incoming webhook or bot API
    /// </summary>
    Task SendMessageToSlackAsync(
        AppIntegration integration,
        ConversationMessage message,
        CancellationToken cancellationToken = default);
}

public class SlackWebhookHandler : ISlackWebhookHandler
{
    private readonly IMessageService _messageService;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SlackWebhookHandler> _logger;
    
    // Cache for Slack user info to avoid repeated API calls
    // Key format: "{integrationId}:{userId}"
    private static readonly ConcurrentDictionary<string, SlackUserInfo> UserInfoCache = new();

    public SlackWebhookHandler(
        IMessageService messageService,
        ITenantContext tenantContext,
        IHttpClientFactory httpClientFactory,
        ILogger<SlackWebhookHandler> logger)
    {
        _messageService = messageService;
        _tenantContext = tenantContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IResult> ProcessEventsWebhookAsync(
        AppIntegration integration,
        string rawBody,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Verify signature
            if (!await VerifySignatureAsync(integration, rawBody, httpContext))
            {
                _logger.LogWarning("Slack signature verification failed for integration {IntegrationId}", integration.Id);
                return Results.Unauthorized();
            }

            // Parse payload
            var payload = ParsePayload(rawBody);
            if (payload == null)
            {
                return Results.BadRequest("Invalid payload format");
            }

            // Handle URL verification challenge
            if (payload.Type == SlackConstants.UrlVerificationType && !string.IsNullOrEmpty(payload.Challenge))
            {
                _logger.LogInformation("Responding to Slack URL verification challenge for integration {IntegrationId}", 
                    integration.Id);
                return Results.Content(payload.Challenge, "text/plain");
            }

            // Handle event callbacks
            if (payload.Type == SlackConstants.EventCallbackType && payload.Event != null)
            {
                // Respond immediately to avoid Slack 3-second timeout
                // Process event asynchronously
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ProcessEventAsync(integration, payload, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing Slack event for integration {IntegrationId}", integration.Id);
                    }
                }, CancellationToken.None);

                return Results.Ok();
            }

            _logger.LogDebug("Unhandled Slack event type: {Type}", payload.Type);
            return Results.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Slack events webhook for integration {IntegrationId}", integration.Id);
            return Results.Problem("An error occurred processing the webhook", statusCode: 500);
        }
    }

    public async Task<IResult> ProcessInteractiveWebhookAsync(
        AppIntegration integration,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Read the form data (Slack sends interactive payloads as form data)
            var form = await httpContext.Request.ReadFormAsync(cancellationToken);
            var payloadJson = form["payload"].ToString();

            if (string.IsNullOrEmpty(payloadJson))
            {
                return Results.BadRequest("Missing payload");
            }

            // Verify signature
            if (!await VerifySignatureAsync(integration, payloadJson, httpContext))
            {
                _logger.LogWarning("Slack signature verification failed for interactive webhook, integration {IntegrationId}", 
                    integration.Id);
                return Results.Unauthorized();
            }

            // Process interactive payload asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await ProcessInteractivePayloadAsync(integration, payloadJson, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing Slack interactive payload for integration {IntegrationId}", 
                        integration.Id);
                }
            }, CancellationToken.None);

            return Results.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Slack interactive webhook for integration {IntegrationId}", integration.Id);
            return Results.Problem("An error occurred", statusCode: 500);
        }
    }

    public async Task<bool> VerifySignatureAsync(
        AppIntegration integration,
        string body,
        HttpContext httpContext)
    {
        if (!integration.Configuration.TryGetValue("signingSecret", out var secretObj) ||
            secretObj?.ToString() is not string signingSecret ||
            string.IsNullOrEmpty(signingSecret))
        {
            _logger.LogWarning("Slack signing secret not configured for integration {IntegrationId}", integration.Id);
            return false;
        }

        if (!httpContext.Request.Headers.TryGetValue("X-Slack-Signature", out var signature) ||
            !httpContext.Request.Headers.TryGetValue("X-Slack-Request-Timestamp", out var timestamp))
        {
            _logger.LogWarning("Missing Slack signature headers");
            return false;
        }

        // Check timestamp to prevent replay attacks (must be within 5 minutes)
        if (!long.TryParse(timestamp.ToString(), out var requestTime))
        {
            return false;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - requestTime) > 300)
        {
            _logger.LogWarning("Slack request timestamp too old: {RequestTime} vs {Now}", requestTime, now);
            return false;
        }

        // Compute expected signature
        var sigBaseString = $"v0:{timestamp}:{body}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingSecret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(sigBaseString));
        var expectedSignature = "v0=" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

        // Constant-time comparison
        var isValid = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(signature.ToString()),
            Encoding.UTF8.GetBytes(expectedSignature));

        if (!isValid)
        {
            _logger.LogWarning("Slack signature verification failed");
        }

        return await Task.FromResult(isValid);
    }

    public async Task SendMessageToSlackAsync(
        AppIntegration integration,
        ConversationMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending message to Slack for integration {IntegrationId}", integration.Id);

            // Extract Slack metadata from message
            var slackMetadata = ExtractSlackMetadata(message);

            if (string.IsNullOrEmpty(slackMetadata.Channel))
            {
                _logger.LogWarning("No Slack channel found in message metadata, cannot send message");
                return;
            }

            // Get incoming webhook URL or use bot token
            // Support both "incomingWebhookUrl" and common typo "incomingWekhookUrl"
            var hasIncomingWebhook = (integration.Configuration.TryGetValue("incomingWebhookUrl", out var webhookUrlObj) 
                || integration.Configuration.TryGetValue("incomingWekhookUrl", out webhookUrlObj)) 
                && !string.IsNullOrEmpty(webhookUrlObj?.ToString());
            var hasBotToken = integration.Configuration.TryGetValue("botToken", out var botTokenObj) 
                && !string.IsNullOrEmpty(botTokenObj?.ToString());

            if (hasIncomingWebhook)
            {
                await SendViaIncomingWebhookAsync(webhookUrlObj!.ToString()!, message, slackMetadata, cancellationToken);
            }
            else if (hasBotToken)
            {
                await SendViaBotApiAsync(botTokenObj!.ToString()!, message, slackMetadata, cancellationToken);
            }
            else
            {
                _logger.LogWarning("No outgoing method configured for Slack integration {IntegrationId} (need incomingWebhookUrl or botToken)",
                    integration.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to Slack for integration {IntegrationId}", integration.Id);
        }
    }

    #region Private Helper Methods

    private SlackWebhookPayload? ParsePayload(string rawBody)
    {
        try
        {
            return JsonSerializer.Deserialize<SlackWebhookPayload>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Slack payload");
            return null;
        }
    }

    private async Task ProcessEventAsync(
        AppIntegration integration,
        SlackWebhookPayload payload,
        CancellationToken cancellationToken)
    {
        var slackEvent = payload.Event;
        if (slackEvent == null)
        {
            _logger.LogDebug("Event is null in payload");
            return;
        }

        // CRITICAL: Filter out bot messages to prevent infinite loops
        // Check multiple indicators that this is a bot message
        if (IsBotMessage(slackEvent))
        {
            _logger.LogDebug("Ignoring bot message to prevent loop (subtype={Subtype}, botId={BotId}, appId={AppId})",
                slackEvent.Subtype, slackEvent.BotId, slackEvent.AppId);
            return;
        }

        // Filter out message edits and deletions
        if (slackEvent.Subtype == SlackConstants.MessageChangedSubtype || 
            slackEvent.Subtype == SlackConstants.MessageDeletedSubtype)
        {
            _logger.LogDebug("Ignoring message edit/deletion event");
            return;
        }

        // Only process message and app_mention events
        if (slackEvent.Type != SlackConstants.MessageEventType && 
            slackEvent.Type != SlackConstants.AppMentionEventType)
        {
            _logger.LogDebug("Ignoring event type: {Type}", slackEvent.Type);
            return;
        }

        var text = slackEvent.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("Empty message text, ignoring");
            return;
        }

        // Fetch user info from Slack if needed (based on configuration)
        SlackUserInfo? userInfo = null;
        if (!string.IsNullOrEmpty(slackEvent.User) && 
            integration.MappingConfig.ParticipantIdSource == "userEmail")
        {
            userInfo = await GetSlackUserInfoAsync(integration, slackEvent.User, cancellationToken);
        }

        // Determine participant ID based on mapping configuration
        var participantId = DetermineParticipantId(slackEvent, integration.MappingConfig, userInfo);

        // Determine scope based on mapping configuration
        var scope = DetermineScope(slackEvent, integration.MappingConfig);

        // Build chat request
        var chatRequest = new ChatOrDataRequest
        {
            WorkflowId = integration.WorkflowId,
            ParticipantId = participantId,
            Text = text,
            Data = new
            {
                slack = new
                {
                    userId = slackEvent.User,
                    userEmail = userInfo?.Profile?.Email,
                    userName = userInfo?.RealName ?? userInfo?.Name,
                    channel = slackEvent.Channel,
                    threadTs = slackEvent.ThreadTs,
                    parentUserId = slackEvent.ParentUserId,
                    ts = slackEvent.Ts,
                    teamId = payload.TeamId,
                    eventType = slackEvent.Type
                }
            },
            Scope = scope,
            Origin = $"app:slack:{integration.Id}",
            Type = MessageType.Chat
        };

        // Set tenant context from integration (required for MessageService validation)
        _tenantContext.TenantId = integration.TenantId;
        _tenantContext.LoggedInUser = "app:slack:" + integration.Id;

        _logger.LogInformation("Sending Slack message to workflow {WorkflowId} from participant {ParticipantId}",
            integration.WorkflowId, participantId);

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

    private async Task ProcessInteractivePayloadAsync(
        AppIntegration integration,
        string payloadJson,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Deserialize<SlackInteractivePayload>(payloadJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (payload == null)
            {
                _logger.LogWarning("Failed to parse interactive payload");
                return;
            }

            _logger.LogInformation("Processing Slack interactive payload of type {Type} for integration {IntegrationId}",
                payload.Type, integration.Id);

            // TODO: Implement interactive payload processing
            // This would parse the payload and send appropriate signals to the workflow
            // For example:
            // - Button clicks -> Send action to workflow
            // - Modal submissions -> Send form data to workflow
            // - Select menu selections -> Send selected values to workflow

            await Task.CompletedTask;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Slack interactive payload");
        }
    }

    /// <summary>
    /// Determines if a Slack event is from a bot (to prevent infinite loops)
    /// Uses only the most reliable indicators that distinguish bot messages from user messages.
    /// </summary>
    private bool IsBotMessage(SlackEvent slackEvent)
    {
        // Check 1: Subtype is explicitly "bot_message"
        // This is the most reliable indicator - Slack explicitly marks bot messages
        if (slackEvent.Subtype == SlackConstants.BotMessageSubtype)
        {
            _logger.LogDebug("Bot detected: subtype is bot_message");
            return true;
        }

        // Check 2: Event has a bot_id field (indicates message from a bot)
        // When a bot sends a message, Slack includes the bot_id
        if (!string.IsNullOrEmpty(slackEvent.BotId))
        {
            _logger.LogDebug("Bot detected: bot_id is present ({BotId})", slackEvent.BotId);
            return true;
        }

        // NOTE: We do NOT check app_id or authorizations.is_bot because:
        // - app_id can appear in user messages within the app's context
        // - authorizations.is_bot refers to the APP's authorization, not the message sender
        // - These checks incorrectly filter out legitimate user messages
        
        // The above 2 checks (subtype and bot_id) are sufficient and reliable

        return false;
    }

    private string DetermineParticipantId(
        SlackEvent slackEvent, 
        AppIntegrationMappingConfig config, 
        SlackUserInfo? userInfo)
    {
        if (string.IsNullOrEmpty(config.ParticipantIdSource))
        {
            return config.DefaultParticipantId ?? slackEvent.User ?? "unknown";
        }

        return config.ParticipantIdSource switch
        {
            "userEmail" => userInfo?.Profile?.Email ?? slackEvent.User ?? config.DefaultParticipantId ?? "unknown",
            "userId" => slackEvent.User ?? config.DefaultParticipantId ?? "unknown",
            "channelId" => slackEvent.Channel ?? config.DefaultParticipantId ?? "unknown",
            "threadId" => slackEvent.ThreadTs ?? slackEvent.Channel ?? config.DefaultParticipantId ?? "unknown",
            _ => config.DefaultParticipantId ?? slackEvent.User ?? "unknown"  // Fall back to defaultParticipantId for unknown values
        };
    }

    private string? DetermineScope(SlackEvent slackEvent, AppIntegrationMappingConfig config)
    {
        if (string.IsNullOrEmpty(config.ScopeSource))
        {
            return config.DefaultScope;
        }

        return config.ScopeSource switch
        {
            "channelId" => slackEvent.Channel,
            "threadId" => slackEvent.ThreadTs,
            _ => config.DefaultScope  // Fall back to defaultScope for unknown values
        };
    }

    /// <summary>
    /// Fetches user information from Slack API including email address.
    /// Results are cached to avoid repeated API calls.
    /// Requires botToken in integration configuration and users:read, users:read.email scopes.
    /// </summary>
    private async Task<SlackUserInfo?> GetSlackUserInfoAsync(
        AppIntegration integration,
        string userId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(userId))
        {
            return null;
        }

        // Check cache first
        var cacheKey = $"{integration.Id}:{userId}";
        if (UserInfoCache.TryGetValue(cacheKey, out var cachedUserInfo))
        {
            _logger.LogDebug("Using cached user info for user {UserId}", userId);
            return cachedUserInfo;
        }

        // Get bot token from configuration
        if (!integration.Configuration.TryGetValue("botToken", out var botTokenObj) ||
            botTokenObj?.ToString() is not string botToken ||
            string.IsNullOrEmpty(botToken))
        {
            _logger.LogDebug("Bot token not configured for integration {IntegrationId}, cannot fetch user info", 
                integration.Id);
            return null;
        }

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var requestUrl = $"{SlackConstants.SlackApiBaseUrl}/users.info?user={Uri.EscapeDataString(userId)}";
            
            var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            request.Headers.Add("Authorization", $"Bearer {botToken}");

            var response = await httpClient.SendAsync(request, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch Slack user info for user {UserId}: {StatusCode}", 
                    userId, response.StatusCode);
                return null;
            }

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var userInfoResponse = JsonSerializer.Deserialize<SlackUserInfoResponse>(responseContent, 
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (userInfoResponse?.Ok == true && userInfoResponse.User != null)
            {
                // Cache the result
                UserInfoCache.TryAdd(cacheKey, userInfoResponse.User);
                
                _logger.LogDebug("Successfully fetched and cached user info for user {UserId}", userId);
                return userInfoResponse.User;
            }
            else
            {
                _logger.LogWarning("Slack API returned error for user {UserId}: {Error}", 
                    userId, userInfoResponse?.Error ?? "unknown");
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching Slack user info for user {UserId}", userId);
            return null;
        }
    }

    private SlackMessageMetadata ExtractSlackMetadata(ConversationMessage message)
    {
        var metadata = new SlackMessageMetadata();

        if (message.Data != null)
        {
            try
            {
                var dataJson = JsonSerializer.Serialize(message.Data);
                using var doc = JsonDocument.Parse(dataJson);
                
                if (doc.RootElement.TryGetProperty("slack", out var slackData))
                {
                    if (slackData.TryGetProperty("channel", out var channel))
                    {
                        metadata.Channel = channel.GetString();
                    }
                    if (slackData.TryGetProperty("threadTs", out var threadTs))
                    {
                        metadata.ThreadTs = threadTs.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract Slack metadata from message data");
            }
        }

        return metadata;
    }

    private async Task SendViaIncomingWebhookAsync(
        string webhookUrl,
        ConversationMessage message,
        SlackMessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();

            var slackMessage = new
            {
                text = message.Text ?? "No message text",
                thread_ts = metadata.ThreadTs
            };

            var jsonContent = JsonSerializer.Serialize(slackMessage);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending message to Slack incoming webhook (thread: {ThreadTs})", metadata.ThreadTs);

            var response = await httpClient.PostAsync(webhookUrl, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully sent message to Slack via incoming webhook");
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to send message to Slack: {StatusCode} - {Error}",
                    response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to Slack via incoming webhook");
        }
    }

    private async Task SendViaBotApiAsync(
        string botToken,
        ConversationMessage message,
        SlackMessageMetadata metadata,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("SlackApi");

            var slackMessage = new Dictionary<string, object>
            {
                ["channel"] = metadata.Channel ?? throw new InvalidOperationException("Channel is required"),
                ["text"] = message.Text ?? "No message text"
            };

            // Include thread_ts to maintain conversation threading
            if (!string.IsNullOrEmpty(metadata.ThreadTs))
            {
                slackMessage["thread_ts"] = metadata.ThreadTs;
            }

            var jsonContent = JsonSerializer.Serialize(slackMessage);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var request = new HttpRequestMessage(HttpMethod.Post, "chat.postMessage")
            {
                Content = content
            };
            request.Headers.Add("Authorization", $"Bearer {botToken}");

            _logger.LogInformation("Sending message to Slack API (channel: {Channel}, thread: {ThreadTs})",
                metadata.Channel, metadata.ThreadTs);

            var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var apiResponse = JsonSerializer.Deserialize<SlackApiResponse>(responseBody);
                if (apiResponse?.Ok == true)
                {
                    _logger.LogInformation("Successfully sent message to Slack via Bot API");
                }
                else
                {
                    _logger.LogError("Slack API returned error: {Error}", apiResponse?.Error);
                }
            }
            else
            {
                _logger.LogError("Failed to send message to Slack API: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to Slack via Bot API");
        }
    }

    #endregion
}

/// <summary>
/// Metadata extracted from message for Slack routing
/// </summary>
internal class SlackMessageMetadata
{
    public string? Channel { get; set; }
    public string? ThreadTs { get; set; }
}

/// <summary>
/// Slack API response
/// </summary>
internal class SlackApiResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("error")]
    public string? Error { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("ts")]
    public string? Ts { get; set; }
}

