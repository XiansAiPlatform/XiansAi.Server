using Features.WebApi.Models;
using Features.WebApi.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Response model for a paginated page of audit activities.
/// </summary>
public class AdminAuditActivityListResponse
{
    public required IEnumerable<AuditActivity> Activities { get; set; }
    public required long TotalCount { get; set; }
    public required int Page { get; set; }
    public required int PageSize { get; set; }
    public required int TotalPages { get; set; }
}

public interface IAdminAuditActivityService
{
    Task<ServiceResult<AdminAuditActivityListResponse>> GetActivitiesAsync(
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
/// AdminApi-facing read access to the audit trail (who did what, and when), scoped to a
/// tenant resolved authoritatively from the route rather than the caller's own token.
/// </summary>
public class AdminAuditActivityService : IAdminAuditActivityService
{
    private readonly IAuditActivityRepository _auditActivityRepository;
    private readonly ILogger<AdminAuditActivityService> _logger;

    public AdminAuditActivityService(
        IAuditActivityRepository auditActivityRepository,
        ILogger<AdminAuditActivityService> logger)
    {
        _auditActivityRepository = auditActivityRepository ?? throw new ArgumentNullException(nameof(auditActivityRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AdminAuditActivityListResponse>> GetActivitiesAsync(
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
                return ServiceResult<AdminAuditActivityListResponse>.BadRequest("Tenant ID is required");
            }

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var (activities, totalCount) = await _auditActivityRepository.GetFilteredAsync(
                tenantId, performedBy, activationName, onlyWithoutActivation, startDate, endDate, page, pageSize);

            var response = new AdminAuditActivityListResponse
            {
                Activities = activities,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            };

            return ServiceResult<AdminAuditActivityListResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit activities for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<AdminAuditActivityListResponse>.InternalServerError("An error occurred while retrieving audit activities");
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

            var values = await _auditActivityRepository.GetDistinctPerformedByAsync(tenantId);
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

            var values = await _auditActivityRepository.GetDistinctActivationNamesAsync(tenantId);
            return ServiceResult<IEnumerable<string>>.Success(values);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving activation name options for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<IEnumerable<string>>.InternalServerError("An error occurred while retrieving activation name options");
        }
    }
}
