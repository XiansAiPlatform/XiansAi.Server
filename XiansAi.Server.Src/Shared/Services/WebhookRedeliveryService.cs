using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using Shared.Configuration;
using Shared.Data.Models;
using Shared.Repositories;

using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>What happened when a recorded call was sent again.</summary>
/// <param name="DeliveryId">The new delivery, or null when the webhook rejected the call before recording it (for example, a revoked API key).</param>
/// <param name="RedeliveryOf">The delivery first received from the source.</param>
/// <param name="StatusCode">HTTP status the webhook answered with.</param>
/// <param name="DurationMs">Time from sending the call to its answer.</param>
public record WebhookRedeliveryResult(Guid? DeliveryId, Guid RedeliveryOf, int StatusCode, long DurationMs);

/// <summary>Sends a call a builtin webhook recorded to that webhook again.</summary>
public interface IWebhookRedeliveryService
{
    /// <summary>
    /// POSTs the recorded body and headers to the webhook's current URL. The call goes through the
    /// whole webhook pipeline (authentication, rate limiting, activation checks, the agent) and is
    /// recorded as a new delivery that points at the original, with <paramref name="triggeredBy"/>
    /// stored as the user who asked for it.
    /// </summary>
    Task<ServiceResult<WebhookRedeliveryResult>> RedeliverAsync(
        string tenantId, string integrationId, Guid incomingId, string? triggeredBy, CancellationToken cancellationToken = default);
}

public class WebhookRedeliveryService : IWebhookRedeliveryService
{
    public const string HttpClientName = "WebhookRedelivery";

    private const string BuiltinWebhookPlatformId = "builtin_webhook";
    private const string BuiltinWebhookPath = "/api/user/webhooks/builtin";

    /// <summary>The webhook's default wait for the agent when its URL sets none.</summary>
    private const int DefaultWebhookTimeoutSeconds = 60;

    /// <summary>Added to the webhook's own wait, so the webhook answers (with its timeout) before we give up.</summary>
    private static readonly TimeSpan TimeoutMargin = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Longest the admin call waits in all. Callers such as Agent Studio run on Node, whose fetch
    /// gives up after 300 s without response headers, so this answers before that. A webhook that
    /// waits longer is reported as timed out; its call is still recorded and runs the agent.
    /// </summary>
    internal static readonly TimeSpan MaxWait = TimeSpan.FromSeconds(290);

    /// <summary>
    /// Headers that describe the original connection rather than the call, or that the client sets
    /// itself. Sending them again would be wrong (Host, Content-Length) or would let the stored call
    /// claim a client address it no longer comes from (X-Forwarded-For).
    /// </summary>
    private static readonly HashSet<string> NotResent = new(StringComparer.OrdinalIgnoreCase)
    {
        "Host", "Content-Length", "Content-Type", "Transfer-Encoding", "Connection", "Keep-Alive",
        "Upgrade", "TE", "Trailer", "Proxy-Connection", "Expect", "Accept-Encoding",
        "Forwarded", "X-Forwarded-For", "X-Forwarded-Host", "X-Forwarded-Proto", "X-Forwarded-Port",
        "X-Forwarded-Prefix", "X-Real-IP", "X-Original-For", "X-Original-Proto", "X-Original-Host",
        "traceparent", "tracestate", "baggage", "Request-Id",
        WebhookRedeliverySigner.TokenHeader, WebhookRedeliverySigner.RedeliveryOfHeader
    };

    private readonly IWebhookIncomingRepository _incomings;
    private readonly IAppIntegrationRepository _integrations;
    private readonly IWebhookRedeliverySigner _signer;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServer _server;
    private readonly IAuditLogService _auditLog;
    private readonly WebhookIncomingsOptions _options;
    private readonly ILogger<WebhookRedeliveryService> _logger;

