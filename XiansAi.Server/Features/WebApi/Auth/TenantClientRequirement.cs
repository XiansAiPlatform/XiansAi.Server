using Features.Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using XiansAi.Server.Auth;

namespace Features.WebApi.Auth;

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
        _logger.LogDebug("Handling TenantClientRequirement");
        var httpContext = context.Resource as HttpContext;
        var currentTenantId = httpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        _logger.LogDebug($"Current Tenant ID: {currentTenantId}");

        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            context.Fail(new AuthorizationFailureReason(this, "No Tenant ID found in X-Tenant-Id header"));
            return Task.CompletedTask;
        }

        var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);
        _logger.LogDebug($"Token validation result: {success} {loggedInUser} {authorizedTenantIds}");
        if (!success)
        {
            _logger.LogWarning("Token validation failed");
            context.Fail(new AuthorizationFailureReason(this, "Token validation failed"));
            return Task.CompletedTask;
        }

        _logger.LogDebug($"Authorized Tenant IDs: {string.Join(", ", authorizedTenantIds ?? new List<string>())}");

        if (authorizedTenantIds == null || !authorizedTenantIds.Contains(currentTenantId))
        {
            _logger.LogWarning($"Tenant ID {currentTenantId} is not authorized for this token holder");
            context.Fail(new AuthorizationFailureReason(this, $"Tenant {currentTenantId} is not authorized for this token holder"));
            return Task.CompletedTask;
        }

        var tenantSection = _configuration.GetSection($"Tenants:{currentTenantId}");
        if (!tenantSection.Exists())
        {
            _logger.LogWarning("Tenant configuration not found for tenant ID: {currentTenantId}", currentTenantId);
            context.Fail(new AuthorizationFailureReason(this, $"Tenant {currentTenantId} configuration not found"));
            return Task.CompletedTask;
        }

        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds;
        _tenantContext.TenantId = currentTenantId;
        _tenantContext.LoggedInUser = loggedInUser ?? null;

        _logger.LogDebug($"Tenant configuration: {_tenantContext}");
        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}