using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// Categories for low star-ratings (typically when rating is below 4).
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeedbackReasonCategory
{
    // Legacy values (historical submissions; kept for deserialization)
    NotAccurate,
    Irrelevant,
    MissingInstructions,
    IncompleteResponse,

    FactuallyIncorrect,
    MissingImportantDetails,
    DidNotAnswerActualQuestion,
    ResponseTooGeneric,
    ResponseTooLong,
    ResponseDifficultToUnderstand,
    FabricatedInformation,
    WrongAssumptionsOrContext,
    FailedToFollowConstraints,
    ToolActionFailure,
    UnsafeOrRiskyOutput,
    PoorCodeQuality,
    PerformanceIssue,
    Other
}

/// <summary>
/// Human feedback on an agent message, stored in MongoDB collection <c>message_feedback</c>.
/// </summary>
[BsonIgnoreExtraElements]
public class MessageFeedbackDocument
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [BsonElement("message_id")]
    [JsonPropertyName("messageId")]
    public required string MessageId { get; set; }

    [BsonElement("thread_id")]
    [JsonPropertyName("threadId")]
    public required string ThreadId { get; set; }

    [BsonElement("tenant_id")]
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; set; }

    [BsonElement("agent_name")]
    [JsonPropertyName("agentName")]
    public required string AgentName { get; set; }

    [BsonElement("workflow_id")]
    [JsonPropertyName("workflowId")]
    public required string WorkflowId { get; set; }

    [BsonElement("workflow_type")]
    [JsonPropertyName("workflowType")]
    public required string WorkflowType { get; set; }

    [BsonElement("participant_id")]
    [JsonPropertyName("participantId")]
    public required string ParticipantId { get; set; }

    [BsonElement("star_rating")]
    [JsonPropertyName("starRating")]
    public int StarRating { get; set; }

    [BsonElement("reason_category")]
    [BsonRepresentation(BsonType.String)]
    [JsonPropertyName("reasonCategory")]
    public FeedbackReasonCategory? ReasonCategory { get; set; }

    [BsonElement("comment")]
    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [BsonElement("submitted_by")]
    [JsonPropertyName("submittedBy")]
    public required string SubmittedBy { get; set; }

    [BsonElement("submitted_at")]
    [JsonPropertyName("submittedAt")]
    public required DateTime SubmittedAt { get; set; }

    [BsonElement("created_at")]
    [JsonPropertyName("createdAt")]
    public required DateTime CreatedAt { get; set; }
}
