using System.Text.Json.Serialization;

namespace Features.AppsApi.Models;

/// <summary>
/// Microsoft Teams Bot Framework Activity
/// Represents incoming messages and events from Teams
/// </summary>
public class TeamsActivity
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("serviceUrl")]
    public string? ServiceUrl { get; set; }

    [JsonPropertyName("channelId")]
    public string? ChannelId { get; set; }

    [JsonPropertyName("from")]
    public TeamsChannelAccount? From { get; set; }

    [JsonPropertyName("conversation")]
    public TeamsConversation? Conversation { get; set; }

    [JsonPropertyName("recipient")]
    public TeamsChannelAccount? Recipient { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("textFormat")]
    public string? TextFormat { get; set; }

    [JsonPropertyName("attachments")]
    public List<TeamsAttachment>? Attachments { get; set; }

    [JsonPropertyName("entities")]
    public List<TeamsEntity>? Entities { get; set; }

    [JsonPropertyName("channelData")]
    public TeamsChannelData? ChannelData { get; set; }

    [JsonPropertyName("replyToId")]
    public string? ReplyToId { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// Teams channel account (user or bot)
/// </summary>
public class TeamsChannelAccount
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("aadObjectId")]
    public string? AadObjectId { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}

/// <summary>
/// Teams conversation
/// </summary>
public class TeamsConversation
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("conversationType")]
    public string? ConversationType { get; set; }

    [JsonPropertyName("tenantId")]
    public string? TenantId { get; set; }

    [JsonPropertyName("isGroup")]
    public bool? IsGroup { get; set; }
}

/// <summary>
/// Teams attachment (e.g., Adaptive Card)
/// </summary>
public class TeamsAttachment
{
    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }

    [JsonPropertyName("content")]
    public object? Content { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Teams entity (mentions, etc.)
/// </summary>
public class TeamsEntity
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("mentioned")]
    public TeamsChannelAccount? Mentioned { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }
}

/// <summary>
/// Teams channel-specific data
/// </summary>
public class TeamsChannelData
{
    [JsonPropertyName("teamsChannelId")]
    public string? TeamsChannelId { get; set; }

    [JsonPropertyName("teamsTeamId")]
    public string? TeamsTeamId { get; set; }

    [JsonPropertyName("channel")]
    public TeamsChannelInfo? Channel { get; set; }

    [JsonPropertyName("team")]
    public TeamsTeamInfo? Team { get; set; }

    [JsonPropertyName("tenant")]
    public TeamsTenantInfo? Tenant { get; set; }
}

/// <summary>
/// Teams channel information
/// </summary>
public class TeamsChannelInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Teams team information
/// </summary>
public class TeamsTeamInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

/// <summary>
/// Teams tenant information
/// </summary>
public class TeamsTenantInfo
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

/// <summary>
/// Response to send back to Teams
/// </summary>
public class TeamsResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "message";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("attachments")]
    public List<TeamsAttachment>? Attachments { get; set; }
}

/// <summary>
/// Constants for Teams Bot Framework
/// </summary>
public static class TeamsConstants
{
    // Activity types
    public const string MessageActivityType = "message";
    public const string ConversationUpdateActivityType = "conversationUpdate";
    public const string InvokeActivityType = "invoke";
    public const string EventActivityType = "event";

    // Channel IDs
    public const string MsTeamsChannelId = "msteams";

    // Conversation types
    public const string PersonalConversationType = "personal";
    public const string GroupConversationType = "groupChat";
    public const string ChannelConversationType = "channel";
}
