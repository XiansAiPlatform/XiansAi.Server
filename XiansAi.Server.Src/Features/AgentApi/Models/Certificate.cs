// Features/AgentApi/Models/Certificate.cs
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System;

namespace Features.AgentApi.Models;

public class Certificate
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();
    
    public string Thumbprint { get; set; } = string.Empty;
    public string SubjectName { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string IssuedTo { get; set; } = string.Empty;
    public DateTime IssuedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool IsRevoked { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime? RevokedAt { get; set; }
    
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    [BsonDateTimeOptions(Kind = DateTimeKind.Utc)]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class CertificateValidationResult
{
    public bool IsValid => !Errors.Any();
    public List<string> Errors { get; } = new();
    public void AddError(string error) => Errors.Add(error);
}