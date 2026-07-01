namespace Shared.Configuration;

/// <summary>
/// Configuration for the outbound webhook system. Bound from the "Webhooks" configuration section.
/// Subscriptions (listener URLs) are defined entirely in configuration; secrets are never persisted.
/// </summary>
public class WebhooksOptions
{
    public const string SectionName = "Webhooks";

    /// <summary>
    /// Master switch. When false, no events are queued and the dispatcher does not run.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// How often the background dispatcher polls the outbox for due deliveries.
    /// </summary>
    public int PollIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Per-request HTTP timeout when POSTing to a listener URL.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Maximum number of delivery attempts before a delivery is marked as failed.
    /// </summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// How long a claimed delivery is leased to a single instance. If the instance crashes
    /// before finishing, the lease expires and another instance can reclaim the delivery.
    /// </summary>
    public int LeaseSeconds { get; set; } = 60;

    /// <summary>
    /// Maximum number of deliveries claimed and processed per poll iteration.
    /// </summary>
    public int BatchSize { get; set; } = 20;

    /// <summary>
    /// When false (default), listener URLs must use HTTPS. Set to true only for trusted
    /// internal listeners reachable over plain HTTP. Delivering event payloads (which may
    /// contain PII) over HTTP exposes them to network eavesdropping.
    /// </summary>
    public bool AllowInsecureHttp { get; set; } = false;

    /// <summary>
    /// Configured webhook listener subscriptions.
    /// </summary>
    public List<WebhookSubscriptionOptions> Subscriptions { get; set; } = new();
}

/// <summary>
/// A single configured webhook listener.
/// </summary>
public class WebhookSubscriptionOptions
{
    /// <summary>
    /// Stable identifier for this subscription. Persisted on delivery rows so the dispatcher
    /// can resolve the URL and secret from configuration at delivery time.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Destination URL that receives the HTTP POST for each matching event.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Optional shared secret. When set, an HMAC-SHA256 signature of the request body is sent
    /// in the <c>X-Xians-Signature</c> header so the listener can verify authenticity.
    /// </summary>
    public string? Secret { get; set; }

    /// <summary>
    /// Event types this subscription receives. Empty or a single "*" entry means all events.
    /// </summary>
    public List<string> EventTypes { get; set; } = new();

    /// <summary>
    /// Whether this subscription is active.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Returns true when this subscription should receive the given event type.
    /// </summary>
    public bool MatchesEvent(string eventType)
    {
        if (!Enabled)
        {
            return false;
        }

        if (EventTypes == null || EventTypes.Count == 0)
        {
            return true;
        }

        return EventTypes.Any(e =>
            e == "*" || string.Equals(e, eventType, StringComparison.OrdinalIgnoreCase));
    }
}
