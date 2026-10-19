namespace Shared.Exceptions;

/// <summary>
/// Thrown when a requested tenant does not exist. The global exception handler maps it to 404.
/// Admin API authentication does not throw it: a SysAdmin naming a missing tenant is an expected
/// probe (see <c>Features.AdminApi.Auth.AdminAuthFailure</c>), and an escaped exception is logged
/// at error level by the exception middleware.
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
