using System.Security.Cryptography;
using System.Text;
using Shared.Configuration;
using Shared.Data.Models;
using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Background worker that delivers queued webhook events. On each poll it atomically claims a
/// batch of due deliveries from the outbox (so no two instances deliver the same row), POSTs the
/// stored payload to the configured listener URL with an optional HMAC signature, then records
/// the outcome and reschedules failures with exponential backoff.
/// </summary>
public class WebhookDispatcherService : BackgroundService
{
    private const string HttpClientName = "Webhooks";

    private readonly WebhooksOptions _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WebhookDispatcherService> _logger;
    private readonly string _instanceId;

    public WebhookDispatcherService(
        WebhooksOptions options,
        IServiceScopeFactory scopeFactory,
        IHttpClientFactory httpClientFactory,
        ILogger<WebhookDispatcherService> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _instanceId = $"{Environment.MachineName}:{Guid.NewGuid():N}";
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Webhook dispatcher is disabled (Webhooks:Enabled=false); not running.");
            return;
        }

        var pollInterval = TimeSpan.FromSeconds(Math.Max(1, _options.PollIntervalSeconds));
        _logger.LogInformation(
            "Webhook dispatcher started (instance {InstanceId}, poll interval {Interval}s).",
            _instanceId, pollInterval.TotalSeconds);

