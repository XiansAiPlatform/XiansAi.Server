using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// One inbound call received by a builtin webhook (including redeliveries), persisted in the
/// MongoDB collection <c>webhook_incomings</c>. Powers the connection's Deliveries page and
/// holds what is needed to redeliver the call. Credential headers are removed by
/// <see cref="RemoveCredentialHeaders"/>, which the repository runs on every insert.
/// </summary>
[BsonIgnoreExtraElements]
public class WebhookIncoming
{
    /// <summary>Headers that carry the caller's credentials. They are never stored.</summary>
    public static readonly IReadOnlySet<string> CredentialHeaders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "X-Api-Key"
    };

    /// <summary>
    /// Largest document MongoDB accepts (16 MB). Covers the whole row; when a call is bigger, its
    /// body is truncated to fit.
    /// </summary>
    public const int MaxDocumentBytes = 16 * 1024 * 1024;

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

    /// <summary>When the call arrived (UTC). Drives the list order and the per-webhook cap.</summary>
    [BsonElement("received_at")]
    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("content_type")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Request headers in the order received, minus <see cref="CredentialHeaders"/>.
    /// Kept as a list so repeated headers survive and header names never become BSON field names.
    /// </summary>
    [BsonElement("headers")]
    public List<WebhookIncomingHeader> Headers { get; set; } = new();

    /// <summary>
    /// Raw request body, kept exactly as received so signatures still verify on redelivery,
    /// unless <see cref="BodyTruncated"/> is set.
    /// </summary>
    [BsonElement("body")]
    public string Body { get; set; } = string.Empty;

    /// <summary>
    /// True when the body was cut short to fit <see cref="MaxDocumentBytes"/>. A truncated call
    /// cannot be redelivered: the stored body is not what was sent, so its signature would not verify.
    /// </summary>
    [BsonElement("body_truncated")]
    public bool BodyTruncated { get; set; }

    /// <summary>Target workflow id: <c>{tenantId}:{agentName}:{workflowName}[:{activationName}]</c>.</summary>
    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }

    /// <summary>
    /// Cuts the body so the whole row is at most <paramref name="maxBytes"/> when serialized,
    /// never splitting a character, and sets <see cref="BodyTruncated"/>.
    /// </summary>
    /// <param name="rowBytes">The row's current serialized size.</param>
    public void TruncateBody(int rowBytes, int maxBytes)
    {
        var body = Encoding.UTF8.GetBytes(Body);
        var keep = Math.Max(0, body.Length - (rowBytes - maxBytes));
        // Back up to the start of a character: UTF-8 continuation bytes look like 10xxxxxx.
        while (keep > 0 && keep < body.Length && (body[keep] & 0xC0) == 0x80)
        {
            keep--;
        }

        Body = Encoding.UTF8.GetString(body, 0, keep);
        BodyTruncated = true;
    }

    /// <summary>Drops every header in <see cref="CredentialHeaders"/>, whatever its casing.</summary>
    /// <returns>The number of headers removed.</returns>
    public int RemoveCredentialHeaders() =>
        Headers.RemoveAll(h => h.Name != null && CredentialHeaders.Contains(h.Name.Trim()));
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
public record WebhookIncomingSummary(Guid Id, string RequestId, DateTime ReceivedAt, bool BodyTruncated);

/// <summary>
/// Position in a list of incoming calls sorted newest first. Pass the last row of one page to get the next.
/// </summary>
public record WebhookIncomingCursor(DateTime ReceivedAt, Guid Id);
