using System.Text;
using System.Text.Json;
using Features.AppsApi.Models;
using Shared.Services;
using Shared.Repositories;
using Shared.Auth;
using System.Text.Json.Serialization;

namespace Features.AppsApi.Handlers;

/// <summary>
/// Handles Microsoft Teams Bot Framework webhooks and activities
/// </summary>
public interface ITeamsWebhookHandler
{
    /// <summary>
    /// Process Teams Bot Framework activity webhook
    /// </summary>
    Task<IResult> ProcessActivityWebhookAsync(
        AppIntegration integration,
        string rawBody,
        HttpContext httpContext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify Teams Bot Framework JWT authentication
    /// </summary>
    Task<bool> VerifyAuthenticationAsync(
        AppIntegration integration,
        HttpContext httpContext);

    /// <summary>
    /// Send outgoing message to Teams
    /// </summary>
    Task SendMessageToTeamsAsync(
        AppIntegration integration,
        ConversationMessage message,
        CancellationToken cancellationToken = default);
}

public class TeamsWebhookHandler : ITeamsWebhookHandler
{
    private readonly IMessageService _messageService;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<TeamsWebhookHandler> _logger;

    public TeamsWebhookHandler(
        IMessageService messageService,
        ITenantContext tenantContext,
        IHttpClientFactory httpClientFactory,
        ILogger<TeamsWebhookHandler> logger)
    {
        _messageService = messageService;
        _tenantContext = tenantContext;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IResult> ProcessActivityWebhookAsync(
        AppIntegration integration,
        string rawBody,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing Teams activity webhook for integration {IntegrationId}", integration.Id);

            // Parse activity
            var activity = ParseActivity(rawBody);
            if (activity == null)
            {
                return Results.BadRequest("Invalid activity format");
            }

            // Verify authentication (Bot Framework JWT)
            if (!await VerifyAuthenticationAsync(integration, httpContext))
            {
                _logger.LogWarning("Teams authentication failed for integration {IntegrationId}", integration.Id);
                return Results.Unauthorized();
            }

            // Handle different activity types
            return activity.Type switch
            {
                TeamsConstants.MessageActivityType => await ProcessMessageActivityAsync(integration, activity, cancellationToken),
                TeamsConstants.ConversationUpdateActivityType => ProcessConversationUpdateActivity(activity),
                TeamsConstants.InvokeActivityType => await ProcessInvokeActivityAsync(integration, activity, cancellationToken),
                _ => HandleUnknownActivityType(activity)
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Teams activity webhook");
            return Results.Problem("An error occurred processing the webhook", statusCode: 500);
        }
    }

    public async Task<bool> VerifyAuthenticationAsync(
        AppIntegration integration,
        HttpContext httpContext)
    {
        // TODO: Implement Bot Framework JWT validation
        // For now, we'll use basic app ID validation
        // Production should verify JWT signature from Azure Bot Service

        if (!httpContext.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            _logger.LogWarning("Missing Authorization header for Teams webhook");
            return false;
        }

        // Basic validation - in production, verify JWT signature
        if (!authHeader.ToString().StartsWith("Bearer "))
        {
            _logger.LogWarning("Invalid Authorization header format");
            return false;
        }

        // TODO: Verify JWT token signature using Microsoft's public keys
        // For now, accept all authenticated requests
        _logger.LogDebug("Teams authentication validated (basic check - TODO: implement full JWT validation)");
        
        return await Task.FromResult(true);
    }

    public async Task SendMessageToTeamsAsync(
        AppIntegration integration,
        ConversationMessage message,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Sending message to Teams for integration {IntegrationId}", integration.Id);

            // Extract Teams metadata from message
            var teamsMetadata = ExtractTeamsMetadata(message);

            if (string.IsNullOrEmpty(teamsMetadata.ServiceUrl) || string.IsNullOrEmpty(teamsMetadata.ConversationId))
            {
                _logger.LogWarning("No Teams conversation info found in message metadata, cannot send message");
                return;
            }

            // Get app credentials
            if (!integration.Configuration.TryGetValue("appId", out var appIdObj) ||
                !integration.Configuration.TryGetValue("appPassword", out var appPasswordObj))
            {
                _logger.LogWarning("Teams appId or appPassword not configured for integration {IntegrationId}", integration.Id);
                return;
            }

            var appId = appIdObj?.ToString();
            var appPassword = appPasswordObj?.ToString();

            // Get access token from Bot Framework
            var accessToken = await GetBotFrameworkTokenAsync(appId!, appPassword!, cancellationToken);
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogError("Failed to get Bot Framework access token");
                return;
            }

            // Build Teams activity response
            var responseActivity = BuildTeamsResponse(message, teamsMetadata);

            // Send to Teams Bot Framework API
            await SendActivityToTeamsAsync(
                teamsMetadata.ServiceUrl!,
                teamsMetadata.ConversationId!,
                responseActivity,
                accessToken,
                cancellationToken);

            _logger.LogInformation("Successfully sent message to Teams conversation {ConversationId}", teamsMetadata.ConversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to Teams for integration {IntegrationId}", integration.Id);
        }
    }

