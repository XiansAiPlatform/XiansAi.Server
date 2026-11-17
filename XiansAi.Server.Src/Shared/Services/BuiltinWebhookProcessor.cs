using Microsoft.AspNetCore.Http;
using Shared.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// One call to a builtin webhook: the values its URL carries, plus the call's headers and body.
/// <c>Redelivery</c> is set only by the server, when it sends a recorded call again.
/// </summary>
public record BuiltinWebhookCallRequest(
    string TenantId,
    string AgentName,
    string WorkflowName,
    string WebhookName,
    string? ActivationName,
    string? ApiKeyId,
    int TimeoutSeconds,
    string? ParticipantId,
    string? Scope,
    string? Authorization,
    string? ContentType,
    IHeaderDictionary Headers,
    string Body,
    WebhookRedelivery? Redelivery = null);

/// <summary>A call sent again from its recording: which delivery it repeats, and who asked for it.</summary>
/// <param name="IntegrationId">The webhook the original was recorded against.</param>
/// <param name="RedeliveryOf">The delivery first received from the source.</param>
/// <param name="RedeliveryOfRequestId">That delivery's request id, shown as the redelivery's label.</param>
/// <param name="TriggeredBy">The user who asked for the redelivery.</param>
public record WebhookRedelivery(string IntegrationId, Guid RedeliveryOf, string RedeliveryOfRequestId, string? TriggeredBy);

/// <summary>
/// How a builtin webhook call ended. Exactly one of <c>Error</c>, <c>Response</c> or <c>RawResult</c>
/// is set. <c>DeliveryId</c> is the recorded call, or null when the call was turned away before it
/// was recorded (a bad parameter, an inactive activation) or could not be matched to a webhook.
/// </summary>
public record BuiltinWebhookOutcome(Guid? DeliveryId, IResult? Error, WebhookResponse? Response, object? RawResult)
{
    /// <summary>The HTTP status the webhook answers with.</summary>
    public int StatusCode => Error is IStatusCodeHttpResult { StatusCode: { } code } ? code
        : Response != null ? (int)Response.StatusCode
        : StatusCodes.Status200OK;
}

/// <summary>
/// What the builtin webhook endpoint does with a call: checks it, records it, sends it to the agent
/// in a workflow of its own, and waits for the agent's answer. The endpoint and redelivery both use it.
/// </summary>
public interface IBuiltinWebhookProcessor
{
    Task<BuiltinWebhookOutcome> ProcessAsync(BuiltinWebhookCallRequest call, CancellationToken cancellationToken);
}

public class BuiltinWebhookProcessor : IBuiltinWebhookProcessor
{
    private readonly IMessageService _messageService;
    private readonly IPendingRequestService _pendingRequestService;
    private readonly IActivationValidationService _activationValidationService;
    private readonly IWebhookIncomingService _webhookIncomingService;
    private readonly ILogger<SyncMessageHandler> _logger;

    public BuiltinWebhookProcessor(
        IMessageService messageService,
        IPendingRequestService pendingRequestService,
        IActivationValidationService activationValidationService,
        IWebhookIncomingService webhookIncomingService,
        ILogger<SyncMessageHandler> logger)
    {
        _messageService = messageService;
        _pendingRequestService = pendingRequestService;
        _activationValidationService = activationValidationService;
        _webhookIncomingService = webhookIncomingService;
        _logger = logger;
    }

