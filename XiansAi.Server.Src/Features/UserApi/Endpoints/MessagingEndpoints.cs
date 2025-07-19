using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Shared.Services;
using Shared.Repositories;
using Shared.Auth;
using System.Text.Json;
using Features.UserApi.Utils;

namespace Features.UserApi.Endpoints
{
    public static class MessagingEndpoints
    {
        public static void MapMessagingEndpoints(this WebApplication app)
        {
            var messagingGroup = app.MapGroup("/api/user/messaging")
                .WithTags("UserAPI - Messaging")
                .RequireAuthorization("EndpointAuthPolicy");

            messagingGroup.MapPost("/send", async (
                [FromQuery] string workflow,
                [FromQuery] string type ,
                [FromQuery] string apiKey,
                [FromQuery] string participantId,
                [FromBody] JsonElement? request,
                [FromServices] IMessageService messageService,
                [FromServices] ITenantContext tenantContext,
                HttpContext context,
                [FromQuery] string? requestId = null,
                [FromQuery] string? text = null) => {
                    
                    // Validate request parameters
                    var (isValid, errorMessage) = MessageRequestValidator.ValidateInboundRequest(
                        workflow, type, apiKey, out var messageType);
                    
                    if (!isValid)
                    {
                        return Results.BadRequest(errorMessage);
                    }

                    var resolvedParticipantId = string.IsNullOrEmpty(participantId) ? tenantContext.LoggedInUser : participantId;
                    var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;

                    // Create the request using the processor utility
                    var message = MessageRequestProcessor.CreateRequest(
                        messageType, 
                        workflowId, 
                        apiKey, 
                        resolvedParticipantId, 
                        request,
                        text,
                        requestId);

                    var result = await messageService.ProcessIncomingMessage(message, messageType);
                    return result.ToHttpResult();
                })
                .WithName("Send Data to workflow from user api")
                .WithOpenApi(operation => {
                    operation.Summary = "Send Data to workflow";
                    operation.Description = "Send data to a workflow. Requires workflowId, type, participantId and apikey as query parameters. For Chat messages, you can pass text as query parameter and optional JSON data in request body. For Data messages, pass JSON in request body. Optional requestId for correlation.";
                    return operation;
                });

            // New synchronous endpoint that waits for responses
            messagingGroup.MapPost("/converse", async (
                [FromQuery] string workflow,
                [FromQuery] string type,
                [FromQuery] string apikey,
                [FromQuery] string participantId,
                [FromBody] JsonElement? request,
                [FromServices] IMessageService messageService,  
                [FromServices] ITenantContext tenantContext,
                [FromServices] IPendingRequestService pendingRequestService,
                HttpContext context,
                [FromQuery] int timeoutSeconds = 60,
                [FromQuery] string? requestId = null,
                [FromQuery] string? text = null) => {
                    
                    // Validate request parameters
                    var (isValid, errorMessage) = MessageRequestValidator.ValidateSyncRequest(
                        workflow, type, apikey, timeoutSeconds, out var messageType);
                    
                    if (!isValid)
                    {
                        return Results.BadRequest(errorMessage);
                    }

                    var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;
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
                        apikey,
                        resolvedParticipantId,
                        request,
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
                .WithName("Send Data to workflow and wait for response")
                .WithOpenApi(operation => {
                    operation.Summary = "Send Data to workflow and wait for response";
                    operation.Description = "Send data to a workflow and wait synchronously for the response. Requires workflowId, type, participantId and apikey as query parameters. For Chat messages, you can pass text as query parameter and optional JSON data in request body. For Data messages, pass JSON in request body. Optional timeoutSeconds (default: 60, max: 300).";
                    return operation;
                });

            // History endpoint with caching
            messagingGroup.MapGet("/history", async (
                [FromQuery] string workflow,
                [FromQuery] string participantId,
                [FromServices] IMessageService messageService,
                [FromServices] ITenantContext tenantContext,
                [FromQuery] int page = 1,
                [FromQuery] int pageSize = 50,
                [FromQuery] string? scope = null) =>
            {
                var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;
                var result = await messageService.GetThreadHistoryAsync(workflowId, participantId, page, pageSize, scope);
                return result.ToHttpResult();
            })
            .WithName("GetMessageHistory")
            .WithSummary("Get conversation history with caching")
            .WithDescription("Retrieves conversation history with optimized database queries and caching");
        }
    }
}
