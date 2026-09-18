namespace Shared.Auditing;

/// <summary>
/// Tenant id used to stamp audit rows that are not scoped to any customer tenant
/// (SysAdmin grant/revoke, global user edits, system template changes, and similar).
/// Tenant-scoped audit queries never return these rows, so a tenant's own admins cannot
/// see platform-wide security events. The id is reserved and cannot be used to create
/// a real tenant.
/// </summary>
public static class AuditLogTenants
{
    public const string Platform = "__platform__";

    public static bool IsPlatform(string? tenantId) =>
        string.Equals(tenantId, Platform, StringComparison.OrdinalIgnoreCase);
}
