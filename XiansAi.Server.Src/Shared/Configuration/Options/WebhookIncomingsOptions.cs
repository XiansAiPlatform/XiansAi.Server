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

    /// <summary>
    /// Base URL (scheme, host and port) a redelivery is sent to, e.g. <c>http://localhost:5000</c>.
    /// When unset, the server sends it to one of its own listening addresses. Set it when those
    /// cannot be reached from inside the server, for example behind a container port mapping.
    /// </summary>
    [AbsoluteHttpUrl]
    public string? RedeliveryBaseUrl { get; set; }
}

/// <summary>
/// Accepts null, empty or an absolute http(s) URL with no path, query or fragment. The webhook
/// path is joined as an absolute path, so a base path such as <c>/xians</c> would be dropped
/// silently; it is refused instead.
/// </summary>
[AttributeUsage(AttributeTargets.Property)]
public sealed class AbsoluteHttpUrlAttribute : ValidationAttribute
{
    public AbsoluteHttpUrlAttribute() : base("{0} must be an absolute http or https URL with only a scheme, host and port.") { }

    public override bool IsValid(object? value) =>
        value is null
        || value is string { Length: 0 }
        || (value is string text
            && Uri.TryCreate(text, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && uri.AbsolutePath == "/"
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment));
}