    public async Task<BuiltinWebhookOutcome> ProcessAsync(BuiltinWebhookCallRequest call, CancellationToken cancellationToken)
    {
        var rejection = Validate(call);
        if (rejection != null)
            return new BuiltinWebhookOutcome(null, rejection, null, null);

        // Validate activation exists and is active when activationName is provided
        if (!string.IsNullOrWhiteSpace(call.ActivationName))
        {
            var validationResult = await _activationValidationService.ValidateActivationAsync(
                call.TenantId, call.AgentName, call.ActivationName, call.WorkflowName);
            if (!validationResult.IsSuccess)
                return new BuiltinWebhookOutcome(null, validationResult.ToHttpResult(), null, null);
        }

        // Build workflowId from workflowName (format: tenant:agent:workflowName[:activation])
        var webhookWorkflowId = $"{call.TenantId}:{call.AgentName}:{call.WorkflowName}{(call.ActivationName != null ? $":{call.ActivationName}" : string.Empty)}";

        // Use participantId from query or default to "webhook" for identification
        // Normalize to lowercase for consistency (especially important for emails)
        var resolvedParticipantId = (call.ParticipantId ?? "webhook").ToLowerInvariant();

        // Generate unique request ID for correlation
        var requestId = MessageRequestProcessor.GenerateRequestId(webhookWorkflowId, resolvedParticipantId);

        // Each call runs in its own workflow, keyed by its request ID, so one call's logs can
        // be found by workflow ID instead of mixing with every other call to this webhook.
        var workflowId = WorkflowIdentifier.WithRequestId(webhookWorkflowId, call.ActivationName, requestId);

        // Record the call before the workflow runs, so it is kept even if the workflow
        // times out or fails. Recording never throws.
        var deliveryId = await _webhookIncomingService.RecordAsync(new BuiltinWebhookCall(
            call.TenantId, call.AgentName, call.ActivationName, call.WorkflowName, call.WebhookName, call.ApiKeyId,
            workflowId, requestId, call.ContentType, call.Headers, call.Body, call.Redelivery));

        var chatRequest = new ChatOrDataRequest
        {
            RequestId = requestId,
            ParticipantId = resolvedParticipantId,
            WorkflowId = workflowId,
            // The call's own workflow belongs to the connection's activation.
            ActivationWorkflowId = webhookWorkflowId,
            Text = call.WebhookName,
            Data = call.Body,
            Scope = call.Scope,
            Authorization = call.Authorization,
            Origin = $"webhook:builtin:{call.WebhookName}",
            Metadata = WebhookHeaderCapture.Capture(call.Headers)
        };

        // Use the sync message handler to process and wait for response
        var syncHandler = new SyncMessageHandler(_messageService, _pendingRequestService, _logger);
        var result = await syncHandler.ProcessSyncMessageAsync(
            chatRequest, MessageType.Webhook, call.TimeoutSeconds, cancellationToken);

        if (result is IResult error)
            return new BuiltinWebhookOutcome(deliveryId, error, null, null);

        // Read the WebhookResponse from the workflow's reply.
        var webhookResponse = WebhookResponseParser.FromSyncResult(result, _logger);
        if (webhookResponse == null)
        {
            _logger.LogWarning("Could not parse WebhookResponse from result, returning raw result");
            return new BuiltinWebhookOutcome(deliveryId, null, null, result);
        }

        return new BuiltinWebhookOutcome(deliveryId, null, webhookResponse, null);
    }

    private static IResult? Validate(BuiltinWebhookCallRequest call)
    {
        if (string.IsNullOrWhiteSpace(call.WorkflowName))
            return Results.BadRequest("workflowName is required");

        if (string.IsNullOrWhiteSpace(call.WebhookName))
            return Results.BadRequest("webhookName is required");

        if (string.IsNullOrWhiteSpace(call.AgentName))
            return Results.BadRequest("agentName is required");

        // Each name is one segment of the workflow ID, so a ':' would shift the segments after it.
        if (call.AgentName.Contains(':') || call.WorkflowName.Contains(':') || call.ActivationName?.Contains(':') == true)
            return Results.BadRequest("agentName, workflowName and activationName must not contain ':'");

        if (call.TimeoutSeconds <= 0 || call.TimeoutSeconds > 300)
            return Results.BadRequest("Timeout must be between 1 and 300 seconds");

        return null;
    }
}
