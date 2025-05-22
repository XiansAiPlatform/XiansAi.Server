using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Features.WebApi.Auth.Providers;
using Shared.Auth;

namespace Features.WebApi.Auth;

/// <summary>
/// Options for configuring the auth requirement
/// </summary>
public class AuthRequirementOptions
{
    /// <summary>
    /// If true, requires and validates the tenant ID from request headers
    /// </summary>
    public bool RequireTenant { get; set; }
    
    /// <summary>
    /// If true, validates that the tenant has configuration in the system
    /// </summary>
    public bool ValidateTenantConfig { get; set; }
    
    /// <summary>
    /// Creates a token-only requirement
    /// </summary>
    public static AuthRequirementOptions TokenOnly => new() 
    { 
        RequireTenant = false, 
        ValidateTenantConfig = false 
    };
    
    /// <summary>
    /// Creates a requirement that validates tenant ID but not config
    /// </summary>
    public static AuthRequirementOptions TenantWithoutConfig => new() 
    { 
        RequireTenant = true, 
        ValidateTenantConfig = false 
    };
    
    /// <summary>
    /// Creates a full tenant requirement with config validation
    /// </summary>
    public static AuthRequirementOptions FullTenantValidation => new() 
    { 
        RequireTenant = true, 
        ValidateTenantConfig = true 
    };
}

/// <summary>
/// Unified authorization requirement that can handle both token and tenant validation
/// </summary>
public class AuthRequirement : IAuthorizationRequirement
{
    public static string TENANT_CLAIM_TYPE = "https://xians.ai/tenants";
    
    /// <summary>
    /// Options for configuring the behavior of this requirement
    /// </summary>
    public AuthRequirementOptions Options { get; }
    
    public AuthRequirement(AuthRequirementOptions options)
    {
        Options = options;
    }
}

/// <summary>
/// Handler for the unified auth requirement
/// </summary>
public class AuthRequirementHandler : AuthorizationHandler<AuthRequirement>
{
    private readonly ILogger<AuthRequirementHandler> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthProviderFactory _authProviderFactory;
    private readonly ITokenValidationCache _tokenCache;
    private readonly IConfiguration _configuration;
    
    public AuthRequirementHandler(
        ILogger<AuthRequirementHandler> logger,
        ITenantContext tenantContext,
        IAuthProviderFactory authProviderFactory,
        ITokenValidationCache tokenCache,
        IConfiguration configuration)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _authProviderFactory = authProviderFactory;
        _tokenCache = tokenCache;
        _configuration = configuration;
    }
    
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        AuthRequirement requirement)
    {
        var httpContext = context.Resource as HttpContext;
        if (httpContext == null)
        {
            _logger.LogWarning("Authorization context does not contain HttpContext");
            context.Fail();
            return;
        }
        
        // Always validate token
        var (success, loggedInUser, authorizedTenantIds) = await ValidateToken(httpContext);
        if (!success)
        {
            _logger.LogWarning("Token validation failed");
            context.Fail(new AuthorizationFailureReason(this, "Token validation failed"));
            return;
        }
        
        // Set user info to tenant context
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds ?? new List<string>();
        _tenantContext.LoggedInUser = loggedInUser ?? throw new InvalidOperationException("Logged in user not found");
        
        // If tenant validation is not required, we're done
        if (!requirement.Options.RequireTenant)
        {
            _logger.LogInformation("Token validation succeeded, tenant validation not required");
            context.Succeed(requirement);
            return;
        }
        
        // Extract and validate tenant ID
        var currentTenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            context.Fail(new AuthorizationFailureReason(this, "No Tenant ID found in X-Tenant-Id header"));
            return;
        }
        
        // Verify user has access to this tenant
        if (authorizedTenantIds == null || !authorizedTenantIds.Contains(currentTenantId))
        {
            _logger.LogWarning("Tenant ID {TenantId} is not authorized for user {UserId}", 
                currentTenantId, loggedInUser);
            context.Fail(new AuthorizationFailureReason(this, 
                $"Tenant {currentTenantId} is not authorized for this token holder"));
            return;
        }
        
        // Validate tenant configuration if required
        if (requirement.Options.ValidateTenantConfig)
        {
            var tenantSection = _configuration.GetSection($"Tenants:{currentTenantId}");
            if (!tenantSection.Exists())
            {
                _logger.LogWarning("Tenant configuration not found for tenant ID: {TenantId}", currentTenantId);
                context.Fail(new AuthorizationFailureReason(this, 
                    $"Tenant {currentTenantId} configuration not found"));
                return;
            }
        }
        
        // Set tenant ID in context
        _tenantContext.TenantId = currentTenantId;
        
        _logger.LogInformation("Authorization requirement succeeded for tenant {TenantId}", currentTenantId);
        context.Succeed(requirement);
    }
    
    private async Task<(bool success, string? loggedInUser, IEnumerable<string>? authorizedTenantIds)> 
        ValidateToken(HttpContext httpContext)
    {
        var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return (false, null, null);
        }

        var token = authHeader.Substring("Bearer ".Length);
        
        // Try to get validation result from cache
        var (found, valid, userId, tenantIds) = await _tokenCache.GetValidation(token);
        if (found)
        {
            if (valid)
            {
                // Set the user name to the logged in user from cache
                httpContext.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId!)]));
                return (true, userId, tenantIds);
            }
            return (false, null, null);
        }

        try
        {
            // Validate token through auth provider
            var authProvider = _authProviderFactory.GetProvider();
            var (success, tokenUserId, tokenTenantIds) = await authProvider.ValidateToken(token);
            
            // Cache the result
            await _tokenCache.CacheValidation(token, success, tokenUserId, tokenTenantIds);
            
            if (!success || string.IsNullOrEmpty(tokenUserId))
            {
                _logger.LogWarning("Token validation failed");
                return (false, null, null);
            }
            
            // Set the user name to the logged in user
            httpContext.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, tokenUserId)]));

            return (true, tokenUserId, tokenTenantIds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null, null);
        }
    }
} 