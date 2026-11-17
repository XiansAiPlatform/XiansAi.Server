using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>What happened when a recorded call was sent again.</summary>
/// <param name="DeliveryId">The new delivery, or null when the webhook turned the call away before recording it (for example, an inactive activation).</param>
/// <param name="RedeliveryOf">The delivery first received from the source.</param>
/// <param name="StatusCode">HTTP status the webhook answered with.</param>
/// <param name="DurationMs">Time from sending the call to its answer.</param>
public record WebhookRedeliveryResult(Guid? DeliveryId, Guid RedeliveryOf, int StatusCode, long DurationMs);

/// <summary>Sends a call a builtin webhook recorded to that webhook again.</summary>
public interface IWebhookRedeliveryService
{
    /// <summary>
    /// Runs the recorded body and headers through the webhook again, with the values from the
    /// webhook's current URL. The call is checked and recorded like any other (as a new delivery that
    /// points at the original, with <paramref name="triggeredBy"/> as the user who asked for it) and
    /// runs the agent again.
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
    /// gives up after 300 s without response headers, so this answers before that. The call is still
    /// recorded and runs the agent.
    /// </summary>
    internal const int MaxTimeoutSeconds = 290;

    /// <summary>Redeliveries currently running, keyed by tenant, webhook and call.</summary>
    private static readonly ConcurrentDictionary<string, byte> InFlight = new();

    private readonly IWebhookIncomingRepository _incomings;
    private readonly IAppIntegrationRepository _integrations;
    private readonly IBuiltinWebhookProcessor _processor;
    private readonly ITenantContext _tenantContext;
    private readonly IAuditLogService _auditLog;

    public WebhookRedeliveryService(
        IWebhookIncomingRepository incomings,
        IAppIntegrationRepository integrations,
        IBuiltinWebhookProcessor processor,
        ITenantContext tenantContext,
        IAuditLogService auditLog)
    {
        _incomings = incomings ?? throw new ArgumentNullException(nameof(incomings));
        _integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
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
        if (url == null || !url.StartsWith(BuiltinWebhookPath + "?", StringComparison.Ordinal))
            return ServiceResult<WebhookRedeliveryResult>.Conflict("This webhook has no URL to redeliver to");

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

            // MessageService passes the caller's own credential to the agent when a message carries
            // none. The caller here is the admin asking for the redelivery, so clear it.
            _tenantContext.Authorization = null;

            var stopwatch = Stopwatch.StartNew();
            var outcome = await _processor.ProcessAsync(ToCall(tenantId, url, original, redelivery), cancellationToken);
            stopwatch.Stop();

            // Audited whatever the answer: a call that timed out was still recorded and may still be
            // running the agent. The deliveryId says whether the webhook recorded it.
            _ = _auditLog.RecordEntryAsync(
                action: DomainEventTypes.IntegrationWebhookRedelivered,
                description: $"Call {redelivery.RedeliveryOfRequestId} was redelivered to builtin webhook '{integration.Name}'.",
                details: new
                {
                    integrationId,
                    // As strings: the audit store cannot serialize a Guid in the free-form details.
                    redeliveryOf = redelivery.RedeliveryOf.ToString(),
                    deliveryId = outcome.DeliveryId?.ToString(),
                    statusCode = outcome.StatusCode
                },
                tenantId: tenantId);

            return ServiceResult<WebhookRedeliveryResult>.Success(new WebhookRedeliveryResult(
                outcome.DeliveryId, redelivery.RedeliveryOf, outcome.StatusCode, stopwatch.ElapsedMilliseconds));
        }
        finally
        {
            InFlight.TryRemove(inFlightKey, out _);
        }
    }

    /// <summary>
    /// The recorded call, with the values from the webhook's URL. The agent is passed the API key the
    /// original call authenticated with (as the endpoint does), never the credential of the user
    /// asking for the redelivery.
    /// </summary>
    private static BuiltinWebhookCallRequest ToCall(string tenantId, string url, WebhookIncoming original, WebhookRedelivery redelivery)
    {
        var query = QueryHelpers.ParseQuery(new Uri("http://localhost" + url).Query);
        string? Query(string name) => query.TryGetValue(name, out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : null;

        var headers = new HeaderDictionary();
        foreach (var group in original.Headers.GroupBy(h => h.Name, StringComparer.OrdinalIgnoreCase))
        {
            headers[group.Key] = group.Select(h => h.Value).ToArray();
        }
        headers[RedeliveryOfHeader] = redelivery.RedeliveryOf.ToString();

        return new BuiltinWebhookCallRequest(
            tenantId,
            Query("agentName") ?? string.Empty,
            Query("workflowName") ?? string.Empty,
            Query("webhookName") ?? string.Empty,
            Query("activationName"),
            Query("apikeyId"),
            Math.Min(int.TryParse(Query("timeoutSeconds"), out var timeout) ? timeout : DefaultTimeoutSeconds, MaxTimeoutSeconds),
            Query("participantId"),
            Query("scope"),
            Query("authorization") ?? ApiKeyFromAuthorizationHeader(headers),
            original.ContentType,
            headers,
            original.Body,
            redelivery);
    }

    /// <summary>The API key in a stored <c>Authorization: Bearer</c> header, which the endpoint passes to the agent.</summary>
    private static string? ApiKeyFromAuthorizationHeader(IHeaderDictionary headers)
    {
        var (found, token) = AuthorizationHeaderHelper.ExtractBearerToken(headers.Authorization.FirstOrDefault());
        return found && token != null && token.StartsWith(ApiKey.KeyPrefix, StringComparison.Ordinal) ? token : null;
    }
}
