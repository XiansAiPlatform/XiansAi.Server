using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;

public class JwtClientRequirement : IAuthorizationRequirement
{
    public const string TENANT_CLAIM_TYPE = "https://flowmaxer.ai/tenants";
    
    public JwtClientRequirement(IConfiguration configuration)
    {
    }
}

public class JwtClientHandler : AuthorizationHandler<JwtClientRequirement>
{
    private readonly ILogger<JwtClientHandler> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IConfiguration _configuration;

    public JwtClientHandler(
        ILogger<JwtClientHandler> logger, 
        ITenantContext tenantContext,
        IConfiguration configuration)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _configuration = configuration;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        JwtClientRequirement requirement)
    {
        // Get token from Authorization header instead of claims
        var httpContext = context.Resource as HttpContext;
        var authHeader = httpContext?.Request.Headers["Authorization"].FirstOrDefault();
        var currentTenantId = httpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return Task.CompletedTask;
        }

        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            return Task.CompletedTask;
        }

        var token = authHeader.Substring("Bearer ".Length);

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            _logger.LogInformation("Token: {token}", token);

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format");
                return Task.CompletedTask;
            }

            var authorizedTenantIds = jsonToken.Claims
                .Where(c => c.Type == JwtClientRequirement.TENANT_CLAIM_TYPE)
                .Select(c => c.Value);

            
            _logger.LogInformation("Authorized tenant IDs: {authorizedTenantIds}", authorizedTenantIds);

            if (authorizedTenantIds == null || !authorizedTenantIds.Contains(currentTenantId))
            {
                _logger.LogWarning($"Tenant ID {currentTenantId} is not authorized for this token holder");
                return Task.CompletedTask;
            }

            if (currentTenantId.Contains('-'))
            {
                _logger.LogWarning($"Tenant ID cannot contain '-'");
                return Task.CompletedTask;
            }

            // Better configuration reading
            var tenantSection = _configuration.GetSection($"Tenants:{currentTenantId}");
            if (!tenantSection.Exists())
            {
                _logger.LogWarning("Tenant configuration not found for tenant ID: {currentTenantId}", currentTenantId);
                return Task.CompletedTask;
            }

            // set tenant context and succeed
            _logger.LogInformation("Authorization succeeded. Setting tenant ID: {currentTenantId}", currentTenantId);
            _tenantContext.TenantId = currentTenantId;
            context.Succeed(requirement);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
        }

        return Task.CompletedTask;
    }
}