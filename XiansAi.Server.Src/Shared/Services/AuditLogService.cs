using Shared.Auditing;
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
    /// <summary>
    /// Snapshots the current request's identity and endpoint metadata, then writes the audit row
    /// in the background. The returned task completes as soon as the document is built and
    /// validated; a slow or failed Mongo insert cannot delay or fail the caller.
    /// </summary>
    Task<ServiceResult<AuditLogEntry>> RecordEntryAsync(
        string action,
        string? description = null,
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
/// Endpoint name/summary are read from the current request via <see cref="IHttpContextAccessor"/>
/// so domain services do not take <c>HttpContext</c>. Caller identity comes from
/// <see cref="ITenantContext"/>. Non-HTTP callers fall back to the action they pass in.
/// Recording never blocks on the database: the document is built on the caller's thread and
/// persisted in the background (best-effort, same contract as webhook publishing).
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly IAuditLogRepository _auditLogRepository;
    private readonly ITenantContext _tenantContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        IAuditLogRepository auditLogRepository,
        ITenantContext tenantContext,
        IHttpContextAccessor httpContextAccessor,
        ILogger<AuditLogService> logger)
    {
        _auditLogRepository = auditLogRepository ?? throw new ArgumentNullException(nameof(auditLogRepository));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<ServiceResult<AuditLogEntry>> RecordEntryAsync(
        string action,
        string? description = null,
        string? activationName = null,
        object? details = null)
    {
        try
        {
            // Snapshot identity and endpoint metadata on the caller's thread: HttpContext and
            // ITenantContext are only valid here. Building the document is CPU-only work; the
            // Mongo write is then fired in the background so a slow or failed insert cannot
            // delay or break the originating business operation (same contract as webhooks).
            var httpContext = _httpContextAccessor.HttpContext;
            var resolvedAction = httpContext?.GetEndpointName() ?? action;
            var resolvedDescription = description ?? httpContext?.GetEndpointSummary() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(resolvedAction))
            {
                return Task.FromResult(ServiceResult<AuditLogEntry>.BadRequest("Action is required"));
            }

            var entry = new AuditLogEntry
            {
                TenantId = _tenantContext.TenantId,
                ParticipantId = _tenantContext.ParticipantId,
                LoggedInUser = _tenantContext.LoggedInUser,
                Action = resolvedAction,
                Description = resolvedDescription,
                ActivationName = activationName,
                Details = ToDictionary(details) ?? []
            };

            var sanitized = entry.SanitizeAndValidate();

            _ = PersistEntryAsync(sanitized, action);

            return Task.FromResult(ServiceResult<AuditLogEntry>.Success(sanitized, StatusCode.Created));
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while recording audit log entry: {Message}", ex.Message);
            return Task.FromResult(ServiceResult<AuditLogEntry>.BadRequest($"Validation failed: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording audit log entry for action {Action}", LogSanitizer.Sanitize(action));
            return Task.FromResult(ServiceResult<AuditLogEntry>.InternalServerError("An error occurred while recording the audit log entry"));
        }
    }

    /// <summary>
    /// Writes the audit row off the request path. Safe to use the injected repository here because
    /// it holds no per-request state (only a thread-safe, long-lived Mongo collection).
    /// </summary>
    private async Task PersistEntryAsync(AuditLogEntry entry, string action)
    {
        try
        {
            await _auditLogRepository.CreateAsync(entry);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recording audit log entry for action {Action}", LogSanitizer.Sanitize(action));
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
        var first = char.ToUpperInvariant(spaced[0]);
        return spaced.Length == 1 ? first.ToString() : first + spaced[1..];
    }
}