        ValidateSubscriptionsAtStartup();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueDeliveriesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in webhook dispatcher loop");
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Webhook dispatcher stopping (instance {InstanceId}).", _instanceId);
    }

    /// <summary>
    /// Emits non-fatal warnings for misconfigured subscriptions so operators can spot problems
    /// (invalid or insecure URLs, or unsigned deliveries) without waiting for delivery failures.
    /// </summary>
    private void ValidateSubscriptionsAtStartup()
    {
        var enabled = _options.Subscriptions.Where(s => s.Enabled).ToList();
        if (enabled.Count == 0)
        {
            _logger.LogWarning("Webhooks are enabled but no enabled subscriptions are configured.");
            return;
        }

        foreach (var subscription in enabled)
        {
            if (!TryValidateUrl(subscription.Url, out var error))
            {
                _logger.LogWarning(
                    "Webhook subscription '{Subscription}' has an invalid URL and its deliveries will fail: {Error}",
                    subscription.Name, error);
            }

            if (string.IsNullOrEmpty(subscription.Secret))
            {
                _logger.LogWarning(
                    "Webhook subscription '{Subscription}' has no Secret configured; deliveries will be unsigned " +
                    "and listeners cannot verify authenticity.",
                    subscription.Name);
            }
        }
    }

    private async Task ProcessDueDeliveriesAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IWebhookDeliveryRepository>();

        var now = DateTime.UtcNow;
        var lease = TimeSpan.FromSeconds(Math.Max(5, _options.LeaseSeconds));
        var batchSize = Math.Max(1, _options.BatchSize);

        var claimed = await repository.ClaimDueBatchAsync(_instanceId, now, lease, batchSize);
        if (claimed.Count == 0)
        {
            return;
        }

        foreach (var delivery in claimed)
        {
            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await DeliverAsync(repository, delivery, stoppingToken);
        }
    }

    private async Task DeliverAsync(
        IWebhookDeliveryRepository repository, WebhookDelivery delivery, CancellationToken stoppingToken)
    {
        var subscription = _options.Subscriptions
            .FirstOrDefault(s => string.Equals(s.Name, delivery.SubscriptionName, StringComparison.Ordinal));

        // The subscription may have been removed or disabled since the row was queued.
        if (subscription == null || !subscription.Enabled || string.IsNullOrWhiteSpace(subscription.Url))
        {
            _logger.LogWarning(
                "Abandoning webhook delivery {DeliveryId}: subscription '{Subscription}' is no longer configured.",
                delivery.Id, delivery.SubscriptionName);
            await repository.MarkFailedAsync(delivery.Id, delivery.AttemptCount + 1,
                "Subscription no longer configured or disabled");
            return;
        }

        var attemptCount = delivery.AttemptCount + 1;

        try
        {
            var (success, detail) = await SendAsync(subscription, delivery, stoppingToken);

            if (success)
            {
                await repository.MarkDeliveredAsync(delivery.Id, DateTime.UtcNow);
                _logger.LogInformation(
                    "Delivered webhook {DeliveryId} ({EventType}) to subscription '{Subscription}'.",
                    delivery.Id, delivery.EventType, subscription.Name);
                return;
            }

            await ScheduleRetryOrFailAsync(repository, delivery, attemptCount, detail);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down: release the lease so another instance/attempt can pick it up promptly.
            await repository.MarkRetryAsync(delivery.Id, delivery.AttemptCount, DateTime.UtcNow, "Dispatcher shutting down");
        }
        catch (Exception ex)
        {
            await ScheduleRetryOrFailAsync(repository, delivery, attemptCount, ex.Message);
        }
    }

    private async Task<(bool Success, string? Detail)> SendAsync(
        WebhookSubscriptionOptions subscription, WebhookDelivery delivery, CancellationToken stoppingToken)
    {
        if (!TryValidateUrl(subscription.Url, out var urlError))
        {
            return (false, urlError);
        }

        var client = _httpClientFactory.CreateClient(HttpClientName);

        using var request = new HttpRequestMessage(HttpMethod.Post, subscription.Url)
        {
            Content = new StringContent(delivery.Payload, Encoding.UTF8, "application/json")
        };

        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        request.Headers.TryAddWithoutValidation("X-Xians-Event", delivery.EventType);
        request.Headers.TryAddWithoutValidation("X-Xians-Event-Id", delivery.EventId);
        request.Headers.TryAddWithoutValidation("X-Xians-Delivery", delivery.Id);
        request.Headers.TryAddWithoutValidation("X-Xians-Attempt", (delivery.AttemptCount + 1).ToString());
        request.Headers.TryAddWithoutValidation("X-Xians-Timestamp", timestamp);

        if (!string.IsNullOrEmpty(subscription.Secret))
        {
            // Sign "{timestamp}.{body}" so a receiver enforcing a freshness window can reject
            // replayed requests; the timestamp is bound into the MAC and cannot be tampered with.
            var signature = ComputeSignature(subscription.Secret, $"{timestamp}.{delivery.Payload}");
            request.Headers.TryAddWithoutValidation("X-Xians-Signature", $"sha256={signature}");
        }

        // ResponseHeadersRead + not reading the body prevents a malicious/broken listener from
        // exhausting memory by streaming a very large response.
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stoppingToken);
        if (response.IsSuccessStatusCode)
        {
            return (true, null);
        }

        return (false, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
    }

    /// <summary>
    /// Validates that a listener URL is an absolute HTTPS URL (or HTTP when explicitly allowed).
    /// </summary>
    private bool TryValidateUrl(string url, out string? error)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            error = "Listener URL is not a valid absolute URI";
            return false;
        }

        if (uri.Scheme == Uri.UriSchemeHttps ||
            (uri.Scheme == Uri.UriSchemeHttp && _options.AllowInsecureHttp))
        {
            error = null;
            return true;
        }

        error = $"Listener URL scheme '{uri.Scheme}' is not allowed " +
                "(only https; set Webhooks:AllowInsecureHttp=true to permit http)";
        return false;
    }

    private async Task ScheduleRetryOrFailAsync(
        IWebhookDeliveryRepository repository, WebhookDelivery delivery, int attemptCount, string? detail)
    {
        if (attemptCount >= Math.Max(1, _options.MaxAttempts))
        {
            _logger.LogWarning(
                "Webhook delivery {DeliveryId} ({EventType}) failed permanently after {Attempts} attempt(s): {Detail}",
                delivery.Id, delivery.EventType, attemptCount, detail);
            await repository.MarkFailedAsync(delivery.Id, attemptCount, detail);
            return;
        }

        var nextAttemptAt = DateTime.UtcNow.Add(ComputeBackoff(attemptCount));
        _logger.LogInformation(
            "Webhook delivery {DeliveryId} ({EventType}) attempt {Attempt} failed: {Detail}. Retrying at {NextAttempt:o}.",
            delivery.Id, delivery.EventType, attemptCount, detail, nextAttemptAt);
        await repository.MarkRetryAsync(delivery.Id, attemptCount, nextAttemptAt, detail);
    }

    /// <summary>Exponential backoff capped at 5 minutes: 10s, 20s, 40s, 80s, ...</summary>
    private static TimeSpan ComputeBackoff(int attemptCount)
    {
        var seconds = 10d * Math.Pow(2, Math.Max(0, attemptCount - 1));
        return TimeSpan.FromSeconds(Math.Min(300, seconds));
    }

    private static string ComputeSignature(string secret, string payload)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
