using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Auth;
using System.Security.Permissions;
using Shared.Utils;
using System.Text.Json;

namespace Features.AgentApi.Endpoints
{
    public class ConversationHistoryQuery
    {
        public string? ThreadId { get; set; }
        public string? WorkflowType { get; set; }
        public string? WorkflowId { get; set; }
        public string ParticipantId { get; set; } = string.Empty;
        public string? Scope { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public static class ConversationEndpoints
    {
        private static void SetAuthorizationFromHeader(HandoffRequest request, HttpContext context)
        {
            if (request.Authorization == null)
            {
                var authHeader = context.Request.Headers["Authorization"].ToString();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer "))
                {
                    request.Authorization = authHeader.Substring("Bearer ".Length).Trim();
                }
            }
        }

        public static void MapConversationEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/agent/conversation")
                .WithTags("AgentAPI - Conversation")
                .RequiresCertificate();

            group.MapGet("/history", GetConversationHistory)
            .WithName("Get Conversation History")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Get conversation history";
                operation.Description = "Gets the conversation history for a given conversation thread with pagination support";
                return operation;
            });

            group.MapPost("/outbound/chat", async (
                [FromBody] ChatOrDataRequest request,
                [FromServices] IMessageService messageService) =>
            {
                var result = await messageService.ProcessOutgoingMessage(request, MessageType.Chat);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Chat from Agent")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process outbound chat from Agent";
                operation.Description = "Processes an outbound chat for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/data", async (
                [FromBody] ChatOrDataRequest request,
                [FromServices] IMessageService messageService) =>
            {
                var result = await messageService.ProcessOutgoingMessage(request, MessageType.Data);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Data from Agent")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process outbound data from Agent";
                operation.Description = "Processes an outbound data for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/handoff", async (
                [FromBody] HandoffRequest request,
                [FromServices] IMessageService messageService,
                HttpContext context) =>
            {
                SetAuthorizationFromHeader(request, context);
                var result = await messageService.ProcessHandoff(request);
                return result.ToHttpResult();
            })
            .WithName("Process Handover Message from Agent")
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process handover message from Agent";
                operation.Description = "Processes a handover message for agent conversations and returns the result";
                return operation;
            });

            // New synchronous endpoint that waits for responses
            group.MapPost("/converse", async (
                [FromQuery] string type,
                [FromBody] ChatOrDataRequest chatOrDataRequest,
                [FromServices] IMessageService messageService,
                [FromServices] ITenantContext tenantContext,
                [FromServices] IPendingRequestService pendingRequestService,
                HttpContext context,
                [FromQuery] int timeoutSeconds = 60,
                [FromQuery] string? requestId = null) =>
            {
                var workflow = chatOrDataRequest.WorkflowId;
                if (string.IsNullOrEmpty(workflow))
                {
                    return Results.BadRequest("Workflow Id not provided");
                }
                var participantId = chatOrDataRequest.ParticipantId;
             
                var text = chatOrDataRequest.Text;
                string jsonString = JsonSerializer.Serialize(chatOrDataRequest.Data);

                // Parse the JSON string to a JsonDocument
                using JsonDocument request = JsonDocument.Parse(jsonString);

                workflow = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;

                // Validate request parameters
                var (isValid, errorMessage) = MessageRequestValidator.ValidateSyncRequest(
                    workflow, type, timeoutSeconds, out var messageType);

                if (!isValid)
                {
                    return Results.BadRequest(errorMessage);
                }

                var resolvedParticipantId = string.IsNullOrEmpty(participantId) ? tenantContext.LoggedInUser : participantId;

                // Generate unique request ID for correlation
                if (string.IsNullOrEmpty(requestId))
                {
                    requestId = MessageRequestProcessor.GenerateRequestId(workflow, resolvedParticipantId);
                }

                // Create the request using the processor utility
                var chat = MessageRequestProcessor.CreateRequest(
                    messageType,
                    workflow,
                    resolvedParticipantId,
                    request.RootElement.Clone(),
                    text,
                    requestId);

                // Use the sync message handler to process the complex flow
                var syncHandler = new SyncMessageHandler(messageService, pendingRequestService);
                return await syncHandler.ProcessSyncMessageAsync(
                    chat,
                    messageType,
                    timeoutSeconds,
                    context.RequestAborted);
            })
                .WithName("Send Data or Chat to workflow and wait for response from Agent")
                .WithOpenApi(operation =>
                {
                    operation.Summary = "Send Data or Chat to workflow and wait for response from Agent";
                    operation.Description = "Send data to a workflow and wait synchronously for the response. Requires timeoutSeconds and type as query parameters. Messages can be pass as ChatOrDataRequest in request body. Optional timeoutSeconds (default: 60, max: 300).";
                    return operation;
                });
        }

        private static async Task<IResult> GetConversationHistory(
            [AsParameters] ConversationHistoryQuery query,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IMessageService messageService)
        {
            if (string.IsNullOrEmpty(query.WorkflowType) && string.IsNullOrEmpty(query.WorkflowId)) {
                return ServiceResult<List<ConversationMessage>>.BadRequest("WorkflowType or WorkflowId is required").ToHttpResult();
            }
            
            if (query.WorkflowId == null) {
                query.WorkflowId = $"{tenantContext.TenantId}:{query.WorkflowType}";
            }

            if (string.IsNullOrEmpty(query.ThreadId)) {
                var result = await messageService.GetThreadHistoryAsync(query.WorkflowId, query.ParticipantId, query.Page, query.PageSize, query.Scope);
                return result.ToHttpResult();
            } else {
                var result = await messageService.GetThreadHistoryAsync(query.ThreadId, query.Page, query.PageSize);
                return result.ToHttpResult();
            }
            
        }
    }
} 