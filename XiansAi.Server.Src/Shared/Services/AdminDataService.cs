using Features.AgentApi.Repositories;
using Shared.Utils.Services;
using System.Text.Json;
using MongoDB.Bson;

namespace Shared.Services;

/// <summary>
/// Request models for AdminData operations.
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
    /// <summary>
    /// Get available data schema (types) for the specified filters.
    /// </summary>
    Task<ServiceResult<AdminDataSchemaResponse>> GetDataSchemaAsync(
        AdminDataSchemaRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get paginated data for the specified filters.
    /// </summary>
    Task<ServiceResult<AdminDataListResponse>> GetDataAsync(
        AdminDataListRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete all data records of a specific type within the specified filters and date range.
    /// </summary>
    Task<ServiceResult<AdminDataDeleteResponse>> DeleteDataAsync(
        AdminDataDeleteRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete a specific data record by its ID.
    /// </summary>
    Task<ServiceResult<AdminDataDeleteRecordResponse>> DeleteRecordAsync(
        AdminDataDeleteRecordRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for admin data operations.
/// Provides access to document data for admin dashboards and analytics.
/// </summary>
public class AdminDataService : IAdminDataService
{
    private readonly IDocumentRepository _documentRepository;
    private readonly ILogger<AdminDataService> _logger;

    // Validation constants
    private const int MaxDateRangeDays = 365;
    private const int MaxLimit = 1000;

    public AdminDataService(
        IDocumentRepository documentRepository,
        ILogger<AdminDataService> logger)
    {
        _documentRepository = documentRepository ?? throw new ArgumentNullException(nameof(documentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AdminDataSchemaResponse>> GetDataSchemaAsync(
        AdminDataSchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validationResult = ValidateSchemaRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Getting data schema - TenantId: {TenantId}, AgentName: {AgentName}, ActivationName: {ActivationName}, Range: {StartDate} to {EndDate}",
                request.TenantId, request.AgentName, request.ActivationName, request.StartDate, request.EndDate);

            // Get all available document types for the agent (filtered by activation name if provided)
            var documentTypes = await _documentRepository.GetDistinctTypesAsync(request.TenantId, request.AgentName, request.ActivationName);

            var response = new AdminDataSchemaResponse
            {
                Period = new AdminDataPeriod
                {
                    StartDate = request.StartDate,
                    EndDate = request.EndDate
                },
                Filters = new AdminDataFilters
                {
                    AgentName = request.AgentName,
                    ActivationName = request.ActivationName
                },
                Types = documentTypes
            };

            _logger.LogInformation(
                "Data schema retrieved successfully - AgentName: {AgentName}, Types: {TypeCount}",
                request.AgentName, documentTypes.Count);

            return ServiceResult<AdminDataSchemaResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve data schema. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminDataSchemaResponse>.InternalServerError("Failed to retrieve data schema");
        }
    }

    public async Task<ServiceResult<AdminDataListResponse>> GetDataAsync(
        AdminDataListRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validationResult = ValidateDataRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Getting data - TenantId: {TenantId}, AgentName: {AgentName}, DataType: {DataType}, ActivationName: {ActivationName}, Range: {StartDate} to {EndDate}, Skip: {Skip}, Limit: {Limit}",
                request.TenantId, request.AgentName, request.DataType, request.ActivationName, request.StartDate, request.EndDate, request.Skip, request.Limit);

            // Build query filter
            var queryFilter = new DocumentQueryFilter
            {
                AgentId = request.AgentName,
                Type = request.DataType,
                ActivationName = request.ActivationName,
                CreatedAfter = request.StartDate,
                CreatedBefore = request.EndDate,
                Skip = request.Skip,
                Limit = request.Limit,
                SortBy = "CreatedAt",
                SortDescending = true
            };

            // Get documents and count in parallel
            var documentsTask = _documentRepository.QueryAsync(request.TenantId, queryFilter);
            var countTask = _documentRepository.CountAsync(request.TenantId, new DocumentQueryFilter
            {
                AgentId = request.AgentName,
                Type = request.DataType,
                ActivationName = request.ActivationName,
                CreatedAfter = request.StartDate,
                CreatedBefore = request.EndDate
            });

            await Task.WhenAll(documentsTask, countTask);

            var documents = documentsTask.Result;
            var totalCount = countTask.Result;

            // Convert documents to response format
            var dataItems = documents.Select(ConvertToDataItemResponse).ToList();

            var response = new AdminDataListResponse
            {
                Data = dataItems,
                Total = (int)totalCount,
                Skip = request.Skip,
                Limit = request.Limit
            };

            _logger.LogInformation(
                "Data retrieved successfully - AgentName: {AgentName}, DataType: {DataType}, Total: {Total}, Returned: {Count}",
                request.AgentName, request.DataType, totalCount, dataItems.Count);

            return ServiceResult<AdminDataListResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve data. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminDataListResponse>.InternalServerError("Failed to retrieve data");
        }
    }

    public async Task<ServiceResult<AdminDataDeleteResponse>> DeleteDataAsync(
        AdminDataDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validationResult = ValidateDeleteRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Deleting data - TenantId: {TenantId}, AgentName: {AgentName}, DataType: {DataType}, ActivationName: {ActivationName}, Range: {StartDate} to {EndDate}",
                request.TenantId, request.AgentName, request.DataType, request.ActivationName, request.StartDate, request.EndDate);

            // Build query filter for deletion
            var queryFilter = new DocumentQueryFilter
            {
                AgentId = request.AgentName,
                Type = request.DataType,
                ActivationName = request.ActivationName,
                CreatedAfter = request.StartDate,
                CreatedBefore = request.EndDate
            };

            // Delete documents matching the filter
            var deletedCount = await _documentRepository.DeleteByFilterAsync(request.TenantId, queryFilter);

            var response = new AdminDataDeleteResponse
            {
                DeletedCount = deletedCount,
                Period = new AdminDataPeriod
                {
                    StartDate = request.StartDate,
                    EndDate = request.EndDate
                },
                Filters = new AdminDataFilters
                {
                    AgentName = request.AgentName,
                    ActivationName = request.ActivationName
                },
                DataType = request.DataType
            };

            _logger.LogInformation(
                "Data deletion completed successfully - AgentName: {AgentName}, DataType: {DataType}, DeletedCount: {DeletedCount}",
                request.AgentName, request.DataType, deletedCount);

            return ServiceResult<AdminDataDeleteResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete data. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminDataDeleteResponse>.InternalServerError("Failed to delete data");
        }
    }

    public async Task<ServiceResult<AdminDataDeleteRecordResponse>> DeleteRecordAsync(
        AdminDataDeleteRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validationResult = ValidateDeleteRecordRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Deleting record - TenantId: {TenantId}, RecordId: {RecordId}",
                request.TenantId, request.RecordId);

            // First, get the record to return it in the response (for confirmation)
            var existingRecord = await _documentRepository.GetByIdAsync(request.RecordId);
            
            // Check if record exists and belongs to the tenant
            if (existingRecord == null || existingRecord.TenantId != request.TenantId)
            {
                if (existingRecord == null)
                {
                    _logger.LogWarning("Record not found - RecordId: {RecordId}, TenantId: {TenantId}", 
                        request.RecordId, request.TenantId);
                }
                else
                {
                    _logger.LogWarning("Access denied - Record belongs to different tenant. RecordId: {RecordId}, RequestedTenant: {TenantId}, ActualTenant: {ActualTenantId}", 
                        request.RecordId, request.TenantId, existingRecord.TenantId);
                }

                // Return a not found response (the endpoint will handle the structured response)
                return ServiceResult<AdminDataDeleteRecordResponse>.NotFound("Record not found");
            }

            // Delete the record
            var deleted = await _documentRepository.DeleteAsync(request.RecordId, request.TenantId);

            var response = new AdminDataDeleteRecordResponse
            {
                Deleted = deleted,
                RecordId = request.RecordId,
                DeletedRecord = deleted ? ConvertToDataItemResponse(existingRecord) : null
            };

            if (deleted)
            {
                _logger.LogInformation(
                    "Record deleted successfully - RecordId: {RecordId}, TenantId: {TenantId}, AgentName: {AgentName}, DataType: {DataType}",
                    request.RecordId, request.TenantId, existingRecord.AgentId, existingRecord.Type);
            }
            else
            {
                _logger.LogWarning(
                    "Record deletion failed - RecordId: {RecordId}, TenantId: {TenantId}",
                    request.RecordId, request.TenantId);
            }

            return ServiceResult<AdminDataDeleteRecordResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete record. RecordId: {RecordId}, Error: {ErrorMessage}", 
                request.RecordId, ex.Message);
            return ServiceResult<AdminDataDeleteRecordResponse>.InternalServerError("Failed to delete record");
        }
    }

    /// <summary>
    /// Validates the schema request parameters.
    /// </summary>
    private ServiceResult<AdminDataSchemaResponse> ValidateSchemaRequest(AdminDataSchemaRequest request)
    {
        if (string.IsNullOrEmpty(request.TenantId))
        {
            return ServiceResult<AdminDataSchemaResponse>.BadRequest("TenantId is required");
        }

        if (request.TenantId == "undefined" || request.TenantId == "null")
        {
            return ServiceResult<AdminDataSchemaResponse>.BadRequest("Invalid TenantId provided");
        }

        if (string.IsNullOrEmpty(request.AgentName))
        {
            return ServiceResult<AdminDataSchemaResponse>.BadRequest("AgentName is required");
        }

        if (request.StartDate >= request.EndDate)
        {
            return ServiceResult<AdminDataSchemaResponse>.BadRequest("StartDate must be before EndDate");
        }

        var dateRange = request.EndDate - request.StartDate;
        if (dateRange.TotalDays > MaxDateRangeDays)
        {
            return ServiceResult<AdminDataSchemaResponse>.BadRequest($"Date range cannot exceed {MaxDateRangeDays} days");
        }

        return ServiceResult<AdminDataSchemaResponse>.Success(new AdminDataSchemaResponse());
    }

    /// <summary>
    /// Validates the data request parameters.
    /// </summary>
    private ServiceResult<AdminDataListResponse> ValidateDataRequest(AdminDataListRequest request)
    {
        if (string.IsNullOrEmpty(request.TenantId))
        {
            return ServiceResult<AdminDataListResponse>.BadRequest("TenantId is required");
        }

        if (request.TenantId == "undefined" || request.TenantId == "null")
        {
            return ServiceResult<AdminDataListResponse>.BadRequest("Invalid TenantId provided");
        }

        if (string.IsNullOrEmpty(request.AgentName))
        {
            return ServiceResult<AdminDataListResponse>.BadRequest("AgentName is required");
        }

        if (string.IsNullOrEmpty(request.DataType))
        {
            return ServiceResult<AdminDataListResponse>.BadRequest("DataType is required");
        }

        if (request.StartDate >= request.EndDate)
        {
            return ServiceResult<AdminDataListResponse>.BadRequest("StartDate must be before EndDate");
        }

        var dateRange = request.EndDate - request.StartDate;
        if (dateRange.TotalDays > MaxDateRangeDays)
        {
            return ServiceResult<AdminDataListResponse>.BadRequest($"Date range cannot exceed {MaxDateRangeDays} days");
        }

        if (request.Skip < 0)
        {
            return ServiceResult<AdminDataListResponse>.BadRequest("Skip cannot be negative");
        }

        if (request.Limit <= 0 || request.Limit > MaxLimit)
        {
            return ServiceResult<AdminDataListResponse>.BadRequest($"Limit must be between 1 and {MaxLimit}");
        }

        return ServiceResult<AdminDataListResponse>.Success(new AdminDataListResponse());
    }

    /// <summary>
    /// Validates the delete request parameters.
    /// </summary>
    private ServiceResult<AdminDataDeleteResponse> ValidateDeleteRequest(AdminDataDeleteRequest request)
    {
        if (string.IsNullOrEmpty(request.TenantId))
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest("TenantId is required");
        }

        if (request.TenantId == "undefined" || request.TenantId == "null")
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest("Invalid TenantId provided");
        }

        if (string.IsNullOrEmpty(request.AgentName))
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest("AgentName is required");
        }

        if (string.IsNullOrEmpty(request.DataType))
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest("DataType is required");
        }

        if (request.StartDate >= request.EndDate)
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest("StartDate must be before EndDate");
        }

        var dateRange = request.EndDate - request.StartDate;
        if (dateRange.TotalDays > MaxDateRangeDays)
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest($"Date range cannot exceed {MaxDateRangeDays} days");
        }

        return ServiceResult<AdminDataDeleteResponse>.Success(new AdminDataDeleteResponse());
    }

    /// <summary>
    /// Validates the delete record request parameters.
    /// </summary>
    private ServiceResult<AdminDataDeleteRecordResponse> ValidateDeleteRecordRequest(AdminDataDeleteRecordRequest request)
    {
        if (string.IsNullOrEmpty(request.TenantId))
        {
            return ServiceResult<AdminDataDeleteRecordResponse>.BadRequest("TenantId is required");
        }

        if (request.TenantId == "undefined" || request.TenantId == "null")
        {
            return ServiceResult<AdminDataDeleteRecordResponse>.BadRequest("Invalid TenantId provided");
        }

        if (string.IsNullOrEmpty(request.RecordId))
        {
            return ServiceResult<AdminDataDeleteRecordResponse>.BadRequest("RecordId is required");
        }

        return ServiceResult<AdminDataDeleteRecordResponse>.Success(new AdminDataDeleteRecordResponse());
    }

    /// <summary>
    /// Converts a Document entity to AdminDataItemResponse.
    /// </summary>
    private AdminDataItemResponse ConvertToDataItemResponse(Shared.Data.Models.Document document)
    {
        JsonElement content = default;
        if (document.Content != null && !document.Content.IsBsonNull)
        {
            var contentJson = document.Content.ToJson();
            content = JsonSerializer.Deserialize<JsonElement>(contentJson);
        }

        Dictionary<string, object>? metadata = null;
        if (document.Metadata != null && !document.Metadata.IsBsonNull)
        {
            var metadataJson = document.Metadata.ToJson();
            metadata = JsonSerializer.Deserialize<Dictionary<string, object>>(metadataJson);
        }

        return new AdminDataItemResponse
        {
            Id = document.Id,
            Key = document.Key ?? string.Empty,
            ParticipantId = document.ParticipantId,
            Content = content,
            Metadata = metadata,
            CreatedAt = document.CreatedAt,
            UpdatedAt = document.UpdatedAt,
            ExpiresAt = document.ExpiresAt
        };
    }
}