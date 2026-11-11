using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Shared.Data.Models;

/// <summary>
/// One inbound call received by a builtin webhook (including redeliveries), persisted in the
/// MongoDB collection <c>webhook_incomings</c>. Powers the connection's Deliveries page and
/// holds what is needed to redeliver the call. The headers are stored encrypted as one value by
/// <see cref="EncryptHeaders"/>, which the repository runs on every insert.
/// </summary>
[BsonIgnoreExtraElements]
public class WebhookIncoming
{
    /// <summary>
    /// Headers that carry the caller's credentials. Their values are never returned by the API
    /// (see <see cref="WebhookIncomingHeader.Masked"/>), only used to redeliver the call.
    /// </summary>
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

    /// <summary>
    /// Most header bytes (names plus values, UTF-8) kept for one call: 16 KB. Kestrel accepts up to
    /// 32 KB; a GitHub delivery sends about 2 KB. When a call is bigger, the longest values are cut
    /// (see <see cref="TruncateHeaders"/>).
    /// </summary>
    public const int MaxHeadersBytes = 16 * 1024;

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
    /// Request headers in the order received, one entry per value so repeated headers survive.
    /// Never stored as is: it is saved encrypted in <see cref="EncryptedHeaders"/>.
    /// </summary>
    [BsonIgnore]
    public List<WebhookIncomingHeader> Headers { get; set; } = new();

    /// <summary>Every header of the call, serialized and encrypted as one value. Not returned by the API.</summary>
    [BsonElement("headers_encrypted")]
    [JsonIgnore]
    public string? EncryptedHeaders { get; set; }

    /// <summary>
    /// True when the headers were over <see cref="MaxHeadersBytes"/> and some values were cut.
    /// Like a truncated body, the call cannot be redelivered as sent.
    /// </summary>
    [BsonElement("headers_truncated")]
    public bool HeadersTruncated { get; set; }

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

    /// <summary>
    /// Cuts header values so names plus values are at most <paramref name="maxBytes"/>, and sets
    /// <see cref="HeadersTruncated"/>. Every header is kept; only values longer than a common cap
    /// are shortened, so one huge header cannot push out the small ones (event name, signature).
    /// </summary>
    /// <returns>True when anything was cut.</returns>
    public bool TruncateHeaders(int maxBytes)
    {
        var lengths = Headers.Select(h => Encoding.UTF8.GetByteCount(h.Value)).ToList();
        var budget = maxBytes - Headers.Sum(h => Encoding.UTF8.GetByteCount(h.Name));
        if (lengths.Sum() <= budget)
        {
            return false;
        }

        var cap = ValueCap(lengths, Math.Max(0, budget));
        for (var i = 0; i < Headers.Count; i++)
        {
            if (lengths[i] > cap)
            {
                Headers[i].Value = Utf8Prefix(Headers[i].Value, cap);
            }
        }

        HeadersTruncated = true;
        return true;
    }

    /// <summary>Encrypts <see cref="Headers"/> into <see cref="EncryptedHeaders"/>.</summary>
    public void EncryptHeaders(Func<string, string> encrypt) =>
        EncryptedHeaders = encrypt(JsonSerializer.Serialize(Headers));

    /// <summary>Fills <see cref="Headers"/> from <see cref="EncryptedHeaders"/>. A row without headers gets an empty list.</summary>
    public void DecryptHeaders(Func<string, string> decrypt) =>
        Headers = string.IsNullOrEmpty(EncryptedHeaders)
            ? new()
            : JsonSerializer.Deserialize<List<WebhookIncomingHeader>>(decrypt(EncryptedHeaders)) ?? new();

    /// <summary>Replaces the value of every credential header with <paramref name="masked"/> and marks it.</summary>
    public void MaskCredentialHeaders(string masked)
    {
        foreach (var header in Headers.Where(h => CredentialHeaders.Contains(h.Name.Trim())))
        {
            header.Value = masked;
            header.Masked = true;
        }
    }

    /// <summary>The largest per-value length at which all values fit in <paramref name="budget"/> bytes.</summary>
    private static int ValueCap(List<int> lengths, int budget)
    {
        var sorted = lengths.OrderBy(l => l).ToList();
        var used = 0;
        for (var i = 0; i < sorted.Count; i++)
        {
            var remaining = sorted.Count - i;
            // Values from here on are at least sorted[i]; if they all fit at that length, keep going.
            if (used + (long)sorted[i] * remaining > budget)
            {
                return (budget - used) / remaining;
            }
            used += sorted[i];
        }
        return sorted.Count == 0 ? 0 : sorted[^1];
    }

    /// <summary>The first <paramref name="maxBytes"/> UTF-8 bytes of a string, never splitting a character.</summary>
    private static string Utf8Prefix(string value, int maxBytes)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var keep = Math.Min(maxBytes, bytes.Length);
        // Back up to the start of a character: UTF-8 continuation bytes look like 10xxxxxx.
        while (keep > 0 && keep < bytes.Length && (bytes[keep] & 0xC0) == 0x80)
        {
            keep--;
        }
        return Encoding.UTF8.GetString(bytes, 0, keep);
    }
}

/// <summary>One request header line of a <see cref="WebhookIncoming"/>.</summary>
public class WebhookIncomingHeader
{
    [JsonPropertyName("name")]
    public required string Name { get; set; }

    [JsonPropertyName("value")]
    public required string Value { get; set; }

    /// <summary>True when <see cref="Value"/> was replaced because the header carries credentials. Never stored.</summary>
    [JsonPropertyName("masked")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Masked { get; set; }
}

/// <summary>The fields shown for an incoming call in a list.</summary>
public record WebhookIncomingSummary(Guid Id, string RequestId, DateTime ReceivedAt, bool BodyTruncated, bool HeadersTruncated);

/// <summary>
/// Position in a list of incoming calls sorted newest first. Pass the last row of one page to get the next.
/// </summary>
public record WebhookIncomingCursor(DateTime ReceivedAt, Guid Id);
