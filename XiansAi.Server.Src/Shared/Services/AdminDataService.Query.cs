using Features.AgentApi.Repositories;
using Shared.Data.Models;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

public partial class AdminDataService
{
    public async Task<ServiceResult<AdminDataSchemaResponse>> GetDataSchemaAsync(
        AdminDataSchemaRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateSchemaRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Getting data schema - TenantId: {TenantId}, AgentName: {AgentName}, ActivationName: {ActivationName}, Range: {StartDate} to {EndDate}",
                LogSanitizer.Sanitize(request.TenantId),
                LogSanitizer.Sanitize(request.AgentName),
                LogSanitizer.Sanitize(request.ActivationName),
                request.StartDate,
                request.EndDate);

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
                LogSanitizer.Sanitize(request.AgentName), documentTypes.Count);

            return ServiceResult<AdminDataSchemaResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve data schema. Error: {ErrorMessage}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AdminDataSchemaResponse>.InternalServerError("Failed to retrieve data schema");
        }
    }

    public async Task<ServiceResult<AdminDataListResponse>> GetDataAsync(
        AdminDataListRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateDataRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Getting data - TenantId: {TenantId}, AgentName: {AgentName}, DataType: {DataType}, ActivationName: {ActivationName}, Range: {StartDate} to {EndDate}, Skip: {Skip}, Limit: {Limit}",
                LogSanitizer.Sanitize(request.TenantId),
                LogSanitizer.Sanitize(request.AgentName),
                LogSanitizer.Sanitize(request.DataType),
                LogSanitizer.Sanitize(request.ActivationName),
                request.StartDate,
                request.EndDate,
                request.Skip,
                request.Limit);

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
            var dataItems = documents.Select(AdminDataMapper.ToItemResponse).ToList();

            var response = new AdminDataListResponse
            {
                Data = dataItems,
                Total = (int)totalCount,
                Skip = request.Skip,
                Limit = request.Limit
            };

            _logger.LogInformation(
                "Data retrieved successfully - AgentName: {AgentName}, DataType: {DataType}, Total: {Total}, Returned: {Count}",
                LogSanitizer.Sanitize(request.AgentName), LogSanitizer.Sanitize(request.DataType), totalCount, dataItems.Count);

            return ServiceResult<AdminDataListResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve data. Error: {ErrorMessage}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AdminDataListResponse>.InternalServerError("Failed to retrieve data");
        }
    }

    public async Task<ServiceResult<AdminDataDeleteResponse>> DeleteDataAsync(
        AdminDataDeleteRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateDeleteRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Deleting data - TenantId: {TenantId}, AgentName: {AgentName}, DataType: {DataType}, ActivationName: {ActivationName}, Range: {StartDate} to {EndDate}",
                LogSanitizer.Sanitize(request.TenantId),
                LogSanitizer.Sanitize(request.AgentName),
                LogSanitizer.Sanitize(request.DataType),
                LogSanitizer.Sanitize(request.ActivationName),
                request.StartDate,
                request.EndDate);

            var queryFilter = new DocumentQueryFilter
            {
                // Internal callers can constrain deletion to a previously previewed snapshot.
                Ids = request.RecordIds,
                AgentId = request.AgentName,
                Type = request.DataType,
                ActivationName = request.ActivationName,
                CreatedAfter = request.StartDate,
                CreatedBefore = request.EndDate
            };

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
                LogSanitizer.Sanitize(request.AgentName), LogSanitizer.Sanitize(request.DataType), deletedCount);

            return ServiceResult<AdminDataDeleteResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete data. Error: {ErrorMessage}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AdminDataDeleteResponse>.InternalServerError("Failed to delete data");
        }
    }

    public async Task<ServiceResult<AdminDataDeleteRecordResponse>> DeleteRecordAsync(
        AdminDataDeleteRecordRequest request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = ValidateDeleteRecordRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Deleting record - TenantId: {TenantId}, RecordId: {RecordId}",
                LogSanitizer.Sanitize(request.TenantId), LogSanitizer.Sanitize(request.RecordId));

            DocumentQueryFilter? scopedFilter = null;
            Document? existingRecord;
            if (request.AgentName is not null)
            {
                scopedFilter = new DocumentQueryFilter
                {
                    Ids = [request.RecordId],
                    AgentId = request.AgentName,
                    ActivationName = request.ActivationName,
                    Limit = 1
                };
                existingRecord = (await _documentRepository.QueryAsync(request.TenantId, scopedFilter)).FirstOrDefault();
            }
            else
            {
                existingRecord = await _documentRepository.GetByIdAsync(request.RecordId);
            }

            if (existingRecord == null || existingRecord.TenantId != request.TenantId)
            {
                if (existingRecord == null)
                {
                    _logger.LogWarning("Record not found - RecordId: {RecordId}, TenantId: {TenantId}",
                        LogSanitizer.Sanitize(request.RecordId), LogSanitizer.Sanitize(request.TenantId));
                }
                else
                {
                    _logger.LogWarning("Access denied - Record belongs to different tenant. RecordId: {RecordId}, RequestedTenant: {TenantId}, ActualTenant: {ActualTenantId}",
                        LogSanitizer.Sanitize(request.RecordId), LogSanitizer.Sanitize(request.TenantId), LogSanitizer.Sanitize(existingRecord.TenantId));
                }

                return ServiceResult<AdminDataDeleteRecordResponse>.NotFound("Record not found");
            }

            var deleted = scopedFilter is not null
                ? await _documentRepository.DeleteByFilterAsync(request.TenantId, scopedFilter) == 1
                : await _documentRepository.DeleteAsync(request.RecordId, request.TenantId);

            var response = new AdminDataDeleteRecordResponse
            {
                Deleted = deleted,
                RecordId = request.RecordId,
                DeletedRecord = deleted ? AdminDataMapper.ToItemResponse(existingRecord) : null
            };

            if (deleted)
            {
                _logger.LogInformation(
                    "Record deleted successfully - RecordId: {RecordId}, TenantId: {TenantId}, AgentName: {AgentName}, DataType: {DataType}",
                    LogSanitizer.Sanitize(request.RecordId), LogSanitizer.Sanitize(request.TenantId), LogSanitizer.Sanitize(existingRecord.AgentId), LogSanitizer.Sanitize(existingRecord.Type));
            }
            else
            {
                _logger.LogWarning(
                    "Record deletion failed - RecordId: {RecordId}, TenantId: {TenantId}",
                    LogSanitizer.Sanitize(request.RecordId), LogSanitizer.Sanitize(request.TenantId));
            }

            return ServiceResult<AdminDataDeleteRecordResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete record. RecordId: {RecordId}, Error: {ErrorMessage}",
                LogSanitizer.Sanitize(request.RecordId), LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AdminDataDeleteRecordResponse>.InternalServerError("Failed to delete record");
        }
    }

    public async Task<ServiceResult<int>> DeleteDocumentsByActivationAsync(string tenantId, string agentName, string activationName)
    {
        if (string.IsNullOrWhiteSpace(tenantId) || string.IsNullOrWhiteSpace(agentName) || string.IsNullOrWhiteSpace(activationName))
        {
            return ServiceResult<int>.BadRequest("TenantId, AgentName, and ActivationName are required");
        }

        try
        {
            var deletedCount = await _documentRepository.DeleteByFilterAsync(tenantId, new DocumentQueryFilter
            {
                AgentId = agentName,
                ActivationName = activationName
            });

            _logger.LogInformation(
                "Deleted {DeletedCount} document(s) for agent {AgentName}, activation {ActivationName}",
                deletedCount, LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(activationName));

            return ServiceResult<int>.Success(deletedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting documents for tenant {TenantId}, agent {AgentName}, activation {ActivationName}",
                LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(activationName));
            return ServiceResult<int>.InternalServerError("An error occurred while deleting documents");
        }
    }

    private ServiceResult<AdminDataSchemaResponse> ValidateSchemaRequest(AdminDataSchemaRequest request)
    {
        var tenantError = ValidateTenantId(request.TenantId);
        if (tenantError != null)
        {
            return ServiceResult<AdminDataSchemaResponse>.BadRequest(tenantError);
        }

        if (string.IsNullOrWhiteSpace(request.AgentName))
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

    private ServiceResult<AdminDataListResponse> ValidateDataRequest(AdminDataListRequest request)
    {
        var tenantError = ValidateTenantId(request.TenantId);
        if (tenantError != null)
        {
            return ServiceResult<AdminDataListResponse>.BadRequest(tenantError);
        }

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return ServiceResult<AdminDataListResponse>.BadRequest("AgentName is required");
        }

        if (string.IsNullOrWhiteSpace(request.DataType))
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

    private ServiceResult<AdminDataDeleteResponse> ValidateDeleteRequest(AdminDataDeleteRequest request)
    {
        var tenantError = ValidateTenantId(request.TenantId);
        if (tenantError != null)
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest(tenantError);
        }

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            return ServiceResult<AdminDataDeleteResponse>.BadRequest("AgentName is required");
        }

        if (string.IsNullOrWhiteSpace(request.DataType))
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

    private ServiceResult<AdminDataDeleteRecordResponse> ValidateDeleteRecordRequest(AdminDataDeleteRecordRequest request)
    {
        if (request.AgentName is not null &&
            (string.IsNullOrWhiteSpace(request.AgentName) || string.IsNullOrWhiteSpace(request.ActivationName)))
        {
            return ServiceResult<AdminDataDeleteRecordResponse>.BadRequest(
                "Scoped deletion requires an agent and activation.");
        }

        var tenantError = ValidateTenantId(request.TenantId);
        if (tenantError != null)
        {
            return ServiceResult<AdminDataDeleteRecordResponse>.BadRequest(tenantError);
        }

        if (string.IsNullOrWhiteSpace(request.RecordId))
        {
            return ServiceResult<AdminDataDeleteRecordResponse>.BadRequest("RecordId is required");
        }

        return ServiceResult<AdminDataDeleteRecordResponse>.Success(new AdminDataDeleteRecordResponse());
    }
}
