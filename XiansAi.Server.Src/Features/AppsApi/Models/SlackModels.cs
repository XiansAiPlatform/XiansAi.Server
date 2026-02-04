using System.Text.Json.Serialization;

namespace Features.AppsApi.Models;

/// <summary>
/// Slack webhook payload
/// </summary>
public class SlackWebhookPayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("challenge")]
    public string? Challenge { get; set; }

    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("event")]
    public SlackEvent? Event { get; set; }

    [JsonPropertyName("event_time")]
    public long? EventTime { get; set; }

    [JsonPropertyName("authorizations")]
    public List<SlackAuthorization>? Authorizations { get; set; }
}

/// <summary>
/// Slack event within a webhook payload
/// </summary>
public class SlackEvent
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("subtype")]
    public string? Subtype { get; set; }

    [JsonPropertyName("user")]
    public string? User { get; set; }

    [JsonPropertyName("bot_id")]
    public string? BotId { get; set; }

    [JsonPropertyName("app_id")]
    public string? AppId { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("ts")]
    public string? Ts { get; set; }

    [JsonPropertyName("thread_ts")]
    public string? ThreadTs { get; set; }

    [JsonPropertyName("channel")]
    public string? Channel { get; set; }

    [JsonPropertyName("channel_type")]
    public string? ChannelType { get; set; }

    [JsonPropertyName("event_ts")]
    public string? EventTs { get; set; }
}

/// <summary>
/// Slack authorization info
/// </summary>
public class SlackAuthorization
{
    [JsonPropertyName("team_id")]
    public string? TeamId { get; set; }

    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("is_bot")]
    public bool IsBot { get; set; }
}

/// <summary>
/// Slack interactive payload (for buttons, modals, etc.)
/// </summary>
public class SlackInteractivePayload
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("user")]
    public SlackUser? User { get; set; }

    [JsonPropertyName("team")]
    public SlackTeam? Team { get; set; }

    [JsonPropertyName("channel")]
    public SlackChannel? Channel { get; set; }

    [JsonPropertyName("actions")]
    public List<SlackAction>? Actions { get; set; }

    [JsonPropertyName("response_url")]
    public string? ResponseUrl { get; set; }

    [JsonPropertyName("trigger_id")]
    public string? TriggerId { get; set; }
}

/// <summary>
/// Slack user info
/// </summary>
public class SlackUser
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Slack team info
/// </summary>
public class SlackTeam
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("domain")]
    public string? Domain { get; set; }
}

/// <summary>
/// Slack channel info
/// </summary>
public class SlackChannel
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Slack action from interactive component
/// </summary>
public class SlackAction
{
    [JsonPropertyName("action_id")]
    public string? ActionId { get; set; }

    [JsonPropertyName("block_id")]
    public string? BlockId { get; set; }

    [JsonPropertyName("text")]
    public SlackText? Text { get; set; }

    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("action_ts")]
    public string? ActionTs { get; set; }
}

/// <summary>
/// Slack text element
/// </summary>
public class SlackText
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("emoji")]
    public bool? Emoji { get; set; }
}

/// <summary>
/// Constants for Slack webhook processing
/// </summary>
public static class SlackConstants
{
    public const string UrlVerificationType = "url_verification";
    public const string EventCallbackType = "event_callback";
    public const string MessageEventType = "message";
    public const string AppMentionEventType = "app_mention";
    public const string BotMessageSubtype = "bot_message";
    public const string MessageChangedSubtype = "message_changed";
    public const string MessageDeletedSubtype = "message_deleted";
}
