using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Response model for a paginated page of audit log entries.
/// </summary>
public class AdminAuditLogListResponse
{
    public required IEnumerable<AuditLogEntry> Entries { get; set; }
    public required long TotalCount { get; set; }
    public required int Page { get; set; }
    public required int PageSize { get; set; }
    public required int TotalPages { get; set; }
}

public interface IAdminAuditLogService
{
    Task<ServiceResult<AdminAuditLogListResponse>> GetEntriesAsync(
        string tenantId,
        string? performedBy,
        string? activationName,
        bool onlyWithoutActivation,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize);

    /// <summary>Distinct "performed by" values recorded for the tenant, for populating a filter dropdown.</summary>
    Task<ServiceResult<IEnumerable<string>>> GetPerformedByOptionsAsync(string tenantId);

    /// <summary>Distinct activation names recorded for the tenant, for populating a filter dropdown.</summary>
    Task<ServiceResult<IEnumerable<string>>> GetActivationNameOptionsAsync(string tenantId);
}

/// <summary>
/// AdminApi-facing read access to the audit log (who did what, and when), scoped to a
/// tenant resolved authoritatively from the route rather than the caller's own token.
/// </summary>
public class AdminAuditLogService : IAdminAuditLogService
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ILogger<AdminAuditLogService> _logger;

    public AdminAuditLogService(
        IAuditLogRepository auditLogRepository,
        ILogger<AdminAuditLogService> logger)
    {
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AdminAuditLogListResponse>> GetEntriesAsync(
        string tenantId,
        string? performedBy,
        string? activationName,
        bool onlyWithoutActivation,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<AdminAuditLogListResponse>.BadRequest("Tenant ID is required");
            }

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var (entries, totalCount) = await _auditLogRepository.GetFilteredAsync(
                tenantId, performedBy, activationName, onlyWithoutActivation, startDate, endDate, page, pageSize);

            var response = new AdminAuditLogListResponse
            {
                Entries = entries,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            return ServiceResult<AdminAuditLogListResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit log entries for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<AdminAuditLogListResponse>.InternalServerError("An error occurred while retrieving audit log entries");
        }
    }

    public async Task<ServiceResult<IEnumerable<string>>> GetPerformedByOptionsAsync(string tenantId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<IEnumerable<string>>.BadRequest("Tenant ID is required");
            }

            var values = await _auditLogRepository.GetDistinctPerformedByAsync(tenantId);
            return ServiceResult<IEnumerable<string>>.Success(values);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving performed-by options for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<IEnumerable<string>>.InternalServerError("An error occurred while retrieving performed-by options");
        }
    }

    public async Task<ServiceResult<IEnumerable<string>>> GetActivationNameOptionsAsync(string tenantId)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<IEnumerable<string>>.BadRequest("Tenant ID is required");
            }

            var values = await _auditLogRepository.GetDistinctActivationNamesAsync(tenantId);
            return ServiceResult<IEnumerable<string>>.Success(values);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving activation name options for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<IEnumerable<string>>.InternalServerError("An error occurred while retrieving activation name options");
        }
    }
}
