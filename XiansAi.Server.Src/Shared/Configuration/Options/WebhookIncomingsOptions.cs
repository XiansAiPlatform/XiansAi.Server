using System.ComponentModel.DataAnnotations;

namespace Shared.Configuration;

/// <summary>
/// Settings for recording calls received by builtin webhooks (collection <c>webhook_incomings</c>).
/// Bound from the "WebhookIncomings" configuration section, e.g. <c>WebhookIncomings__MaxPerWebhook=200</c>.
/// </summary>
public class WebhookIncomingsOptions
{
    public const string SectionName = "WebhookIncomings";

    /// <summary>
    /// How many calls each builtin webhook keeps; older ones are deleted as new ones arrive.
    /// Capped at 1,000 because trimming walks this many index entries on every call.
    /// </summary>
    [Range(1, 1_000)]
    public int MaxPerWebhook { get; set; } = 100;
}
