using Microsoft.AspNetCore.Mvc;
using Features.UserApi.Services;
using Features.WebApi.Utils;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Utils;
using System.Text.Json.Serialization;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Logger class for AdminApi messaging endpoints.
/// </summary>
public class AdminMessagingEndpointsLogger { }

/// <summary>
/// Request model for sending messages to an agent activation.
/// </summary>
public class AdminSendMessageRequest
{
    public required string AgentName { get; set; }
    public required string ActivationName { get; set; }
    /// <summary>Optional. Defaults to "Supervisor Workflow"</summary>
    public string? WorkflowType { get; set; }
    public required string ParticipantId { get; set; }
    public required string Text { get; set; }
    public object? Data { get; set; }
    public string? Topic { get; set; }
    public string? RequestId { get; set; }
    public string? Hint { get; set; }
    public string? Authorization { get; set; }
    public string? Origin { get; set; }
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageType? Type { get; set; }
}

/// <summary>
/// AdminApi endpoints for messaging and communication.
/// These are administrative operations for managing real-time messaging via Server-Sent Events.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminMessagingEndpoints
{
    private static void SetAuthorizationFromHeader(ChatOrDataRequest request, HttpContext context)
    {
        if (request.Authorization == null)
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            var (success, token) = AuthorizationHeaderHelper.ExtractBearerToken(authHeader);
            
            if (success && token != null)
            {
                request.Authorization = token;
            }
        }
    }

    /// <summary>
    /// Maps all AdminApi messaging endpoints.
    /// </summary>
    public static void MapAdminMessagingEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminMessagingGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/messaging")
            .WithTags("AdminAPI - Messaging")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        // Server-Sent Events endpoint for real-time message streaming
        adminMessagingGroup.MapGet("/listen", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageEventPublisher messageEventPublisher,
            [FromServices] IConversationRepository conversationRepository,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken,
            [FromQuery] string? workflowType = null,
            [FromQuery] int heartbeatSeconds = 60) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            // Construct the workflow ID. Default to Supervisor Workflow for backward compatibility.
            var effectiveWorkflowType = string.IsNullOrWhiteSpace(workflowType) ? "Supervisor Workflow" : workflowType.Trim();

            // Validate activation exists and is active, and optionally that the agent has this workflow type registered
            var validationResult = await activationValidationService.ValidateActivationAsync(tenantId, agentName, activationName, effectiveWorkflowType);
            if (!validationResult.IsSuccess)
            {
                return validationResult.ToHttpResult();
            }
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();

            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            // Create or get the thread
            var thread = new ConversationThread
            {
                TenantId = tenantId,
                WorkflowId = workflowId,
                Agent = agentName,
                ParticipantId = participantId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = tenantContext.LoggedInUser,
                Status = ConversationThreadStatus.Active
            };

            var threadId = await conversationRepository.CreateOrGetThreadIdAsync(thread);

            // Create logger from factory
            var logger = loggerFactory.CreateLogger<AdminMessagingEndpointsLogger>();

            // Use the SSE stream handler to manage the entire connection lifecycle
            var streamHandler = new WebApiSSEStreamHandler(
                messageEventPublisher, 
                logger, 
                context, 
                threadId, 
                tenantContext, 
                cancellationToken,
                TimeSpan.FromSeconds(Math.Max(1, Math.Min(heartbeatSeconds, 300)))); // Between 1-300 seconds

            return await streamHandler.HandleStreamAsync();
        })
        .WithName("ListenToAgentActivationMessagesForAdminApi")
        ;

        // Unified send message endpoint
        adminMessagingGroup.MapPost("/send", async (
            string tenantId,
            [FromBody] AdminSendMessageRequest request,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            HttpContext context) =>
        {
            // Construct the workflow ID. Default to Supervisor Workflow for backward compatibility.
            var effectiveWorkflowType = string.IsNullOrWhiteSpace(request.WorkflowType) ? "Supervisor Workflow" : request.WorkflowType.Trim();

            // Validate activation exists and is active, and that the agent has this workflow type registered
            var validationResult = await activationValidationService.ValidateActivationAsync(
                tenantId, request.AgentName, request.ActivationName, effectiveWorkflowType);
            if (!validationResult.IsSuccess)
            {
                return validationResult.ToHttpResult();
            }
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, request.AgentName, effectiveWorkflowType, request.ActivationName);
            
            // Default to Chat if type not specified
            var messageType = request.Type ?? MessageType.Chat;
            
            // Normalize participantId to lowercase (typically an email)
            var normalizedParticipantId = request.ParticipantId.ToLowerInvariant();
            
            // Map to ChatOrDataRequest
            var chatOrDataRequest = new ChatOrDataRequest
            {
                WorkflowId = workflowId,
                ParticipantId = normalizedParticipantId,
                Text = request.Text,
                Data = request.Data,
                Scope = request.Topic,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Hint = request.Hint,
                Authorization = request.Authorization,
                Origin = request.Origin,
                Type = messageType
            };
            
            // Use authorization from header if not in body
            SetAuthorizationFromHeader(chatOrDataRequest, context);
            
            var result = await messageService.ProcessIncomingMessage(chatOrDataRequest, messageType);
            return result.ToHttpResult();
        })
        .WithName("SendMessageForAdminApi")
        ;

        // Get topics (distinct scopes) for a specific agent activation and participant
        adminMessagingGroup.MapGet("/topics", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromQuery] string? workflowType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            // Construct the workflow ID. Default to Supervisor Workflow for backward compatibility.
            var effectiveWorkflowType = string.IsNullOrWhiteSpace(workflowType) ? "Supervisor Workflow" : workflowType.Trim();

            // Validate activation exists and is active, and that the agent has this workflow type registered
            var validationResult = await activationValidationService.ValidateActivationAsync(tenantId, agentName, activationName, effectiveWorkflowType);
            if (!validationResult.IsSuccess)
            {
                return validationResult.ToHttpResult();
            }
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            var result = await messageService.GetTopicsByWorkflowAndParticipantAsync(workflowId, participantId, page, pageSize);
            return result.ToHttpResult();
        })
        .WithName("GetTopicsForAdminApi")
        ;

        // Get message history for a specific agent activation and participant
        adminMessagingGroup.MapGet("/history", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromQuery] string? workflowType = null,
            [FromQuery] string? topic = null,
            [FromQuery] string sortOrder = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] bool chatOnly = false) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            // Construct the workflow ID. Default to Supervisor Workflow for backward compatibility.
            var effectiveWorkflowType = string.IsNullOrWhiteSpace(workflowType) ? "Supervisor Workflow" : workflowType.Trim();

            // Validate activation exists and is active, and that the agent has this workflow type registered
            var validationResult = await activationValidationService.ValidateActivationAsync(tenantId, agentName, activationName, effectiveWorkflowType);
            if (!validationResult.IsSuccess)
            {
                return validationResult.ToHttpResult();
            }
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();

            // Validate sort order
            var normalizedSortOrder = sortOrder.ToLowerInvariant();
            if (normalizedSortOrder != "asc" && normalizedSortOrder != "desc")
            {
                return Results.BadRequest("sortOrder must be either 'asc' or 'desc'");
            }
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            // When topic is not provided (null) or empty, get messages with null scope (no topic)
            // When topic has a specific value, get messages with that specific scope
            var result = await messageService.GetThreadHistoryAsync(workflowId, participantId, page, pageSize, topic, chatOnly, normalizedSortOrder);
            return result.ToHttpResult();
        })
        .WithName("GetHistoryForAdminApi")
        ;

        // Delete messages by topic for a specific agent activation and participant
        adminMessagingGroup.MapDelete("/messages", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromQuery] string? workflowType = null,
            [FromQuery] string? topic = null) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            // Construct the workflow ID. Default to Supervisor Workflow for backward compatibility.
            var effectiveWorkflowType = string.IsNullOrWhiteSpace(workflowType) ? "Supervisor Workflow" : workflowType.Trim();

            // Validate activation exists and is active, and that the agent has this workflow type registered
            var validationResult = await activationValidationService.ValidateActivationAsync(tenantId, agentName, activationName, effectiveWorkflowType);
            if (!validationResult.IsSuccess)
            {
                return validationResult.ToHttpResult();
            }
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();
            
            // Normalize topic: empty string should be treated as null
            var normalizedTopic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            // Delete messages with the specified topic/scope
            // When topic is null or empty, delete messages with null scope (no topic)
            // When topic has a value, delete messages with that specific scope
            var result = await messageService.DeleteMessagesByTopicAsync(workflowId, participantId, normalizedTopic);
            return result.ToHttpResult();
        })
        .WithName("DeleteMessagesByTopicForAdminApi")
        ;
    }
}

