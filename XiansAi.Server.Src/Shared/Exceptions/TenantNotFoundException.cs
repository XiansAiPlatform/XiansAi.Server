namespace Shared.Exceptions;

/// <summary>
/// Thrown when a requested tenant does not exist.
/// Used by Admin API when SysAdmin provides a tenant ID that cannot be found.
/// </summary>
public class TenantNotFoundException : Exception
{
    public string TenantId { get; }

    public TenantNotFoundException(string tenantId)
        : base($"Tenant '{tenantId}' was not found")
    {
        TenantId = tenantId;
    }
}
