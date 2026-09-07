using Features.WebApi.Models;
using Features.WebApi.Repositories;
using Shared.Auth;
using Shared.Utils;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;

namespace Features.WebApi.Services;

public interface IAuditActivityService
{
    Task<ServiceResult<AuditActivity>> RecordActivityAsync(
        string action,
        string? description,
        string? activationName = null,
        Dictionary<string, object?>? details = null);

    Task<ServiceResult<(IEnumerable<AuditActivity> activities, long totalCount)>> GetActivitiesAsync(
        string? performedBy = null,
        string? activationName = null,
        bool onlyWithoutActivation = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20);
}

/// <summary>
/// Records and queries the audit trail of user/system actions (who did what, and when).
/// </summary>
public class AuditActivityService : IAuditActivityService
{
    private readonly IAuditActivityRepository _auditActivityRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuditActivityService> _logger;

    public AuditActivityService(
        IAuditActivityRepository auditActivityRepository,
        ITenantContext tenantContext,
        ILogger<AuditActivityService> logger)
    {
        _auditActivityRepository = auditActivityRepository ?? throw new ArgumentNullException(nameof(auditActivityRepository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AuditActivity>> RecordActivityAsync(
        string action,
        string? description,
        string? activationName = null,
        Dictionary<string, object?>? details = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return ServiceResult<AuditActivity>.BadRequest("Action is required");
            }

            var activity = new AuditActivity
            {
                TenantId = _tenantContext.TenantId,
                PerformedBy = _tenantContext.LoggedInUser,
                Action = action,
                Description = description,
                ActivationName = activationName,
                Details = details ?? []
            };

            var sanitized = activity.SanitizeAndValidate();

            await _auditActivityRepository.CreateAsync(sanitized);

            return ServiceResult<AuditActivity>.Success(sanitized, StatusCode.Created);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while recording audit activity: {Message}", ex.Message);
            return ServiceResult<AuditActivity>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording audit activity for action {Action}", LogSanitizer.Sanitize(action));
            return ServiceResult<AuditActivity>.InternalServerError("An error occurred while recording the audit activity");
        }
    }

    public async Task<ServiceResult<(IEnumerable<AuditActivity> activities, long totalCount)>> GetActivitiesAsync(
        string? performedBy = null,
        string? activationName = null,
        bool onlyWithoutActivation = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20)
    {
        try
        {
            (page, pageSize) = NormalizePaging(page, pageSize);

            var result = await _auditActivityRepository.GetFilteredAsync(
                _tenantContext.TenantId, performedBy, activationName, onlyWithoutActivation, startTime, endTime, page, pageSize);

            return ServiceResult<(IEnumerable<AuditActivity> activities, long totalCount)>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit activities");
            return ServiceResult<(IEnumerable<AuditActivity> activities, long totalCount)>.InternalServerError("An error occurred while retrieving audit activities");
        }
    }

    private static (int page, int pageSize) NormalizePaging(int page, int pageSize)
    {
        if (page < 1)
        {
            page = 1;
        }

        if (pageSize < 1 || pageSize > 100)
        {
            pageSize = 20;
        }

        return (page, pageSize);
    }
}
