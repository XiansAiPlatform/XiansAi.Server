using Microsoft.AspNetCore.Authorization;

public class HasScopeRequirement : IAuthorizationRequirement
{
    public string Issuer { get; }
    public string Scope { get; }
    public string TenantId { get; }
    public HasScopeRequirement(string scope, string issuer, string tenantId)
    {
        Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        Issuer = issuer ?? throw new ArgumentNullException(nameof(issuer));
        TenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
    }
} 