using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Data.Models.Validation;
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

/// <summary>
/// Body Studio (and other admin clients) POST to persist an audit row.
/// </summary>
public class AdminAuditLogCreateRequest
{
    public string Action { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? ActivationName { get; set; }
    public Dictionary<string, object?>? Details { get; set; }
}

public interface IAdminAuditLogService
{
    Task<ServiceResult<AuditLogEntry>> CreateEntryAsync(string tenantId, AdminAuditLogCreateRequest request);

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
/// AdminApi-facing read/write access to the audit log (who did what, and when), scoped to a
/// tenant resolved authoritatively from the route rather than the caller's own token.
/// Pass <see cref="Shared.Auditing.AuditLogTenants.Platform"/> to read platform-scoped events
/// that must never appear in a customer tenant's audit view.
/// </summary>
public class AdminAuditLogService : IAdminAuditLogService
{
    internal static readonly TimeSpan ViewAsIdempotencyWindow = TimeSpan.FromHours(1);
    private const string TargetParticipantIdKey = "targetParticipantId";

    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AdminAuditLogService> _logger;

    public AdminAuditLogService(
        IAuditLogRepository auditLogRepository,
        ITenantContext tenantContext,
        ILogger<AdminAuditLogService> logger)
    {
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AuditLogEntry>> CreateEntryAsync(string tenantId, AdminAuditLogCreateRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tenantId))
                return ServiceResult<AuditLogEntry>.BadRequest("Tenant ID is required");

            if (request == null || string.IsNullOrWhiteSpace(request.Action))
                return ServiceResult<AuditLogEntry>.BadRequest("Action is required");

            var actor = _tenantContext.ParticipantId;
            var loggedInUser = _tenantContext.LoggedInUser;
            if (string.IsNullOrWhiteSpace(actor))
                return ServiceResult<AuditLogEntry>.BadRequest("Performed-by identity is required");
            if (string.IsNullOrWhiteSpace(loggedInUser))
                return ServiceResult<AuditLogEntry>.BadRequest("Logged-in user is required");

            var details = NormalizeDetails(request.Details);
            var targetParticipantId = ReadRequiredDetail(details, TargetParticipantIdKey);
            if (string.Equals(request.Action.Trim(), DomainEventTypes.ConversationViewAs, StringComparison.Ordinal)
                && string.IsNullOrWhiteSpace(targetParticipantId))
            {
                return ServiceResult<AuditLogEntry>.BadRequest("details.targetParticipantId is required");
            }

            var entry = BuildEntry(tenantId, actor, loggedInUser, request, details);
            var sanitized = entry.SanitizeAndValidate();
            var sanitizedTarget = ReadRequiredDetail(sanitized.Details ?? [], TargetParticipantIdKey);

            if (!string.IsNullOrWhiteSpace(sanitizedTarget))
            {
                var replayed = await TryReplayRecentAsync(sanitized, sanitizedTarget);
                if (replayed != null)
                    return ServiceResult<AuditLogEntry>.Success(replayed);
            }

            await _auditLogRepository.CreateAsync(sanitized);
            return ServiceResult<AuditLogEntry>.Success(sanitized, StatusCode.Created);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while creating audit log entry: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AuditLogEntry>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (MongoException ex)
        {
            _logger.LogError(ex, "Error creating audit log entry for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<AuditLogEntry>.InternalServerError("An error occurred while recording the audit log entry");
        }
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

    private async Task<AuditLogEntry?> TryReplayRecentAsync(AuditLogEntry sanitized, string targetParticipantId)
    {
        var cutoff = DateTime.UtcNow.Subtract(ViewAsIdempotencyWindow);
        var existing = await _auditLogRepository.FindRecentMatchingAsync(
            sanitized.TenantId,
            sanitized.Action,
            sanitized.ParticipantId,
            targetParticipantId,
            cutoff);

        if (existing == null)
            return null;

        existing.CreatedAt = DateTime.UtcNow;
        existing.Description = sanitized.Description;
        existing.ActivationName = sanitized.ActivationName;
        existing.Details = sanitized.Details;
        existing.LoggedInUser = sanitized.LoggedInUser;
        await _auditLogRepository.ReplaceAsync(existing);
        return existing;
    }

    private static AuditLogEntry BuildEntry(
        string tenantId,
        string actor,
        string loggedInUser,
        AdminAuditLogCreateRequest request,
        Dictionary<string, object?> details)
    {
        var activationName = string.IsNullOrWhiteSpace(request.ActivationName)
            ? null
            : request.ActivationName.Trim();

        return new AuditLogEntry
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = tenantId,
            ParticipantId = actor,
            LoggedInUser = loggedInUser,
            Action = request.Action.Trim(),
            Description = request.Description,
            ActivationName = activationName,
            Details = details,
            CreatedAt = DateTime.UtcNow
        };
    }

    private static string? ReadRequiredDetail(Dictionary<string, object?> details, string key)
    {
        if (!details.TryGetValue(key, out var value) || value == null)
            return null;

        var text = value switch
        {
            string s => s,
            JsonElement element when element.ValueKind == JsonValueKind.String => element.GetString(),
            _ => value.ToString()
        };

        return string.IsNullOrWhiteSpace(text) ? null : ValidationHelpers.SanitizeString(text);
    }

    private static Dictionary<string, object?> NormalizeDetails(Dictionary<string, object?>? details)
    {
        var normalized = new Dictionary<string, object?>();
        if (details == null)
            return normalized;

        foreach (var (key, value) in details)
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;

            normalized[key] = NormalizeDetailValue(value);
        }

        return normalized;
    }

    private static object? NormalizeDetailValue(object? value)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind switch
        {
            JsonValueKind.Null or JsonValueKind.Undefined => null,
            JsonValueKind.String => element.GetString(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when element.TryGetInt64(out var integer) => integer,
            JsonValueKind.Number => element.GetDouble(),
            _ => element.ToString()
        };
    }
}
