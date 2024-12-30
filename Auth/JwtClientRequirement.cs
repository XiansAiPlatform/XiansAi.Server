using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.Configuration;

public class JwtClientRequirement : IAuthorizationRequirement
{
    public const string TENANT_CLAIM_TYPE = "https://flowmaxer.ai/tenant";
    
    public JwtClientRequirement(IConfiguration configuration)
    {
    }
}

public class JwtClientHandler : AuthorizationHandler<JwtClientRequirement>
{
    private readonly ILogger<JwtClientHandler> _logger;
    private readonly TenantContext _tenantContext;
    private readonly IConfiguration _configuration;

    public JwtClientHandler(
        ILogger<JwtClientHandler> logger, 
        TenantContext tenantContext,
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
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return Task.CompletedTask;
        }

        var token = authHeader.Substring("Bearer ".Length);

        try
        {
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

            if (jsonToken == null)
            {
                _logger.LogWarning("Invalid JWT token format");
                return Task.CompletedTask;
            }

            var tenantId = jsonToken.Claims.FirstOrDefault(c => c.Type == JwtClientRequirement.TENANT_CLAIM_TYPE)?.Value;

            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("Tenant ID missing in token");
                return Task.CompletedTask;
            }

            // Better configuration reading
            var tenantSection = _configuration.GetSection($"Tenants:{tenantId}");
            if (!tenantSection.Exists())
            {
                _logger.LogWarning("Tenant configuration not found for tenant ID: {TenantId}", tenantId);
                return Task.CompletedTask;
            }

            // set tenant context and succeed
            _logger.LogInformation("Authorization succeeded. Setting tenant ID: {TenantId}", tenantId);
            _tenantContext.TenantId = tenantId;
            context.Succeed(requirement);

        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
        }

        return Task.CompletedTask;
    }
}