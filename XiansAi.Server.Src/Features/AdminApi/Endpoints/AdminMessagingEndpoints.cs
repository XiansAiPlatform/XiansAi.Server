using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
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
            [FromServices] IMessageEventPublisher messageEventPublisher,
            [FromServices] IConversationRepository conversationRepository,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken,
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
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();

            // Construct the workflow ID as per specification:
            // {tenantId}:{agentName}:Supervisor Workflow:{activationName}
            var workflowId = WorkflowIdentifier.BuildSupervisorWorkflowId(tenantId, agentName, activationName);

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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Listen to Agent Activation Messages",
            Description = """
                Subscribe to real-time message events for a specific agent activation using Server-Sent Events (SSE).
                
                **Workflow ID Construction:**
                The workflow ID is automatically constructed as: `{tenantId}:{agentName}:Supervisor Workflow:{activationName}`
                
                **Query Parameters:**
                - `agentName` (required): Name of the agent
                - `activationName` (required): Name of the activation
                - `participantId` (required): Unique identifier for the participant
                - `heartbeatSeconds` (optional): Heartbeat interval in seconds (1-300, default: 60)
                
                **Behavior:**
                - Automatically creates a conversation thread if it doesn't exist
                - Streams all messages for the specified agent activation and participant
                - Sends periodic heartbeat events to keep the connection alive
                - Connection remains open until client disconnects or server shutdown
                
                **Response Format:**
                Server-Sent Events (SSE) stream with events in the following format:
                ```
                event: message
                data: {"type":"chat","text":"Hello","timestamp":"2024-01-01T00:00:00Z",...}
                
                event: heartbeat
                data: {"timestamp":"2024-01-01T00:00:05Z"}
                ```
                
                **Notes:**
                - Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header
                - Use EventSource API or similar SSE client to consume this endpoint
                """
        });

        // Unified send message endpoint
        adminMessagingGroup.MapPost("/send", async (
            string tenantId,
            [FromBody] AdminSendMessageRequest request,
            [FromServices] IMessageService messageService,
            HttpContext context) =>
        {
            // Construct the workflow ID as per specification:
            // {tenantId}:{agentName}:Supervisor Workflow:{activationName}
            var workflowId = WorkflowIdentifier.BuildSupervisorWorkflowId(tenantId, request.AgentName, request.ActivationName);
            
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
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Send Message to Agent Activation",
            Description = """
                Send a message to a specific agent activation.
                
                **Workflow ID Construction:**
                The workflow ID is automatically constructed as: `{tenantId}:{agentName}:Supervisor Workflow:{activationName}`
                
                **Request Body Fields:**
                - `agentName` (required): Name of the agent
                - `activationName` (required): Name of the activation
                - `participantId` (required): Unique identifier for the participant
                - `text` (required): The message text content
                - `data` (optional): Additional structured data (JSON object)
                - `topic` (optional): Topic for the message (stored as 'scope' in the message thread for organizing conversations)
                - `type` (optional): Message type - either 'Chat' or 'Data' (defaults to 'Chat')
                - `requestId` (optional): Unique request identifier (auto-generated GUID if not provided)
                - `hint` (optional): Hint for the agent to use when processing the message
                - `authorization` (optional): Authorization token (can also be provided via Authorization header)
                - `origin` (optional): Origin of the message
                
                **Message Types:**
                - `Chat`: Use for conversational messages (default). Set `text` field with the message content.
                - `Data`: Use for sending structured data to the workflow. Set `data` field with a JSON object and optionally include `text` for context.
                
                **Examples:**
                
                Sending a chat message:
                ```json
                {
                  "agentName": "CustomerSupport",
                  "activationName": "LiveChat",
                  "participantId": "user123",
                  "text": "Hello, I need help with my order",
                  "topic": "order-assistance"
                }
                ```
                
                Sending structured data:
                ```json
                {
                  "agentName": "DataProcessor",
                  "activationName": "Analytics",
                  "participantId": "system",
                  "text": "Processing order data",
                  "type": "Data",
                  "data": {
                    "orderId": "ORD-12345",
                    "status": "completed",
                    "amount": 99.99
                  }
                }
                ```
                
                **Notes:**
                - Tenant ID can be provided via route parameter (in URL) or X-Tenant-Id header
                - Authorization can be provided in the request body or via the Authorization header
                """
        });

        // Get topics (distinct scopes) for a specific agent activation and participant
        adminMessagingGroup.MapGet("/topics", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IMessageService messageService,
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
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();

            // Construct the workflow ID as per specification:
            // {tenantId}:{agentName}:Supervisor Workflow:{activationName}
            var workflowId = WorkflowIdentifier.BuildSupervisorWorkflowId(tenantId, agentName, activationName);

            var result = await messageService.GetTopicsByWorkflowAndParticipantAsync(workflowId, participantId, page, pageSize);
            return result.ToHttpResult();
        })
        .WithName("GetTopicsForAdminApi")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Topics (Scopes) for Agent Activation",
            Description = "Retrieves distinct scopes (topics) from message threads for a specific tenant, agent activation, and participant. The workflow ID is constructed as {tenantId}:{agentName}:Supervisor Workflow:{activationName}. Supports pagination with page and pageSize query parameters (defaults: page=1, pageSize=20)."
        });

        // Get message history for a specific agent activation and participant
        adminMessagingGroup.MapGet("/history", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IMessageService messageService,
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
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();

            // Validate sort order
            var normalizedSortOrder = sortOrder.ToLowerInvariant();
            if (normalizedSortOrder != "asc" && normalizedSortOrder != "desc")
            {
                return Results.BadRequest("sortOrder must be either 'asc' or 'desc'");
            }

            // Construct the workflow ID as per specification:
            // {tenantId}:{agentName}:Supervisor Workflow:{activationName}
            var workflowId = WorkflowIdentifier.BuildSupervisorWorkflowId(tenantId, agentName, activationName);

            // When topic is not provided (null) or empty, get messages with null scope (no topic)
            // When topic has a specific value, get messages with that specific scope
            var result = await messageService.GetThreadHistoryAsync(workflowId, participantId, page, pageSize, topic, chatOnly, normalizedSortOrder);
            return result.ToHttpResult();
        })
        .WithName("GetHistoryForAdminApi")
        .WithOpenApi(operation => new(operation)
        {
            Summary = "Get Message History for Agent Activation",
            Description = "Retrieves message history for a specific tenant, agent activation, and participant. The workflow ID is constructed as {tenantId}:{agentName}:Supervisor Workflow:{activationName}. Optional 'topic' parameter filters by scope (omit or leave empty to get messages with no scope/topic, or specify a topic name to get messages for that specific topic). Optional 'sortOrder' parameter controls sorting: 'desc' for newest first (default), 'asc' for oldest first. Supports pagination with page and pageSize query parameters (defaults: page=1, pageSize=50). Set chatOnly=true to retrieve only chat messages."
        });
    }
}

