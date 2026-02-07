using System.Text.Json.Serialization;

namespace Features.AppsApi.Models;

/// <summary>
/// Contains all sensitive/secret values for an app integration.
/// This entire object is encrypted at rest in the database.
/// </summary>
public class AppIntegrationSecrets
{
    /// <summary>
    /// Webhook secret for validating incoming webhook requests.
    /// Used in the webhook URL path for defense-in-depth.
    /// </summary>
    [JsonPropertyName("webhookSecret")]
    public string? WebhookSecret { get; set; }

    /// <summary>
    /// Slack signing secret for verifying webhook signatures
    /// </summary>
    [JsonPropertyName("slackSigningSecret")]
    public string? SlackSigningSecret { get; set; }

    /// <summary>
    /// Slack bot token for API calls
    /// </summary>
    [JsonPropertyName("slackBotToken")]
    public string? SlackBotToken { get; set; }

    /// <summary>
    /// Slack incoming webhook URL (contains embedded secret)
    /// </summary>
    [JsonPropertyName("slackIncomingWebhookUrl")]
    public string? SlackIncomingWebhookUrl { get; set; }

    /// <summary>
    /// Microsoft Teams/Bot Framework app password
    /// </summary>
    [JsonPropertyName("teamsAppPassword")]
    public string? TeamsAppPassword { get; set; }

    /// <summary>
    /// Outlook/Graph client secret
    /// </summary>
    [JsonPropertyName("outlookClientSecret")]
    public string? OutlookClientSecret { get; set; }

    /// <summary>
    /// Generic webhook HMAC secret
    /// </summary>
    [JsonPropertyName("genericWebhookSecret")]
    public string? GenericWebhookSecret { get; set; }

    /// <summary>
    /// Additional custom secrets (extensibility for future platforms)
    /// </summary>
    [JsonPropertyName("customSecrets")]
    public Dictionary<string, string?> CustomSecrets { get; set; } = new();

    /// <summary>
    /// Creates a masked copy for API responses (shows first 4 and last 4 chars)
    /// </summary>
    public AppIntegrationSecrets Mask()
    {
        return new AppIntegrationSecrets
        {
            WebhookSecret = MaskSecret(WebhookSecret),
            SlackSigningSecret = MaskSecret(SlackSigningSecret),
            SlackBotToken = MaskSecret(SlackBotToken),
            SlackIncomingWebhookUrl = MaskSecret(SlackIncomingWebhookUrl),
            TeamsAppPassword = MaskSecret(TeamsAppPassword),
            OutlookClientSecret = MaskSecret(OutlookClientSecret),
            GenericWebhookSecret = MaskSecret(GenericWebhookSecret),
            CustomSecrets = CustomSecrets.ToDictionary<KeyValuePair<string, string?>, string, string?>(
                kvp => kvp.Key,
                kvp => MaskSecret(kvp.Value))
        };
    }

    private static string? MaskSecret(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
            return secret;

        if (secret.Length > 8)
            return secret[..4] + "****" + secret[^4..];
        
        if (secret.Length > 0)
            return "****";
        
        return secret;
    }

    /// <summary>
    /// Checks if any secrets are configured
    /// </summary>
    public bool HasAnySecrets()
    {
        return !string.IsNullOrEmpty(WebhookSecret) ||
               !string.IsNullOrEmpty(SlackSigningSecret) ||
               !string.IsNullOrEmpty(SlackBotToken) ||
               !string.IsNullOrEmpty(SlackIncomingWebhookUrl) ||
               !string.IsNullOrEmpty(TeamsAppPassword) ||
               !string.IsNullOrEmpty(OutlookClientSecret) ||
               !string.IsNullOrEmpty(GenericWebhookSecret) ||
               CustomSecrets.Any();
    }
}
