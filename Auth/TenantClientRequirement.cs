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

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantClientRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var currentTenantId = httpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            return Task.CompletedTask;
        }

        var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);
        
        if (!success || authorizedTenantIds == null || !authorizedTenantIds.Contains(currentTenantId))
        {
            _logger.LogWarning($"Tenant ID {currentTenantId} is not authorized for this token holder");
            return Task.CompletedTask;
        }

        if (currentTenantId.Contains('-'))
        {
            _logger.LogWarning($"Tenant ID cannot contain '-'");
            return Task.CompletedTask;
        }

        var tenantSection = _configuration.GetSection($"Tenants:{currentTenantId}");
        if (!tenantSection.Exists())
        {
            _logger.LogWarning("Tenant configuration not found for tenant ID: {currentTenantId}", currentTenantId);
            return Task.CompletedTask;
        }

        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds;
        _tenantContext.TenantId = currentTenantId;
        _tenantContext.LoggedInUser = loggedInUser ?? null;
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}