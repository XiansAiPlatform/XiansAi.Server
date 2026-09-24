using Features.AdminApi.Auth;
using Features.AdminApi.Models;
using Features.AdminApi.Repositories;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Features.AdminApi.Services;

/// <summary>
/// One action's resolved rule, as the matrix CRUD's effective view reports it.
///
/// <c>AllowedRoles</c> is the stored rule, or the code default when nothing is stored, and
/// <c>Source</c> says which. <c>EffectiveRoles</c> is that plus SysAdmin, reported separately because
/// SysAdmin is never stored as a role and a view showing only the stored rule reads as though it
/// were excluded.
/// </summary>
public sealed record CapabilityResolution(
    string Action,
    IReadOnlyList<string> AllowedRoles,
    IReadOnlyList<string> EffectiveRoles,
    string Source,
    bool NonDelegable,
    string Description,
    DateTime? UpdatedAt,
    string? UpdatedBy);

public interface ICapabilityMatrixService
{
    /// <summary>
    /// Roles permitted to perform <paramref name="action"/>, excluding SysAdmin. Returns empty for an
    /// action nothing declares, so an unknown action is SysAdmin-only rather than open.
    ///
    /// Returns a bare list rather than a <see cref="ServiceResult{T}"/> on purpose: this sits on the
    /// authorization path, where "the lookup failed" must resolve to the code default rather than
    /// become an error the caller has to interpret.
    /// </summary>
    Task<IReadOnlyList<string>> GetAllowedRolesAsync(string action);

    /// <summary>Every declared action merged with its resolved row, for the matrix CRUD's list view.</summary>
    Task<IReadOnlyList<CapabilityResolution>> GetEffectiveAsync();

    Task<ServiceResult<bool>> UpsertAsync(string action, List<string> allowedRoles, string? description, string actorUserId);

    /// <summary>Removes the row for <paramref name="action"/>, reverting it to the code default.</summary>
    Task<ServiceResult<bool>> DeleteAsync(string action, string actorUserId);
}

/// <summary>
/// Resolves the capability matrix: a stored row when one exists, otherwise the action's code default
/// from <see cref="CapabilityActions"/>.
///
/// A row <b>replaces</b> the default rather than adding to it, so a rule can be tightened below the
/// default as well as widened — union would make tightening impossible without a redeploy. Absence of
/// a row always means "use the default", never "deny", so no data state, including no data at all,
/// can lock anyone out.
/// </summary>
public class CapabilityMatrixService : ICapabilityMatrixService
{
    private const string CacheKey = "admin_capability_matrix:all";

    /// <summary>
    /// Short because <see cref="ObjectCache"/> falls back to a per-node in-memory provider whenever
    /// <c>Cache:Provider</c> is unset (<c>CacheProviderFactory.RegisterProvider</c>), which makes the
    /// explicit invalidation below clear only the node that served the write. On a multi-instance
    /// deployment without Redis this TTL is therefore the only bound on how long a tightening takes
    /// to reach the other nodes: within 30s fleet-wide, immediately on the writing node and on all
    /// nodes when Redis is configured.
    /// </summary>
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(30);

    private readonly ICapabilityMatrixRepository _repository;
    private readonly ObjectCache _cache;
    private readonly IWebhookEventPublisher _webhookEventPublisher;
    private readonly ILogger<CapabilityMatrixService> _logger;