    public WebhookRedeliveryService(
        IWebhookIncomingRepository incomings,
        IAppIntegrationRepository integrations,
        IWebhookRedeliverySigner signer,
        IHttpClientFactory httpClientFactory,
        IServer server,
        IAuditLogService auditLog,
        IOptions<WebhookIncomingsOptions> options,
        ILogger<WebhookRedeliveryService> logger)
    {
        _incomings = incomings ?? throw new ArgumentNullException(nameof(incomings));
        _integrations = integrations ?? throw new ArgumentNullException(nameof(integrations));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _server = server ?? throw new ArgumentNullException(nameof(server));
        _auditLog = auditLog ?? throw new ArgumentNullException(nameof(auditLog));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
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

        var path = ConfigValue(integration, "webhookUrl");
        if (path == null || !path.StartsWith(BuiltinWebhookPath + "?", StringComparison.Ordinal))
            return ServiceResult<WebhookRedeliveryResult>.Conflict("This webhook has no URL to redeliver to");

        var baseUrl = ResolveBaseUrl();
        if (baseUrl == null)
        {
            _logger.LogError("Cannot redeliver: the server has no listening address; set WebhookIncomings__RedeliveryBaseUrl");
            return ServiceResult<WebhookRedeliveryResult>.InternalServerError("Redelivery is not configured on this server");
        }

        // Always the first delivery from the source, so every redelivery of it shares one label.
        var claim = new WebhookRedeliveryClaim(
            tenantId,
            integrationId,
            original.RedeliveryOf ?? original.Id,
            original.RedeliveryOfRequestId ?? original.RequestId,
            Guid.CreateVersion7(),
            triggeredBy,
            DateTimeOffset.UtcNow.Add(WebhookRedeliverySigner.Lifetime).ToUnixTimeSeconds());

        string token;
        try
        {
            token = _signer.Sign(claim);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogError(ex, "Cannot redeliver: no signing key");
            return ServiceResult<WebhookRedeliveryResult>.InternalServerError("Redelivery is not configured on this server");
        }

        // One redelivery of a call at a time: a second click while the first is still waiting on
        // the agent would run it twice.
        var inFlightKey = $"{tenantId}|{integrationId}|{incomingId}";
        if (!InFlight.TryAdd(inFlightKey, 0))
            return ServiceResult<WebhookRedeliveryResult>.Conflict("This call is already being redelivered");

        try
        {
            return await SendAsync(integration, original, incomingId, claim, token, new Uri(baseUrl, path), cancellationToken);
        }
        finally
        {
            InFlight.TryRemove(inFlightKey, out _);
        }
    }

    /// <summary>Redeliveries currently waiting on the webhook, keyed by tenant, webhook and call.</summary>
    private static readonly ConcurrentDictionary<string, byte> InFlight = new();

    private async Task<ServiceResult<WebhookRedeliveryResult>> SendAsync(
        AppIntegration integration, WebhookIncoming original, Guid incomingId, WebhookRedeliveryClaim claim,
        string token, Uri target, CancellationToken cancellationToken)
    {
        using var request = BuildRequest(target, original, claim, token);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Wait(integration));

