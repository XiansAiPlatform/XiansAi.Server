using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;
using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.RegularExpressions;

namespace Shared.Services;

public interface IAuditLogService
{
    Task<ServiceResult<AuditLogEntry>> RecordEntryAsync(
        string action,
        string? description,
        string? activationName = null,
        object? details = null);

    Task<ServiceResult<(IEnumerable<AuditLogEntry> entries, long totalCount)>> GetEntriesAsync(
        string? performedBy = null,
        string? activationName = null,
        bool onlyWithoutActivation = false,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1,
        int pageSize = 20);
}

/// <summary>
/// Records and queries the audit log of user/system actions (who did what, and when).
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IAuditLogRepository auditLogRepository,
        ITenantContext tenantContext,
        ILogger<AuditLogService> logger)
    {
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AuditLogEntry>> RecordEntryAsync(
        string action,
        string? description,
        string? activationName = null,
        object? details = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return ServiceResult<AuditLogEntry>.BadRequest("Action is required");
            }

            var entry = new AuditLogEntry
            {
                TenantId = _tenantContext.TenantId,
                ParticipantId = _tenantContext.ParticipantId,
                LoggedInUser = _tenantContext.LoggedInUser,
                Action = action,
                Description = description,
                ActivationName = activationName,
                Details = ToDictionary(details) ?? []
            };

            var sanitized = entry.SanitizeAndValidate();

            await _auditLogRepository.CreateAsync(sanitized);

            return ServiceResult<AuditLogEntry>.Success(sanitized, StatusCode.Created);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while recording audit log entry: {Message}", ex.Message);
            return ServiceResult<AuditLogEntry>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording audit log entry for action {Action}", LogSanitizer.Sanitize(action));
            return ServiceResult<AuditLogEntry>.InternalServerError("An error occurred while recording the audit log entry");
        }
    }

    public async Task<ServiceResult<(IEnumerable<AuditLogEntry> entries, long totalCount)>> GetEntriesAsync(
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

            var result = await _auditLogRepository.GetFilteredAsync(
                _tenantContext.TenantId, performedBy, activationName, onlyWithoutActivation, startTime, endTime, page, pageSize);

            return ServiceResult<(IEnumerable<AuditLogEntry> entries, long totalCount)>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving audit log entries");
            return ServiceResult<(IEnumerable<AuditLogEntry> entries, long totalCount)>.InternalServerError("An error occurred while retrieving audit log entries");
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

    public static Dictionary<string, object?> ToDictionary(object? obj)
    {
        var dictionary = new Dictionary<string, object?>();
        if (obj == null) return dictionary;

        if (obj is Dictionary<string, object?> existing)
        {
            return existing;
        }

        // Reflect over the object's actual runtime type, not the static "object?" parameter
        // type here - a generic ToDictionary<T>(T obj) would infer T from the compile-time
        // type of the caller's argument (often just "object"), silently reflecting nothing.
        foreach (PropertyInfo property in obj.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead)
            {
                dictionary[Humanize(property.Name)] = property.GetValue(obj, null);
            }
        }

        return dictionary;
    }

    /// <summary>
    /// Converts a PascalCase/camelCase identifier (e.g. "KnowledgeId") into space-separated,
    /// capitalized words (e.g. "Knowledge Id") for display in the Audit Log UI.
    /// </summary>
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        var spaced = Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");
        return char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
