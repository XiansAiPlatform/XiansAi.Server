using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Providers;

namespace Shared.Data.Models;

/// <summary>
/// A tenant's own SMTP server. When present it overrides the server default (<c>Email:Smtp</c>).
/// </summary>
[BsonIgnoreExtraElements]
public class TenantSmtpConfig
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("host")]
    public string Host { get; set; } = string.Empty;

    [BsonElement("port")]
    public int Port { get; set; } = 587;

    [BsonElement("security")]
    [BsonRepresentation(BsonType.String)]
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;

    [BsonElement("username")]
    public string? Username { get; set; }

    /// <summary>Encrypted at rest; the repository decrypts it on read.</summary>
    [BsonElement("password")]
    public string? Password { get; set; }

    [BsonElement("sender_email")]
    public string SenderEmail { get; set; } = string.Empty;

    [BsonElement("sender_name")]
    public string? SenderName { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("updated_at")]
    public required DateTime UpdatedAt { get; set; }

    [BsonElement("updated_by")]
    public required string UpdatedBy { get; set; }

    public SmtpSettings ToSettings() => new()
    {
        Host = Host,
        Port = Port,
        Security = Security,
        Username = Username,
        Password = Password,
        SenderEmail = SenderEmail,
        SenderName = SenderName
    };
}