    #region Private Helper Methods

    private TeamsActivity? ParseActivity(string rawBody)
    {
        try
        {
            return JsonSerializer.Deserialize<TeamsActivity>(rawBody, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse Teams activity");
            return null;
        }
    }

    private async Task<IResult> ProcessMessageActivityAsync(
        AppIntegration integration,
        TeamsActivity activity,
        CancellationToken cancellationToken)
    {
        // Filter out bot's own messages to prevent loops
        if (IsBotMessage(activity))
        {
            _logger.LogDebug("Ignoring bot message to prevent loop");
            return Results.Ok();
        }

        var text = activity.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _logger.LogDebug("Empty message text, ignoring");
            return Results.Ok();
        }

        // Determine participant ID
        var participantId = DetermineParticipantId(activity, integration.MappingConfig);

        // Determine scope
        var scope = DetermineScope(activity, integration.MappingConfig);

        // Set tenant context from integration
        _tenantContext.TenantId = integration.TenantId;
        _tenantContext.LoggedInUser = "app:msteams:" + integration.Id;

        // Build chat request
        var chatRequest = new ChatOrDataRequest
        {
            WorkflowId = integration.WorkflowId,
            ParticipantId = participantId,
            Text = text,
            Data = new
            {
                teams = new
                {
                    activityId = activity.Id,
                    conversationId = activity.Conversation?.Id,
                    serviceUrl = activity.ServiceUrl,
                    userId = activity.From?.Id,
                    userName = activity.From?.Name,
                    channelId = activity.ChannelData?.TeamsChannelId,
                    teamId = activity.ChannelData?.TeamsTeamId,
                    tenantId = activity.ChannelData?.Tenant?.Id,
                    conversationType = activity.Conversation?.ConversationType
                }
            },
            Scope = scope,
            Origin = $"app:msteams:{integration.Id}",
            Type = MessageType.Chat
        };

        _logger.LogInformation("Sending Teams message to workflow {WorkflowId} from participant {ParticipantId}",
            integration.WorkflowId, participantId);

        var result = await _messageService.ProcessIncomingMessage(chatRequest, MessageType.Chat);

        if (result.IsSuccess)
        {
            _logger.LogInformation("Successfully sent Teams message to agent workflow");
        }
        else
        {
            _logger.LogError("Failed to send Teams message to agent: {Error}", result.ErrorMessage);
        }

        // Teams expects a 200 OK response
        return Results.Ok();
    }

    private IResult ProcessConversationUpdateActivity(TeamsActivity activity)
    {
        // Handle bot added/removed from conversation, members added/removed, etc.
        _logger.LogInformation("Received conversation update activity: {ActivityId}", activity.Id);
        return Results.Ok();
    }

    private async Task<IResult> ProcessInvokeActivityAsync(
        AppIntegration integration,
        TeamsActivity activity,
        CancellationToken cancellationToken)
    {
        // Handle adaptive card actions, task module submissions, etc.
        _logger.LogInformation("Received invoke activity: {ActivityId}", activity.Id);
        
        // TODO: Implement invoke activity processing for adaptive card actions
        
        return await Task.FromResult(Results.Ok());
    }

    private IResult HandleUnknownActivityType(TeamsActivity activity)
    {
        _logger.LogDebug("Unhandled activity type: {Type}", activity.Type);
        return Results.Ok();
    }

    private bool IsBotMessage(TeamsActivity activity)
    {
        // Check if message is from a bot (to prevent loops)
        if (activity.From?.Role == "bot")
        {
            return true;
        }

        // If recipient is the same as sender, it's likely an echo
        if (activity.From?.Id == activity.Recipient?.Id)
        {
            return true;
        }

        return false;
    }