        int? statusCode = null;
        string outcome;
        ServiceResult<WebhookRedeliveryResult>? failure = null;
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName)
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            statusCode = (int)response.StatusCode;
            outcome = "answered";
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            outcome = "timedOut";
            failure = ServiceResult<WebhookRedeliveryResult>.RequestTimeout("The webhook did not answer in time");
        }
        catch (OperationCanceledException)
        {
            // The caller went away. The call may already be running the agent, so it is audited.
            await AuditAsync(integration, claim, "cancelled", statusCode: null);
            throw;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Redelivery of webhook call {IncomingId} could not reach {Target}", incomingId, target.GetLeftPart(UriPartial.Authority));
            outcome = "unreachable";
            failure = ServiceResult<WebhookRedeliveryResult>.InternalServerError("Could not reach the webhook URL");
        }
        stopwatch.Stop();

        // Every outcome is audited: a call that timed out was still recorded and may still be
        // running the agent. The deliveryId says whether the webhook recorded it.
        var recorded = await AuditAsync(integration, claim, outcome, statusCode);
        if (failure != null)
            return failure;

        if (!recorded)
        {
            _logger.LogWarning(
                "Redelivery of webhook call {IncomingId} was answered {StatusCode} and not recorded",
                incomingId, statusCode);
        }

        return ServiceResult<WebhookRedeliveryResult>.Success(new WebhookRedeliveryResult(
            recorded ? claim.DeliveryId : null, claim.RedeliveryOf, statusCode!.Value, stopwatch.ElapsedMilliseconds));
    }

    /// <summary>
    /// Writes the audit entry for a redelivery attempt and returns whether the webhook recorded the
    /// call. The endpoint records a call before it runs the agent, so the row exists unless the call
    /// was turned away first (bad key, inactive activation) or never arrived.
    /// </summary>
    private async Task<bool> AuditAsync(AppIntegration integration, WebhookRedeliveryClaim claim, string outcome, int? statusCode)
    {
        var recorded = await _incomings.ExistsAsync(claim.TenantId, claim.IntegrationId, claim.DeliveryId);

        // Builds the entry here and writes it in the background, by the audit service's contract.
        _ = _auditLog.RecordEntryAsync(
            action: DomainEventTypes.IntegrationWebhookRedelivered,
            description: $"Call {claim.RedeliveryOfRequestId} was redelivered to builtin webhook '{integration.Name}'.",
            details: new
            {
                integrationId = claim.IntegrationId,
                // As strings: the audit store cannot serialize a Guid in the free-form details.
                redeliveryOf = claim.RedeliveryOf.ToString(),
                deliveryId = recorded ? claim.DeliveryId.ToString() : null,
                outcome,
                statusCode
            },
            tenantId: claim.TenantId);

        return recorded;
    }

    /// <summary>The stored call as an HTTP request: the body byte for byte, its headers, and the signed claim.</summary>
    internal static HttpRequestMessage BuildRequest(Uri target, WebhookIncoming original, WebhookRedeliveryClaim claim, string token)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(original.Body));
        if (!string.IsNullOrWhiteSpace(original.ContentType))
        {
            content.Headers.TryAddWithoutValidation("Content-Type", original.ContentType);
        }

        var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = content };
        foreach (var header in original.Headers.Where(h => !NotResent.Contains(h.Name.Trim())))
        {
            // A header HttpRequestHeaders refuses (Content-Language, Content-Encoding…) belongs on the content.
            if (!request.Headers.TryAddWithoutValidation(header.Name, header.Value))
            {
                content.Headers.TryAddWithoutValidation(header.Name, header.Value);
            }
        }
        request.Headers.TryAddWithoutValidation(WebhookRedeliverySigner.TokenHeader, token);
        request.Headers.TryAddWithoutValidation(WebhookRedeliverySigner.RedeliveryOfHeader, claim.RedeliveryOf.ToString());
        return request;
    }

    /// <summary>
    /// <see cref="WebhookIncomingsOptions.RedeliveryBaseUrl"/>, or else one of the server's own
    /// listening addresses (http preferred, so no certificate has to match). Never a host taken from
    /// the admin request, which would let a caller point the redelivery at any host.
    /// </summary>
    private Uri? ResolveBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(_options.RedeliveryBaseUrl))
            return new Uri(_options.RedeliveryBaseUrl);

        var addresses = _server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var address = addresses.FirstOrDefault(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            ?? addresses.FirstOrDefault(a => a.StartsWith("https://", StringComparison.OrdinalIgnoreCase));
        return address == null ? null : ToLoopback(address);
    }

    /// <summary>A listening address with a wildcard host (<c>+</c>, <c>*</c>, <c>0.0.0.0</c>, <c>[::]</c>) pointed at localhost.</summary>
    internal static Uri? ToLoopback(string address)
    {
        var schemeEnd = address.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
            return null;

        var rest = address[(schemeEnd + 3)..];
        var portStart = rest.StartsWith('[') ? rest.IndexOf("]:", StringComparison.Ordinal) + 1 : rest.LastIndexOf(':');
        var host = portStart > 0 ? rest[..portStart] : rest.TrimEnd('/');
        var port = portStart > 0 ? rest[portStart..].TrimEnd('/') : string.Empty;
        if (host is "+" or "*" or "0.0.0.0" or "[::]")
        {
            host = "localhost";
        }

        return Uri.TryCreate($"{address[..schemeEnd]}://{host}{port}", UriKind.Absolute, out var uri) ? uri : null;
    }

    /// <summary>How long to wait for the webhook: its own wait plus a margin, at most <see cref="MaxWait"/>.</summary>
    internal static TimeSpan Wait(AppIntegration integration)
    {
        var wait = TimeSpan.FromSeconds(WebhookTimeoutSeconds(integration)) + TimeoutMargin;
        return wait < MaxWait ? wait : MaxWait;
    }

    /// <summary>How long the webhook waits for the agent: its URL's <c>timeoutSeconds</c> (stored as <c>timeoutInSeconds</c>).</summary>
    private static int WebhookTimeoutSeconds(AppIntegration integration) =>
        int.TryParse(ConfigValue(integration, "timeoutInSeconds"), out var seconds) && seconds > 0
            ? Math.Min(seconds, 300)
            : DefaultWebhookTimeoutSeconds;

    private static string? ConfigValue(AppIntegration integration, string key) =>
        integration.Configuration.TryGetValue(key, out var value) ? value?.ToString() : null;
}
