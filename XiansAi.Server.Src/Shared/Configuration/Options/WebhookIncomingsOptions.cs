using System.ComponentModel.DataAnnotations;

namespace Shared.Configuration;

/// <summary>
/// Configuration for recording calls received by builtin webhooks (collection <c>webhook_incomings</c>).
/// Bound from the "WebhookIncomings" configuration section, e.g. <c>WebhookIncomings__MaxPerWebhook=200</c>.
/// Validated at startup, so an out-of-range value stops the server instead of deleting or rejecting calls.
/// </summary>
public class WebhookIncomingsOptions
{
    public const string SectionName = "WebhookIncomings";

    /// <summary>
    /// Upper bound for <see cref="MaxStoredBodyBytes"/>: MongoDB's 16 MB document limit, less room for
    /// headers and the other fields.
    /// </summary>
    public const int MaxStoredBodyBytesLimit = 15 * 1024 * 1024;

    /// <summary>
    /// Master switch. When false, incoming calls are not recorded.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Number of calls kept per builtin webhook. Older calls are deleted.
    /// </summary>
    [Range(1, 10_000)]
    public int MaxPerWebhook { get; set; } = 100;

    /// <summary>
    /// Largest request body, in bytes, that is stored.
    /// </summary>
    [Range(1, MaxStoredBodyBytesLimit)]
    public int MaxStoredBodyBytes { get; set; } = 1_048_576;

    /// <summary>
    /// When false, only metadata and headers are stored, without the request body.
    /// </summary>
    public bool StoreBodies { get; set; } = true;
}
