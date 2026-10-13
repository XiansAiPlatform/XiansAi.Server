using System.ComponentModel.DataAnnotations;
using Shared.Data.Models;
using Shared.Services;

namespace Features.AdminApi.Auth;

/// <summary>
/// Why an Admin API caller could not be authenticated. Decides the status code the caller gets
/// back: anything other than <see cref="Unauthorized"/> is about the tenant the request names,
/// not the caller's credentials, so it must not be reported as a 401.
/// </summary>
public enum AdminAuthFailure
{
    /// <summary>Credentials missing or invalid, or the caller lacks the required role (401).</summary>
    Unauthorized,

    /// <summary>A SysAdmin named a well-formed tenant id that does not exist (404).</summary>
    TenantNotFound,

    /// <summary>A SysAdmin named a tenant id that is not in the tenant id format (400).</summary>
    InvalidTenantId
}

/// <summary>
/// The check both Admin API resolvers run when a SysAdmin names the tenant to act on.
/// </summary>
/// <remarks>
/// A missing tenant is an expected answer rather than an error: since the Agent API probes the
/// lowercase form of a tenant id before its legacy casing, the first probe for every legacy tenant
/// misses. It is therefore returned as a value instead of thrown, so it never reaches the exception
/// middleware, which would log it at error level with a stack trace.
/// </remarks>
internal static class SysAdminTenantCheck
{
    /// <summary>
    /// Returns null when <paramref name="tenantId"/> names an existing tenant, otherwise why it does not.
    /// The format is checked first so a malformed id never reaches the tenant cache, which would
    /// reject it by throwing.
    /// </summary>
    public static async Task<AdminAuthFailure?> FindFailureAsync(
        ITenantCacheService tenantCacheService, string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            Tenant.SanitizeAndValidateTenantId(tenantId);
        }
        catch (ValidationException)
        {
            return AdminAuthFailure.InvalidTenantId;
        }

        var tenant = await tenantCacheService.GetByTenantIdAsync(tenantId, cancellationToken);
        return tenant == null ? AdminAuthFailure.TenantNotFound : null;
    }

    /// <summary>
    /// The message returned to the caller. The not-found message names the tenant id, which has
    /// passed the format check by then. The invalid-format message does not echo the input.
    /// </summary>
    public static string MessageFor(AdminAuthFailure failure, string tenantId) => failure switch
    {
        AdminAuthFailure.TenantNotFound => $"Tenant '{tenantId}' was not found",
        AdminAuthFailure.InvalidTenantId => "Invalid tenant ID format",
        _ => "Authorization failed"
    };
}
