using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// One inbound call received by a builtin webhook (including redeliveries), persisted in the
/// MongoDB collection <c>webhook_incomings</c>. Powers the connection's Deliveries page and
/// holds what is needed to redeliver the call. Secrets are stripped before a row is saved.
/// </summary>
[BsonIgnoreExtraElements]
public class WebhookIncoming
{
    /// <summary>Delivery id. A UUID v7, so ids sort in the order they were created.</summary>
    [BsonId]
    [BsonGuidRepresentation(GuidRepresentation.Standard)]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    /// <summary>Id of the builtin webhook app integration (<c>app_integrations._id</c>) the call came in on.</summary>
    [BsonElement("integration_id")]
    public required string IntegrationId { get; set; }

    /// <summary>Label shown for the delivery. A redelivery copies the original's value.</summary>
    [BsonElement("request_id")]
    public required string RequestId { get; set; }

    /// <summary>When the call arrived (UTC). Drives the list order, the per-webhook cap and the TTL index.</summary>
    [BsonElement("received_at")]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("content_type")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Request headers in the order received, minus credentials (Authorization, Cookie, X-Api-Key).
    /// Kept as a list so repeated headers survive and header names never become BSON field names.
    /// </summary>
    [BsonElement("headers")]
    public List<WebhookIncomingHeader> Headers { get; set; } = new();

    /// <summary>Raw request body, kept exactly as received so signatures still verify on redelivery.</summary>
    [BsonElement("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>Target workflow id: <c>{tenantId}:{agentName}:{workflowName}[:{activationName}]</c>.</summary>
    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }
}

/// <summary>One request header line of a <see cref="WebhookIncoming"/>.</summary>
public class WebhookIncomingHeader
{
    [BsonElement("name")]
    public required string Name { get; set; }

    [BsonElement("value")]
    public required string Value { get; set; }
}

/// <summary>The fields shown for an incoming call in a list.</summary>
public record WebhookIncomingSummary(Guid Id, string RequestId, DateTime ReceivedAt);

/// <summary>
/// Position in a list of incoming calls sorted newest first. Pass the last row of one page to get the next.
/// </summary>
public record WebhookIncomingCursor(DateTime ReceivedAt, Guid Id);
