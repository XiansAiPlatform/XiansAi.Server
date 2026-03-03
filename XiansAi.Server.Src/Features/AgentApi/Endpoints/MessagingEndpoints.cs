using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Auth;
using Shared.Utils;
using System.Text.Json;
using Features.Shared.Configuration;

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
        private static readonly ILogger<ConversationHistoryQuery> _logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<ConversationHistoryQuery>();
        private static void SetAuthorizationFromHeader(HandoffRequest request, HttpContext context)
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

        public static void MapConversationEndpoints(WebApplication app)
        {
            var group = app.MapGroup("/api/agent/conversation")
                .WithTags("AgentAPI - Conversation")
                .RequiresCertificate()
                .WithAgentUserApiRateLimit(); // Apply higher rate limits for agent APIs

            group.MapGet("/history", GetConversationHistory)
            .WithName("Get Conversation History")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Get conversation history";
                operation.Description = "Gets the conversation history for a given conversation thread with pagination support";
                return operation;
            });

            group.MapGet("/last-task-id", GetLastTaskId)
            .WithName("Get Last Task Id")
            .Produces<string>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Get last task id from message history";
                operation.Description = "Gets the most recent task id from message history for a given workflow, participant, and optional scope";
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
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
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
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process outbound data from Agent";
                operation.Description = "Processes an outbound data for agent conversations and returns the result";
                return operation;
            });

            group.MapPost("/outbound/webhook", async (
                [FromBody] ChatOrDataRequest request,
                [FromServices] IMessageService messageService) =>
            {
                var result = await messageService.ProcessOutgoingMessage(request, MessageType.Webhook);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Webhook from Agent")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process outbound webhook response from Agent";
                operation.Description = "Processes an outbound webhook response for agent webhook handlers and returns the result";
                return operation;
            });

            group.MapPost("/outbound/reasoning", async (
                [FromBody] ChatOrDataRequest request,
                [FromServices] IMessageService messageService) =>
            {
                var result = await messageService.ProcessOutgoingMessage(request, MessageType.Reasoning);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Reasoning from Agent")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process outbound reasoning from Agent";
                operation.Description = "Processes an outbound reasoning message for streaming agent thinking steps. Delivered via SSE for frontend display of intermediate actions.";
                return operation;
            });

            group.MapPost("/outbound/tool", async (
                [FromBody] ChatOrDataRequest request,
                [FromServices] IMessageService messageService) =>
            {
                var result = await messageService.ProcessOutgoingMessage(request, MessageType.Tool);
                return result.ToHttpResult();
            })
            .WithName("Process Outbound Tool Execution from Agent")
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process outbound tool execution from Agent";
                operation.Description = "Processes an outbound tool execution message for streaming tool call steps. Delivered via SSE for frontend display of intermediate actions.";
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
            .Produces<object>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status500InternalServerError)
            .WithOpenApi(operation =>
            {
                operation.Summary = "Process handover message from Agent";
                operation.Description = "Processes a handover message for agent conversations and returns the result";
                return operation;
            });

            // New synchronous endpoint that waits for responses
            group.MapPost("/converse", async (
                [FromServices] ILogger<ConversationHistoryQuery> logger,
                [FromServices] ILoggerFactory loggerFactory,
                [FromQuery] string type,
                [FromBody] ChatOrDataRequest chatOrDataRequest,
                [FromServices] IMessageService messageService,
                [FromServices] ITenantContext tenantContext,
                [FromServices] IPendingRequestService pendingRequestService,
                HttpContext context,
                [FromQuery] int timeoutSeconds = 60,
                [FromQuery] string? requestId = null) =>
            {
                logger.LogInformation("Converse endpoint called with request: {Request}", JsonSerializer.Serialize(chatOrDataRequest));
                var workflow = chatOrDataRequest.WorkflowId;
                if (string.IsNullOrEmpty(workflow))
                {
                    logger.LogError("Workflow Id not provided");
                    return Results.BadRequest("Workflow Id not provided");
                }
                var participantId = chatOrDataRequest.ParticipantId;
                var origin = chatOrDataRequest.Origin;
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
                    logger.LogError("Invalid request: {ErrorMessage}", errorMessage);
                    return Results.BadRequest(errorMessage);
                }

                // Normalize participant ID to lowercase for consistency (especially important for emails)
                var resolvedParticipantId = (string.IsNullOrEmpty(participantId) ? tenantContext.LoggedInUser : participantId).ToLowerInvariant();

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
                    requestId,
                    origin);


                // Use the sync message handler to process the complex flow
                var syncLogger = loggerFactory.CreateLogger<SyncMessageHandler>();
                var syncHandler = new SyncMessageHandler(messageService, pendingRequestService, syncLogger);
                var result =  await syncHandler.ProcessSyncMessageAsync(
                    chat,
                    messageType,
                    timeoutSeconds,
                    context.RequestAborted);

                return Results.Ok(result);
            })
                .WithName("Send Data or Chat to workflow and wait for response from Agent")
                .Produces<object>(StatusCodes.Status200OK)
                .ProducesProblem(StatusCodes.Status400BadRequest)
                .ProducesProblem(StatusCodes.Status408RequestTimeout)
                .ProducesProblem(StatusCodes.Status500InternalServerError)
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
                        
            if (query.WorkflowId == null)
            {
                query.WorkflowId = $"{tenantContext.TenantId}:{query.WorkflowType}";
            }

            if (string.IsNullOrEmpty(query.ThreadId)) {
                var result = await messageService.GetThreadHistoryAsync(query.WorkflowId, query.ParticipantId, query.Page, query.PageSize, query.Scope, true);
                return result.ToHttpResult();
            } else {
                var result = await messageService.GetThreadHistoryAsync(query.ThreadId, query.Page, query.PageSize, query.Scope, true);
                return result.ToHttpResult();
            }
            
        }

        private static async Task<IResult> GetLastTaskId(
            [FromQuery] string? workflowId,
            [FromQuery] string participantId,
            [FromQuery] string? scope,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IMessageService messageService)
        {
            if (string.IsNullOrEmpty(workflowId))
            {
                return ServiceResult<string?>.BadRequest("WorkflowId is required").ToHttpResult();
            }

            if (string.IsNullOrEmpty(participantId))
            {
                return ServiceResult<string?>.BadRequest("ParticipantId is required").ToHttpResult();
            }

            // workflowId should start with tenantId
            if (!workflowId.StartsWith(tenantContext.TenantId))
            {
                return ServiceResult<string?>.BadRequest("WorkflowId must start with tenantId").ToHttpResult();
            }

            var result = await messageService.GetLastTaskIdAsync(workflowId, participantId, scope);
            return result.ToHttpResult();
        }
    }
} 