namespace XiansAi.Server.Shared.Data.Models;

/// <summary>
/// Base interface for all knowledge-based entities
/// </summary>
public interface IKnowledge
{
    string? Agent { get; set; }
    string? TenantId { get; set; }
    string Id { get; set; }
    string Name { get; set; }
    string Version { get; set; }
    DateTime CreatedAt { get; set; }
    string CreatedBy { get; set; }
} 