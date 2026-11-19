using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>A call sent again from its recording: which delivery it repeats, and who asked for it.</summary>
/// <param name="IntegrationId">The webhook the original was recorded against.</param>
/// <param name="RedeliveryOf">The delivery first received from the source.</param>
/// <param name="RedeliveryOfRequestId">That delivery's request id, shown as the redelivery's label.</param>
/// <param name="TriggeredBy">The user who asked for the redelivery.</param>
public record WebhookRedelivery(string IntegrationId, Guid RedeliveryOf, string RedeliveryOfRequestId, string? TriggeredBy);

/// <summary>What happened when a recorded call was sent again.</summary>
/// <param name="DeliveryId">The new delivery, or null when it could not be recorded.</param>
/// <param name="RedeliveryOf">The delivery first received from the source.</param>
/// <param name="StatusCode">HTTP status the webhook would have answered with.</param>
/// <param name="DurationMs">Time from starting the workflow to the agent's answer.</param>
public record WebhookRedeliveryResult(Guid? DeliveryId, Guid RedeliveryOf, int StatusCode, long DurationMs);

/// <summary>Sends a call a builtin webhook recorded to that webhook's agent again.</summary>
public interface IWebhookRedeliveryService
{
    /// <summary>
    /// Starts the agent's workflow again with the recorded body and headers and the values from the
    /// webhook's current URL, and waits for its answer. The call is recorded as a new delivery that
    /// points at the original, with <paramref name="triggeredBy"/> as the user who asked for it.
    /// </summary>
    Task<ServiceResult<WebhookRedeliveryResult>> RedeliverAsync(
        string tenantId, string integrationId, Guid incomingId, string? triggeredBy, CancellationToken cancellationToken = default);
}

public class WebhookRedeliveryService : IWebhookRedeliveryService
{
    /// <summary>The id of the delivery being repeated, added to the redelivered call's headers so the agent can tell.</summary>
    public const string RedeliveryOfHeader = "X-Xians-Redelivery-Of";

    private const string BuiltinWebhookPlatformId = "builtin_webhook";
    private const string BuiltinWebhookPath = "/api/user/webhooks/builtin";

    /// <summary>The webhook's default wait for the agent when its URL sets none.</summary>
    private const int DefaultTimeoutSeconds = 60;

    /// <summary>
    /// Longest a redelivery waits for the agent. Callers such as Agent Studio run on Node, whose fetch
    /// gives up after 300 s without response headers, so this answers before that. The workflow keeps
    /// running after the wait ends.
    /// </summary>
    internal const int MaxTimeoutSeconds = 290;

    /// <summary>Redeliveries currently running, keyed by tenant, webhook and call.</summary>
    private static readonly ConcurrentDictionary<string, byte> InFlight = new();

    private readonly IWebhookIncomingRepository _incomings;
    private readonly IAppIntegrationRepository _integrations;
    private readonly IWebhookIncomingService _webhookIncomingService;
    private readonly IActivationValidationService _activationValidationService;
    private readonly IMessageService _messageService;
    private readonly IPendingRequestService _pendingRequestService;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;
    private readonly ILogger<SyncMessageHandler> _logger;

