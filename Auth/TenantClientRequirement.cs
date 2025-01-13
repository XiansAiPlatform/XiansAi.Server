using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;

namespace XiansAi.Server.Auth;

public class TenantClientRequirement : BaseAuthRequirement
{
    public TenantClientRequirement(IConfiguration configuration) : base(configuration)
    {
    }
}

public class TenantClientHandler : BaseAuthHandler<TenantClientRequirement>
{
    private readonly IConfiguration _configuration;

    public TenantClientHandler(
        ILogger<TenantClientHandler> logger, 
        ITenantContext tenantContext,
        IConfiguration configuration)
        : base(logger, tenantContext)
    {
        _configuration = configuration;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantClientRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var currentTenantId = httpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            return;
        }

        var (success, loggedInUser, authorizedTenantIds) = ValidateTokenAsync(context);
        
        if (!success || authorizedTenantIds == null || !authorizedTenantIds.Contains(currentTenantId))
        {
            _logger.LogWarning($"Tenant ID {currentTenantId} is not authorized for this token holder");
            return;
        }

        if (currentTenantId.Contains('-'))
        {
            _logger.LogWarning($"Tenant ID cannot contain '-'");
            return;
        }

        var tenantSection = _configuration.GetSection($"Tenants:{currentTenantId}");
        if (!tenantSection.Exists())
        {
            _logger.LogWarning("Tenant configuration not found for tenant ID: {currentTenantId}", currentTenantId);
            return;
        }

        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds;
        _tenantContext.TenantId = currentTenantId;
        _tenantContext.LoggedInUser = loggedInUser ?? null;
        context.Succeed(requirement);
    }
}