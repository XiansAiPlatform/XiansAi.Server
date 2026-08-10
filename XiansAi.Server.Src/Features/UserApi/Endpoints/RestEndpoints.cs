using Microsoft.AspNetCore.Mvc;
using Shared.Utils.Services;
using Shared.Services;
using Shared.Auth;
using System.Text.Json;
using Shared.Utils;
using Features.Shared.Configuration;
using Features.UserApi.Utils;

namespace Features.UserApi.Endpoints;

public class RestEndpointsLogger { }

public static class RestEndpoints
{
    public static void MapRestEndpoints(this WebApplication app)
    {
        var restGroup = app.MapGroup("/api/user/rest")
            .WithTags("UserAPI - Rest")
            .RequireAuthorization("EndpointAuthPolicy")
            .WithAgentUserApiRateLimit(); // Apply higher rate limits for user APIs

        restGroup.MapPost("/send", async (
            [FromQuery] string workflow,
            [FromQuery] string type,
            [FromQuery] string participantId,
            [FromBody] JsonElement? request,
            [FromServices] IMessageService messageService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILogger<RestEndpointsLogger> logger,
            HttpContext context,
            [FromQuery] string? requestId = null,
            [FromQuery] string? text = null) =>
        {

            // Validate request parameters
            var (isValid, errorMessage) = MessageRequestValidator.ValidateInboundRequest(
                workflow, type, out var messageType);

            if (!isValid)
            {
                return Results.BadRequest(errorMessage);
            }

            if (!ParticipantIdResolver.TryResolve(participantId, tenantContext, out var participant))
            {
                return ParticipantAccessDenied.ToResult("Send", participantId, logger);
            }

            var resolvedParticipantId = participant.ParticipantId;
            var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;

            // Create the request using the processor utility
            var message = MessageRequestProcessor.CreateRequest(
                messageType,
                workflowId,
                resolvedParticipantId,
                request,
                text,
                requestId,
                origin: null,
                authorization: tenantContext.Authorization);

            var result = await messageService.ProcessIncomingMessage(message, messageType);
            return result.ToHttpResult();
        })
            .WithName("Send Data to workflow from user api")
            
        .WithSummary("Send Data to workflow")
        .WithDescription("Send data to a workflow. Requires workflowId, type, participantId and apikey as query parameters. For Chat messages, you can pass text as query parameter and optional JSON data in request body. For Data messages, pass JSON in request body. Optional requestId for correlation.");

        // New synchronous endpoint that waits for responses
        restGroup.MapPost("/converse", async (
            [FromQuery] string workflow,
            [FromQuery] string type,
            [FromQuery] string participantId,
            [FromBody] JsonElement? request,
            [FromServices] IMessageService messageService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] IPendingRequestService pendingRequestService,
            [FromServices] ILogger<SyncMessageHandler> logger,
            HttpContext context,
            [FromQuery] int timeoutSeconds = 60,
            [FromQuery] string? requestId = null,
            [FromQuery] string? text = null) =>
        {
            workflow = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;

            // Validate request parameters
            var (isValid, errorMessage) = MessageRequestValidator.ValidateSyncRequest(
                workflow, type, timeoutSeconds, out var messageType);

            if (!isValid)
            {
                return Results.BadRequest(errorMessage);
            }

            if (!ParticipantIdResolver.TryResolve(participantId, tenantContext, out var participant))
            {
                return ParticipantAccessDenied.ToResult("Converse", participantId, logger);
            }

            var resolvedParticipantId = participant.ParticipantId;

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
                request,
                text,
                requestId,
                origin: null,
                authorization: tenantContext.Authorization);

            // Use the sync message handler to process the complex flow
            var syncHandler = new SyncMessageHandler(messageService, pendingRequestService, logger);
            return await syncHandler.ProcessSyncMessageAsync(
                chat,
                messageType,
                timeoutSeconds,
                context.RequestAborted);
        })
            .WithName("Send Data to workflow and wait for response")
            
        .WithSummary("Send Data to workflow and wait for response")
        .WithDescription("Send data to a workflow and wait synchronously for the response. Requires workflowId, type, participantId and apikey as query parameters. For Chat messages, you can pass text as query parameter and optional JSON data in request body. For Data messages, pass JSON in request body. Optional timeoutSeconds (default: 60, max: 300).");

        // History endpoint with caching
        restGroup.MapGet("/history", async (
            [FromQuery] string workflow,
            [FromQuery] string participantId,
            [FromServices] IMessageService messageService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILogger<RestEndpointsLogger> logger,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? scope = null) =>
        {
            if (!ParticipantIdResolver.TryResolve(participantId, tenantContext, out var participant))
            {
                return ParticipantAccessDenied.ToResult("GetMessageHistory", participantId, logger);
            }

            var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;
            var result = await messageService.GetThreadHistoryAsync(workflowId, participant.ParticipantId, page, pageSize, scope);

            // Clients that relied on the default participant id previously had their threads stored
            // under the canonical login id, so surface those until they have history under the new id.
            if (participant.LegacyParticipantId != null && result.IsSuccess && result.Data?.Count == 0)
            {
                result = await messageService.GetThreadHistoryAsync(
                    workflowId, participant.LegacyParticipantId, page, pageSize, scope);
            }

            return result.ToHttpResult();
        })
        .WithName("GetMessageHistory")
        .WithSummary("Get conversation history with caching")
        .WithDescription("Retrieves conversation history with optimized database queries and caching");

        // Delete thread endpoint
        restGroup.MapDelete("/thread", async (
            [FromQuery] string workflow,
            [FromQuery] string participantId,
            [FromServices] IMessageService messageService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILogger<RestEndpointsLogger> logger) =>
        {
            // Validate request parameters
            if (string.IsNullOrEmpty(workflow))
            {
                return Results.BadRequest("Workflow is required parameter");
            }

            if (!ParticipantIdResolver.TryResolve(participantId, tenantContext, out var participant))
            {
                return ParticipantAccessDenied.ToResult("DeleteThread", participantId, logger);
            }

            var workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;

            var result = await messageService.DeleteThreadAsync(workflowId, participant.ParticipantId);

            // Same transitional fallback as /history, so callers can still delete a thread created
            // under their previous participant id.
            if (participant.LegacyParticipantId != null && result.StatusCode == StatusCode.NotFound)
            {
                result = await messageService.DeleteThreadAsync(workflowId, participant.LegacyParticipantId);
            }

            return result.ToHttpResult();
        })
        .WithName("DeleteThread")
        .WithSummary("Delete conversation thread")
        .WithDescription("Deletes a conversation thread and all its associated messages for the specified workflow and participant");
    }
}