    public WebhookRedeliveryService(
        IWebhookIncomingRepository incomings,
        IAppIntegrationRepository integrations,
        IWebhookIncomingService webhookIncomingService,
        IActivationValidationService activationValidationService,
        IMessageService messageService,
        IPendingRequestService pendingRequestService,
        ITenantContext tenantContext,
        IAuditLogService auditLog,
        ILogger<SyncMessageHandler> logger)
    {
        _incomings = incomings ?? throw new ArgumentNullException(nameof(incomings));
        _integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
        _webhookIncomingService = webhookIncomingService ?? throw new ArgumentNullException(nameof(webhookIncomingService));
        _activationValidationService = activationValidationService ?? throw new ArgumentNullException(nameof(activationValidationService));
        _messageService = messageService ?? throw new ArgumentNullException(nameof(messageService));
        _pendingRequestService = pendingRequestService ?? throw new ArgumentNullException(nameof(pendingRequestService));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<WebhookRedeliveryResult>> RedeliverAsync(
        string tenantId, string integrationId, Guid incomingId, string? triggeredBy, CancellationToken cancellationToken = default)
    {
        var integration = await _integrations.GetByIdAsync(integrationId);
        if (integration == null
            || integration.TenantId != tenantId
            || !integration.PlatformId.Equals(BuiltinWebhookPlatformId, StringComparison.OrdinalIgnoreCase))
        {
            return ServiceResult<WebhookRedeliveryResult>.NotFound("Webhook not found");
        }

        var original = await _incomings.GetAsync(tenantId, integrationId, incomingId);
        if (original == null)
            return ServiceResult<WebhookRedeliveryResult>.NotFound("Incoming call not found");

        if (original.BodyTruncated || original.HeadersTruncated)
        {
            return ServiceResult<WebhookRedeliveryResult>.Conflict(
                "This call was stored truncated, so it cannot be redelivered as it was sent");
        }

        var url = integration.Configuration.TryGetValue("webhookUrl", out var value) ? value?.ToString() : null;
        var target = url != null && url.StartsWith(BuiltinWebhookPath + "?", StringComparison.Ordinal)
            ? WebhookTarget.FromUrl(url)
            : null;
        if (target == null)
            return ServiceResult<WebhookRedeliveryResult>.Conflict("This webhook has no URL to redeliver to");

        // The workflow ID is built from these names, so the same checks as the webhook endpoint.
        if (target.AgentName.Contains(':') || target.WorkflowName.Contains(':') || target.ActivationName?.Contains(':') == true)
            return ServiceResult<WebhookRedeliveryResult>.Conflict("This webhook's URL has a name containing ':'");

        if (!string.IsNullOrWhiteSpace(target.ActivationName))
        {
            var activation = await _activationValidationService.ValidateActivationAsync(
                tenantId, target.AgentName, target.ActivationName, target.WorkflowName);
            if (!activation.IsSuccess)
                return ServiceResult<WebhookRedeliveryResult>.Conflict(activation.ErrorMessage ?? "The webhook's activation is not active");
        }

        // One redelivery of a call at a time: a second click while the first is still waiting on
        // the agent would run it twice. This guard is per process; a second instance would not see it.
        var inFlightKey = $"{tenantId}|{integrationId}|{incomingId}";
        if (!InFlight.TryAdd(inFlightKey, 0))
            return ServiceResult<WebhookRedeliveryResult>.Conflict("This call is already being redelivered");

        try
        {
            // Always the first delivery from the source, so every redelivery of it shares one label.
            var redelivery = new WebhookRedelivery(
                integrationId,
                original.RedeliveryOf ?? original.Id,
                original.RedeliveryOfRequestId ?? original.RequestId,
                triggeredBy);

            var stopwatch = Stopwatch.StartNew();
            var (deliveryId, statusCode) = await StartWorkflowAsync(tenantId, target, original, redelivery, cancellationToken);
            stopwatch.Stop();

            // Audited whatever the answer: a call that timed out was still recorded and may still be
            // running the agent.
            _ = _auditLog.RecordEntryAsync(
                action: DomainEventTypes.IntegrationWebhookRedelivered,
                description: $"Call {redelivery.RedeliveryOfRequestId} was redelivered to builtin webhook '{integration.Name}'.",
                details: new
                {
                    integrationId,
                    // As strings: the audit store cannot serialize a Guid in the free-form details.
                    redeliveryOf = redelivery.RedeliveryOf.ToString(),
                    deliveryId = deliveryId?.ToString(),
                    statusCode
                },
                tenantId: tenantId);

            return ServiceResult<WebhookRedeliveryResult>.Success(new WebhookRedeliveryResult(
                deliveryId, redelivery.RedeliveryOf, statusCode, stopwatch.ElapsedMilliseconds));
        }
        finally
        {
            InFlight.TryRemove(inFlightKey, out _);
        }
    }

    /// <summary>
    /// Records the call as a new delivery, then sends it to the agent in a workflow of its own and
    /// waits for the answer, as the webhook endpoint does for a call from the source.
    /// </summary>
    private async Task<(Guid? DeliveryId, int StatusCode)> StartWorkflowAsync(
        string tenantId, WebhookTarget target, WebhookIncoming original, WebhookRedelivery redelivery, CancellationToken cancellationToken)
    {
        var webhookWorkflowId = $"{tenantId}:{target.AgentName}:{target.WorkflowName}{(target.ActivationName != null ? $":{target.ActivationName}" : string.Empty)}";
        var participantId = (target.ParticipantId ?? "webhook").ToLowerInvariant();
        var requestId = MessageRequestProcessor.GenerateRequestId(webhookWorkflowId, participantId);
        var workflowId = WorkflowIdentifier.WithRequestId(webhookWorkflowId, target.ActivationName, requestId);

        var headers = new HeaderDictionary();
        foreach (var group in original.Headers.GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            headers[group.Key] = group.Select(h => h.Value).ToArray();
        }
        headers[RedeliveryOfHeader] = redelivery.RedeliveryOf.ToString();

        var deliveryId = await _webhookIncomingService.RecordAsync(new BuiltinWebhookCall(
            tenantId, target.AgentName, target.ActivationName, target.WorkflowName, target.WebhookName, target.ApiKeyId,
            workflowId, requestId, original.ContentType, headers, original.Body, redelivery));

        // MessageService passes the caller's own credential to the agent when a message carries
        // none. The caller here is the admin asking for the redelivery, so clear it.
        _tenantContext.Authorization = null;

        var chatRequest = new ChatOrDataRequest
        {
            RequestId = requestId,
            ParticipantId = participantId,
            WorkflowId = workflowId,
            ActivationWorkflowId = webhookWorkflowId,
            Text = target.WebhookName,
            Data = original.Body,
            Scope = target.Scope,
            // The API key the original call authenticated with, as the endpoint passes it.
            Authorization = target.Authorization ?? ApiKeyFromAuthorizationHeader(headers),
            Origin = $"webhook:builtin:{target.WebhookName}",
            Metadata = WebhookHeaderCapture.Capture(headers)
        };

        var result = await new SyncMessageHandler(_messageService, _pendingRequestService, _logger)
            .ProcessSyncMessageAsync(chatRequest, MessageType.Webhook, target.TimeoutSeconds, cancellationToken);

        var statusCode = result switch
        {
            IStatusCodeHttpResult { StatusCode: { } code } => code,
            IResult => StatusCodes.Status200OK,
            _ => WebhookResponseParser.FromSyncResult(result, _logger) is { } response
                ? (int)response.StatusCode
                : StatusCodes.Status200OK
        };
        return (deliveryId, statusCode);
    }

    /// <summary>The API key in a stored <c>Authorization: Bearer</c> header, which the endpoint passes to the agent.</summary>
    private static string? ApiKeyFromAuthorizationHeader(IHeaderDictionary headers)
    {
        var (found, token) = AuthorizationHeaderHelper.ExtractBearerToken(headers.Authorization.FirstOrDefault());
        return found && token != null && token.StartsWith(ApiKey.KeyPrefix, StringComparison.Ordinal) ? token : null;
    }

    /// <summary>The values a builtin webhook's URL carries.</summary>
    private sealed record WebhookTarget(
        string AgentName, string WorkflowName, string WebhookName, string? ActivationName, string? ApiKeyId,
        string? ParticipantId, string? Scope, string? Authorization, int TimeoutSeconds)
    {
        /// <returns>The values, or null when the URL lacks a required name.</returns>
        public static WebhookTarget? FromUrl(string url)
        {
            var query = QueryHelpers.ParseQuery(new Uri("http://localhost" + url).Query);
            string? Query(string name) => query.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;

            var agentName = Query("agentName");
            var workflowName = Query("workflowName");
            var webhookName = Query("webhookName");
            if (agentName == null || workflowName == null || webhookName == null)
                return null;

            var timeout = int.TryParse(Query("timeoutSeconds"), out var t) && t > 0 ? t : DefaultTimeoutSeconds;
            return new WebhookTarget(
                agentName, workflowName, webhookName, Query("activationName"), Query("apikeyId"),
                Query("participantId"), Query("scope"), Query("authorization"), Math.Min(timeout, MaxTimeoutSeconds));
        }
    }
}
