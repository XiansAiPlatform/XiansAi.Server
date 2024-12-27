using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

public class HasScopeHandler : AuthorizationHandler<HasScopeRequirement>
{
    private readonly TenantContext _tenantContext;

    public HasScopeHandler(TenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, HasScopeRequirement requirement)
    {
        if (!context.User.HasClaim(c => c.Type == "scope" && c.Issuer == requirement.Issuer))
            return Task.CompletedTask;

        var scopes = context.User.FindFirst(c => c.Type == "scope" && c.Issuer == requirement.Issuer)?.Value.Split(' ');

        if (scopes != null && scopes.Any(s => s == requirement.Scope) && _tenantContext.TenantId == requirement.TenantId)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}