    private string DetermineParticipantId(TeamsActivity activity, AppIntegrationMappingConfig config)
    {
        return config.ParticipantIdSource switch
        {
            "userId" => activity.From?.Id ?? config.DefaultParticipantId ?? "unknown",
            "email" => activity.From?.AadObjectId ?? activity.From?.Id ?? config.DefaultParticipantId ?? "unknown",
            "channelId" => activity.ChannelData?.TeamsChannelId ?? activity.Conversation?.Id ?? config.DefaultParticipantId ?? "unknown",
            _ => activity.From?.Id ?? config.DefaultParticipantId ?? "unknown"
        };
    }

    private string? DetermineScope(TeamsActivity activity, AppIntegrationMappingConfig config)
    {
        if (string.IsNullOrEmpty(config.ScopeSource))
        {
            return config.DefaultScope;
        }

        return config.ScopeSource switch
        {
            "channelId" => activity.ChannelData?.TeamsChannelId,
            "teamId" => activity.ChannelData?.TeamsTeamId,
            "channelName" => activity.ChannelData?.Channel?.Name,
            _ => config.DefaultScope
        };
    }

    private TeamsMessageMetadata ExtractTeamsMetadata(ConversationMessage message)
    {
        var metadata = new TeamsMessageMetadata();

        if (message.Data != null)
        {
            try
            {
                var dataJson = JsonSerializer.Serialize(message.Data);
                using var doc = JsonDocument.Parse(dataJson);

                if (doc.RootElement.TryGetProperty("teams", out var teamsData))
                {
                    if (teamsData.TryGetProperty("serviceUrl", out var serviceUrl))
                    {
                        metadata.ServiceUrl = serviceUrl.GetString();
                    }
                    if (teamsData.TryGetProperty("conversationId", out var conversationId))
                    {
                        metadata.ConversationId = conversationId.GetString();
                    }
                    if (teamsData.TryGetProperty("activityId", out var activityId))
                    {
                        metadata.ActivityId = activityId.GetString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract Teams metadata from message data");
            }
        }

        return metadata;
    }

    private async Task<string?> GetBotFrameworkTokenAsync(string appId, string appPassword, CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            
            var tokenRequest = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", appId),
                new KeyValuePair<string, string>("client_secret", appPassword),
                new KeyValuePair<string, string>("scope", "https://api.botframework.com/.default")
            });

            var response = await httpClient.PostAsync(
                "https://login.microsoftonline.com/botframework.com/oauth2/v2.0/token",
                tokenRequest,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("Failed to get Bot Framework token: {StatusCode} - {Error}", response.StatusCode, error);
                return null;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            var tokenResponse = JsonSerializer.Deserialize<BotFrameworkTokenResponse>(responseBody);

            return tokenResponse?.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting Bot Framework access token");
            return null;
        }
    }

    private TeamsResponse BuildTeamsResponse(ConversationMessage message, TeamsMessageMetadata metadata)
    {
        return new TeamsResponse
        {
            Type = TeamsConstants.MessageActivityType,
            Text = message.Text ?? "No message text"
        };
    }

    private async Task SendActivityToTeamsAsync(
        string serviceUrl,
        string conversationId,
        TeamsResponse activity,
        string accessToken,
        CancellationToken cancellationToken)
    {
        try
        {
            var httpClient = _httpClientFactory.CreateClient("TeamsApi");
            
            var url = $"{serviceUrl.TrimEnd('/')}/v3/conversations/{conversationId}/activities";
            
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {accessToken}");
            
            var jsonContent = JsonSerializer.Serialize(activity);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            _logger.LogInformation("Sending activity to Teams conversation {ConversationId}", conversationId);

            var response = await httpClient.SendAsync(request, cancellationToken);
            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Successfully sent message to Teams");
            }
            else
            {
                _logger.LogError("Failed to send message to Teams: {StatusCode} - {Response}",
                    response.StatusCode, responseBody);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending activity to Teams");
        }
    }

    #endregion
}

/// <summary>
/// Metadata extracted from message for Teams routing
/// </summary>
internal class TeamsMessageMetadata
{
    public string? ServiceUrl { get; set; }
    public string? ConversationId { get; set; }
    public string? ActivityId { get; set; }
}

/// <summary>
/// Bot Framework OAuth token response
/// </summary>
internal class BotFrameworkTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }
}