    public CapabilityMatrixService(
        ICapabilityMatrixRepository repository,
        ObjectCache cache,
        IWebhookEventPublisher webhookEventPublisher,
        ILogger<CapabilityMatrixService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _webhookEventPublisher = webhookEventPublisher ?? throw new ArgumentNullException(nameof(webhookEventPublisher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> GetAllowedRolesAsync(string action)
    {
        var declared = CapabilityActions.Find(action);
        if (declared == null)
        {
            // Undeclared: no row is consulted.
            return [];
        }

        // Non-delegable: no stored row is ever consulted, so this always falls through to the fixed
        // DefaultRoles below — usually empty (SysAdmin-only), but not required to be.
        var row = declared.NonDelegable ? null : FindRow(await GetSnapshotAsync(), action);
        return row?.AllowedRoles ?? declared.DefaultRoles;
    }

    public async Task<IReadOnlyList<CapabilityResolution>> GetEffectiveAsync()
    {
        var rows = await GetSnapshotAsync();

        return CapabilityActions.All.Select(declared =>
        {
            // A row stored against a non-delegable action is inert, so it is reported as "default"
            // rather than as an override that is doing something.
            var row = declared.NonDelegable ? null : FindRow(rows, declared.Name);
            var allowed = row?.AllowedRoles ?? declared.DefaultRoles;

            return new CapabilityResolution(
                Action: declared.Name,
                AllowedRoles: allowed,
                EffectiveRoles: [.. allowed, SystemRoles.SysAdmin],
                Source: row == null ? "default" : "override",
                NonDelegable: declared.NonDelegable,
                Description: row?.Description ?? declared.Description,
                UpdatedAt: row?.UpdatedAt,
                UpdatedBy: row?.UpdatedBy);
        }).ToList();
    }

    public async Task<ServiceResult<bool>> UpsertAsync(
        string action, List<string> allowedRoles, string? description, string actorUserId)
    {
        var declared = CapabilityActions.Find(action);
        if (declared == null)
            return ServiceResult<bool>.BadRequest(
                $"Unknown action '{action}'. The matrix accepts new roles without a redeploy, but not actions nothing enforces; see GET /admin-console/capability-matrix/catalog.");

        if (declared.NonDelegable)
            return ServiceResult<bool>.BadRequest(
                $"Action '{action}' is restricted to system administrators in code. Storing a rule for it would have no effect.");

        allowedRoles ??= [];
        WarnAboutInertRoles(action, allowedRoles);

        try
        {
            var previous = await _repository.UpsertAsync(new CapabilityMatrixEntry
            {
                Action = action,
                AllowedRoles = allowedRoles,
                Description = description,
                UpdatedAt = DateTime.UtcNow,
                UpdatedBy = actorUserId,
            });
            await _cache.RemoveAsync(CacheKey);

            await RecordChangeAsync(
                DomainEventTypes.CapabilityMatrixUpdated,
                action,
                previous?.AllowedRoles ?? declared.DefaultRoles,
                allowedRoles,
                actorUserId,
                new { created = previous == null });

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting capability matrix row for {Action}", LogSanitizer.Sanitize(action));
            return ServiceResult<bool>.InternalServerError("Failed to save the capability matrix entry");
        }
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string action, string actorUserId)
    {
        var declared = CapabilityActions.Find(action);
        if (declared == null)
            return ServiceResult<bool>.NotFound($"Unknown action '{action}'");

        try
        {
            var deleted = await _repository.DeleteAsync(action);
            if (deleted == null)
                return ServiceResult<bool>.NotFound($"No stored rule for action '{action}'");

            await _cache.RemoveAsync(CacheKey);

            await RecordChangeAsync(
                DomainEventTypes.CapabilityMatrixDeleted,
                action,
                deleted.AllowedRoles,
                declared.DefaultRoles,
                actorUserId);

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting capability matrix row for {Action}", LogSanitizer.Sanitize(action));
            return ServiceResult<bool>.InternalServerError("Failed to delete the capability matrix entry");
        }
    }

    private static CapabilityMatrixEntry? FindRow(List<CapabilityMatrixEntry> rows, string action) =>
        rows.FirstOrDefault(row => string.Equals(row.Action, action, StringComparison.Ordinal));

    /// <summary>
    /// An unrecognized role is saved rather than refused, so a role added in a later release needs no
    /// code change here; a typo therefore closes one action until the same PUT corrects it. SysAdmin
    /// is accepted too but does nothing, since its access is the bypass in <c>CapabilityMatrixFilter</c>.
    /// </summary>
    private void WarnAboutInertRoles(string action, List<string> allowedRoles)
    {
        foreach (var role in allowedRoles.Where(role => !SystemRoles.All.Contains(role)))
        {
            _logger.LogWarning(
                "Capability matrix row for {Action} names role {Role}, which is not a known system role. Saving anyway.",
                LogSanitizer.Sanitize(action), LogSanitizer.Sanitize(role));
        }

        if (allowedRoles.Contains(SystemRoles.SysAdmin))
        {
            _logger.LogInformation(
                "Capability matrix row for {Action} lists SysAdmin, which has no effect.",
                LogSanitizer.Sanitize(action));
        }
    }

    /// <summary>
    /// UpdatedBy/UpdatedAt on the row record only its current state, so a widening that is later
    /// reverted would leave no trace on the surface that decides who can reach global.users.*. Both
    /// the log line and the webhook therefore carry what the rule was as well as what it became.
    /// </summary>
    private async Task RecordChangeAsync(
        string eventType,
        string action,
        IReadOnlyList<string> previousRoles,
        IReadOnlyList<string> newRoles,
        string actorUserId,
        object? extra = null)
    {
        _logger.LogInformation(
            "Capability matrix {EventType}: action {Action}, roles [{Previous}] -> [{New}], by {Actor}",
            eventType,
            LogSanitizer.Sanitize(action),
            LogSanitizer.Sanitize(string.Join(", ", previousRoles)),
            LogSanitizer.Sanitize(string.Join(", ", newRoles)),
            LogSanitizer.RedactUserId(actorUserId));

        await _webhookEventPublisher.PublishAsync(
            eventType,
            new { action, previousAllowedRoles = previousRoles, allowedRoles = newRoles, actorUserId, extra });
    }

    /// <summary>
    /// The whole matrix under one key: it is small, and this is read on the authorization path, so a
    /// query per action would be the wrong shape. Neither <see cref="ObjectCache"/> nor the repository
    /// throws — both log and degrade — so there is nothing to catch here.
    /// </summary>
    private async Task<List<CapabilityMatrixEntry>> GetSnapshotAsync()
    {
        var cached = await _cache.GetAsync<List<CapabilityMatrixEntry>>(CacheKey);
        if (cached != null)
        {
            return cached;
        }

        var rows = await _repository.GetAllAsync();
        await _cache.SetAsync(CacheKey, rows, CacheExpiration);
        return rows;
    }
}
