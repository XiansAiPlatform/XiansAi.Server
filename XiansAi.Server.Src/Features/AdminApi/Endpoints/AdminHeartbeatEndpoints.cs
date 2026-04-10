using Microsoft.AspNetCore.Mvc;
using Shared.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// AdminApi heartbeat endpoint for agent worker liveness checks.
/// Sends a Heartbeat message via Temporal signals to the agent workflow and waits synchronously
/// for a response. Returns available=true when workers respond; available=false on timeout or error.
/// Aligns with agent MessageType.Heartbeat and MessageResponseHelper.SendHeartbeatResponseAsync.
/// </summary>
public static class AdminHeartbeatEndpoints
{
    /// <summary>
    /// Maps the AdminApi heartbeat endpoint.
    /// </summary>
    public static void MapAdminHeartbeatEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var heartbeatGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/heartbeat")
            .WithTags("AdminAPI - Health")
            .RequireAuthorization("AdminEndpointAuthPolicy");

        heartbeatGroup.MapGet("", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromServices] IPendingRequestService pendingRequestService,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            [FromQuery] string? workflowType = null,
            [FromQuery] int timeoutSeconds = 10) =>
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

            if (timeoutSeconds < 1 || timeoutSeconds > 30)
            {
                return Results.BadRequest("timeoutSeconds must be between 1 and 30");
            }

            // Construct workflow ID and validate activation exists
            var effectiveWorkflowType = string.IsNullOrWhiteSpace(workflowType) ? "Supervisor Workflow" : workflowType.Trim();
            var validationResult = await activationValidationService.ValidateActivationAsync(tenantId, agentName, activationName, effectiveWorkflowType);
            if (!validationResult.IsSuccess)
            {
                return Results.Json(new { available = false }, statusCode: StatusCodes.Status200OK);
            }

            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);
            var participantId = "heartbeat";
            var requestId = MessageRequestProcessor.GenerateRequestId(workflowId, participantId);

            var chatRequest = MessageRequestProcessor.CreateRequest(
                MessageType.Heartbeat,
                workflowId,
                participantId,
                requestId: requestId,
                authorization: tenantContext.Authorization);

            var syncHandler = new SyncMessageHandler(messageService, pendingRequestService, loggerFactory.CreateLogger<SyncMessageHandler>());

            try
            {
                var result = await syncHandler.ProcessSyncMessageAsync(
                    chatRequest,
                    MessageType.Heartbeat,
                    timeoutSeconds,
                    context.RequestAborted);

                // Success: agent worker responded; IResult indicates error from sync handler
                if (result is IResult)
                {
                    return Results.Json(new { available = false }, statusCode: StatusCodes.Status200OK);
                }

                return Results.Json(new { available = true }, statusCode: StatusCodes.Status200OK);
            }
            catch (TimeoutException)
            {
                return Results.Json(new { available = false }, statusCode: StatusCodes.Status200OK);
            }
            catch (Exception)
            {
                return Results.Json(new { available = false }, statusCode: StatusCodes.Status200OK);
            }
        })
        .WithName("AdminHeartbeat")
        .Produces<HeartbeatResponse>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        ;
    }

    private sealed record HeartbeatResponse(bool Available);
}
