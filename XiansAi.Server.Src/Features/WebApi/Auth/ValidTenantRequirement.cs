using Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using XiansAi.Server.Auth;

namespace Features.WebApi.Auth;

public class ValidTenantRequirement : BaseAuthRequirement
{
    public ValidTenantRequirement(IConfiguration configuration) : base(configuration)
    {
    }
}

public class ValidTenantHandler : BaseAuthHandler<ValidTenantRequirement>
{
    private readonly IConfiguration _configuration;

    public ValidTenantHandler(
        ILogger<ValidTenantHandler> logger, 
        ITenantContext tenantContext,
        IConfiguration configuration)
        : base(logger, tenantContext)
    {
        _configuration = configuration;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidTenantRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var currentTenantId = httpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            context.Fail(new AuthorizationFailureReason(this, "No Tenant ID found in X-Tenant-Id header"));
            return Task.CompletedTask;
        }

        var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);
        if (!success)
        {
            _logger.LogWarning("Token validation failed");
            context.Fail(new AuthorizationFailureReason(this, "Token validation failed"));
            return Task.CompletedTask;
        }

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
        _tenantContext.LoggedInUser = loggedInUser ?? throw new InvalidOperationException("Logged in user not found");

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}