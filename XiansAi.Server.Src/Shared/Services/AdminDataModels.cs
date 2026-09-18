using System.Text.Json;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Request models for AdminData operations.
/// Tenant identity is always taken from the route, never from these bodies.
/// </summary>
public class AdminDataSchemaRequest
{
    public string TenantId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string? ActivationName { get; set; }
}

public class AdminDataListRequest
{
    public string TenantId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string? ActivationName { get; set; }
    public string DataType { get; set; } = string.Empty;
    public int Skip { get; set; } = 0;
    public int Limit { get; set; } = 100;
}

public class AdminDataDeleteRequest
{
    public string TenantId { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string? ActivationName { get; set; }
    public string DataType { get; set; } = string.Empty;
}

public class AdminDataDeleteRecordRequest
{
    public string TenantId { get; set; } = string.Empty;
    public string RecordId { get; set; } = string.Empty;
}

public class AdminDataCreateRequest
{
    public string AgentName { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public JsonElement Content { get; set; }
    public string? ActivationName { get; set; }
    public string? ParticipantId { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public string? WorkflowId { get; set; }
}

public class AdminDataUpdateRequest
{
    public string? DataType { get; set; }
    public string? Key { get; set; }
    public JsonElement Content { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public string? ParticipantId { get; set; }
    public string? ActivationName { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

/// <summary>
/// Response models for AdminData operations.
/// </summary>
public class AdminDataSchemaResponse
{
    public AdminDataPeriod Period { get; set; } = new();
    public AdminDataFilters Filters { get; set; } = new();
    public List<string> Types { get; set; } = new();
}

public class AdminDataPeriod
{
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
}

public class AdminDataFilters
{
    public string? AgentName { get; set; }
    public string? ActivationName { get; set; }
}

public class AdminDataListResponse
{
    public List<AdminDataItemResponse> Data { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Limit { get; set; }
}

public class AdminDataItemResponse
{
    public string Id { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string? Type { get; set; }
    public string? AgentName { get; set; }
    public string? ActivationName { get; set; }
    public string? ParticipantId { get; set; }
    public JsonElement Content { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class AdminDataDeleteResponse
{
    public int DeletedCount { get; set; }
    public AdminDataPeriod Period { get; set; } = new();
    public AdminDataFilters Filters { get; set; } = new();
    public string DataType { get; set; } = string.Empty;
}

public class AdminDataDeleteRecordResponse
{
    public bool Deleted { get; set; }
    public string RecordId { get; set; } = string.Empty;
    public AdminDataItemResponse? DeletedRecord { get; set; }
}

/// <summary>
/// Service interface for admin data operations.
/// </summary>
public interface IAdminDataService
{
    Task<ServiceResult<AdminDataSchemaResponse>> GetDataSchemaAsync(
        AdminDataSchemaRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminDataListResponse>> GetDataAsync(
        AdminDataListRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminDataItemResponse>> GetRecordAsync(
        string tenantId,
        string recordId,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminDataItemResponse>> CreateDataAsync(
        string tenantId,
        AdminDataCreateRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminDataItemResponse>> UpdateDataAsync(
        string tenantId,
        string recordId,
        AdminDataUpdateRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminDataDeleteResponse>> DeleteDataAsync(
        AdminDataDeleteRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<AdminDataDeleteRecordResponse>> DeleteRecordAsync(
        AdminDataDeleteRecordRequest request,
        CancellationToken cancellationToken = default);

    Task<ServiceResult<int>> DeleteDocumentsByActivationAsync(string tenantId, string agentName, string activationName);
}
