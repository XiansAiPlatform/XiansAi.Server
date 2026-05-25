using System.Text.Json.Serialization;
using Shared.Repositories;

namespace Shared.Data.Models;

/// <summary>
/// API projection of a conversation message with optional embedded human feedback.
/// </summary>
public class ConversationMessageDto : ConversationMessage
{
    [JsonPropertyName("feedback")]
    public FeedbackDto? Feedback { get; set; }
}

/// <summary>
/// Read-only feedback summary returned on messages (e.g. in history).
/// </summary>
public class FeedbackDto
{
    [JsonPropertyName("starRating")]
    public int StarRating { get; set; }

    [JsonPropertyName("reasonCategory")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FeedbackReasonCategory? ReasonCategory { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("submittedBy")]
    public string SubmittedBy { get; set; } = string.Empty;

    [JsonPropertyName("submittedAt")]
    public DateTime SubmittedAt { get; set; }
}
