using System.Text.Json.Serialization;

namespace Features.AgentApi.Models;

/// <summary>
/// Generic document wrapper that mirrors the client library structure
/// </summary>
public class DocumentDto<T>
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("content")]
    public required T Content { get; set; }

    [JsonPropertyName("metadata")]
    public Dictionary<string, object>? Metadata { get; set; }

    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    [JsonPropertyName("workflowId")]
    public string? WorkflowId { get; set; }

    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; set; }

    [JsonPropertyName("activationName")]
    public string? ActivationName { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }

    [JsonPropertyName("createdBy")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("updatedBy")]
    public string? UpdatedBy { get; set; }
}

/// <summary>
/// Options for document storage operations
/// </summary>
public class DocumentOptions
{
    [JsonPropertyName("ttlMinutes")]
    public int? TtlMinutes { get; set; }

    [JsonPropertyName("overwrite")]
    public bool Overwrite { get; set; } = false;

    [JsonPropertyName("useKeyAsIdentifier")]
    public bool UseKeyAsIdentifier { get; set; } = false;
}

/// <summary>
/// Query parameters for searching documents
/// </summary>
public class DocumentQuery
{
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("key")]
    public string? Key { get; set; }

    [JsonPropertyName("participantId")]
    public string? ParticipantId { get; set; }

    [JsonPropertyName("activationName")]
    public string? ActivationName { get; set; }

    [JsonPropertyName("metadataFilters")]
    public Dictionary<string, object>? MetadataFilters { get; set; }

    [JsonPropertyName("limit")]
    public int? Limit { get; set; } = 100;

    [JsonPropertyName("skip")]
    public int? Skip { get; set; } = 0;

    [JsonPropertyName("sortBy")]
    public string? SortBy { get; set; }

    [JsonPropertyName("sortDescending")]
    public bool SortDescending { get; set; } = true;

    [JsonPropertyName("createdAfter")]
    public DateTime? CreatedAfter { get; set; }

    [JsonPropertyName("createdBefore")]
    public DateTime? CreatedBefore { get; set; }
}

/// <summary>
/// Request model for document save operations
/// </summary>
public class DocumentRequest<T>
{
    [JsonPropertyName("document")]
    public required DocumentDto<T> Document { get; set; }

    [JsonPropertyName("options")]
    public DocumentOptions? Options { get; set; }
}

/// <summary>
/// Request model for document ID operations
/// </summary>
public class DocumentIdRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }
}

/// <summary>
/// Request model for document key operations
/// </summary>
public class DocumentKeyRequest
{
    [JsonPropertyName("type")]
    public required string Type { get; set; }

    [JsonPropertyName("key")]
    public required string Key { get; set; }
}

/// <summary>
/// Request model for bulk delete operations
/// </summary>
public class DocumentIdsRequest
{
    [JsonPropertyName("ids")]
    public required IEnumerable<string> Ids { get; set; }
}

/// <summary>
/// Request model for document query operations
/// </summary>
public class DocumentQueryRequest
{
    [JsonPropertyName("query")]
    public required DocumentQuery Query { get; set; }

    [JsonPropertyName("contentType")]
    public string? ContentType { get; set; }
}

/// <summary>
/// Response model for bulk delete operations
/// </summary>
public class BulkDeleteResult
{
    [JsonPropertyName("deletedCount")]
    public int DeletedCount { get; set; }
}

/// <summary>
/// Response model for existence check operations
/// </summary>
public class ExistsResult
{
    [JsonPropertyName("exists")]
    public bool Exists { get; set; }
}
