using Shared.Auth;
using Microsoft.AspNetCore.Authorization;
using Features.WebApi.Auth.Providers;

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
        IConfiguration configuration,
        IAuthProviderFactory authProviderFactory)
        : base(logger, tenantContext, authProviderFactory)
    {
        _configuration = configuration;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ValidTenantRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        var currentTenantId = httpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            context.Fail(new AuthorizationFailureReason(this, "No Tenant ID found in X-Tenant-Id header"));
            return;
        }

        var (success, loggedInUser, authorizedTenantIds) = await ValidateToken(context);
        if (!success)
        {
            _logger.LogWarning("Token validation failed");
            context.Fail(new AuthorizationFailureReason(this, "Token validation failed"));
            return;
        }

        if (authorizedTenantIds == null || !authorizedTenantIds.Contains(currentTenantId))
        {
            _logger.LogWarning($"Tenant ID {currentTenantId} is not authorized for this token holder");
            context.Fail(new AuthorizationFailureReason(this, $"Tenant {currentTenantId} is not authorized for this token holder"));
            return;
        }

        var tenantSection = _configuration.GetSection($"Tenants:{currentTenantId}");
        if (!tenantSection.Exists())
        {
            _logger.LogWarning("Tenant configuration not found for tenant ID: {currentTenantId}", currentTenantId);
            context.Fail(new AuthorizationFailureReason(this, $"Tenant {currentTenantId} configuration not found"));
            return;
        }

        // set tenant context and succeed
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds;
        _tenantContext.TenantId = currentTenantId;
        _tenantContext.LoggedInUser = loggedInUser ?? throw new InvalidOperationException("Logged in user not found");

        context.Succeed(requirement);
    }
}