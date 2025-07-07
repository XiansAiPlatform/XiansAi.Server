using Auth0.ManagementApi.Models;
using Features.WebApi.Auth.Providers;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Authorization;
using Shared.Auth;
using Shared.Utils;
using System.Security.Claims;
using XiansAi.Server.Features.WebApi.Services;

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
    /// <summary>
    /// Options for configuring the behavior of this requirement
    /// </summary>
    public AuthRequirementOptions Options { get; }
    
    /// <summary>
    /// Required roles (optional)
    /// </summary>
    public string[]? RequiredRoles { get; }
    
    public AuthRequirement(AuthRequirementOptions options, params string[]? requiredRoles)
    {
        Options = options;
        RequiredRoles = requiredRoles;
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
    private readonly ITenantService _tenantService;
    private readonly IRoleCacheService _roleCacheService;
    public AuthRequirementHandler(
        ILogger<AuthRequirementHandler> logger,
        ITenantContext tenantContext,
        IAuthProviderFactory authProviderFactory,
        ITokenValidationCache tokenCache,
        IConfiguration configuration,
        IRoleCacheService roleCacheService,
        ITenantService tenantService)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _authProviderFactory = authProviderFactory;
        _tokenCache = tokenCache;
        _configuration = configuration;
        _tenantService = tenantService;
        _roleCacheService = roleCacheService;
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
        
        // Try authorization with cache first, then retry without cache if needed
        var success = await TryAuthorizeWithRetry(context, httpContext, requirement);
        if (!success)
        {
            _logger.LogWarning("Authorization failed for requirement: {RequirementType}", requirement.GetType().Name);
            return; // Context.Fail() already called in TryAuthorize methods
        }
        
        _logger.LogInformation("Authorization succeeded for requirement: {RequirementType}", requirement.GetType().Name);
        context.Succeed(requirement);
    }
    
    private async Task<bool> TryAuthorizeWithRetry(AuthorizationHandlerContext context, HttpContext httpContext, AuthRequirement requirement)
    {
        // First attempt with cache
        var success = await TryAuthorize(context, httpContext, requirement, bypassCache: false);
        
        // If authorization failed, retry with fresh token validation
        if (!success)
        {
            _logger.LogInformation("Authorization failed with cached data, retrying with fresh validation");
            success = await TryAuthorize(context, httpContext, requirement, bypassCache: true);
        }
        
        return success;
    }
    
    private async Task<bool> TryAuthorize(AuthorizationHandlerContext context, HttpContext httpContext, AuthRequirement requirement, bool bypassCache)
    {
        // Always validate token
        var (success, loggedInUser, authorizedTenantIds, roles) = await ValidateToken(httpContext, bypassCache);
        if (!success)
        {
            _logger.LogWarning("Token validation failed");
            context.Fail(new AuthorizationFailureReason(this, "Token validation failed"));
            return false;
        }
        // If no tenant ids are returned, set the default tenant id
        if (authorizedTenantIds == null || authorizedTenantIds.Count() == 0) {
            authorizedTenantIds = new List<string> { Constants.DefaultTenantId };
        }
        
        // Set user info to tenant context
        _tenantContext.AuthorizedTenantIds = authorizedTenantIds ?? new List<string>();
        _tenantContext.LoggedInUser = loggedInUser ?? throw new InvalidOperationException("Logged in user not found");
        _tenantContext.UserRoles = roles?.ToArray() ?? Array.Empty<string>();

        // If tenant validation is not required, we're done
        if (!requirement.Options.RequireTenant)
        {
            _logger.LogInformation("Token validation succeeded, tenant validation not required");
            return true;
        }
        
        // Extract and validate tenant ID
        var currentTenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        if (string.IsNullOrEmpty(currentTenantId))
        {
            _logger.LogWarning("No Tenant ID found in X-Tenant-Id header");
            context.Fail(new AuthorizationFailureReason(this, "No Tenant ID found in X-Tenant-Id header"));
            return false;
        }
        
        // Verify user has access to this tenant
        if (currentTenantId != Constants.DefaultTenantId && !authorizedTenantIds!.Contains(currentTenantId))
        {
            var logMessage = bypassCache 
                ? "Tenant ID {TenantId} is not authorized for user {UserId} even after fresh token validation"
                : "Tenant ID {TenantId} is not authorized for user {UserId} (cached result)";
            
            _logger.LogWarning(logMessage, currentTenantId, loggedInUser);
            
            // Only fail permanently if this was already a retry
            if (bypassCache)
            {
                context.Fail(new AuthorizationFailureReason(this, 
                    $"Tenant {currentTenantId} is not authorized for this token holder"));
            }
            return false;
        }
        
        // Validate tenant configuration if required
        if (requirement.Options.ValidateTenantConfig)
        {
            var tenantResult = await _tenantService.GetTenantByTenantId(currentTenantId);
            if (!tenantResult.IsSuccess || tenantResult.Data == null)
            {
                _logger.LogWarning("Tenant configuration not found for tenant ID: {TenantId}", currentTenantId);
                context.Fail(new AuthorizationFailureReason(this, 
                    $"Tenant {currentTenantId} configuration not found"));
                return false;
            }
        }

        // Remove existing role claims from all identities
        foreach (var identity in httpContext.User.Identities.OfType<ClaimsIdentity>())
        {
            var existingRoleClaims = identity.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .ToList();
            foreach (var claim in existingRoleClaims)
            {
                identity.RemoveClaim(claim);
            }
        }

        // Add new role claims to the primary identity
        if (httpContext.User.Identity is ClaimsIdentity primaryIdentity)
        {
            foreach (var role in roles ?? Enumerable.Empty<string>())
            {
                primaryIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
                _logger.LogInformation("Added role claim: {Role} for user {UserId}", role, loggedInUser);
            }
            
            // Log system role constants for comparison
            _logger.LogInformation("SystemRoles.SysAdmin constant: '{SysAdminRole}'", SystemRoles.SysAdmin);
        }

        // Log all current claims for debugging
        var allClaims = httpContext.User.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
        _logger.LogInformation("All user claims after role assignment: {Claims}", string.Join(", ", allClaims));

        // Check required roles if specified
        if (requirement.RequiredRoles != null && requirement.RequiredRoles.Length > 0)
        {
            var userRoles = roles?.ToList() ?? new List<string>();
            var hasRequiredRole = requirement.RequiredRoles.Any(requiredRole => 
                userRoles.Contains(requiredRole, StringComparer.OrdinalIgnoreCase));
            
            if (!hasRequiredRole)
            {
                _logger.LogWarning("User {UserId} does not have any of the required roles: {RequiredRoles}. User roles: {UserRoles}", 
                    loggedInUser, string.Join(", ", requirement.RequiredRoles), string.Join(", ", userRoles));
                context.Fail(new AuthorizationFailureReason(this, 
                    $"User does not have required role. Required: {string.Join(" or ", requirement.RequiredRoles)}"));
                return false;
            }
            
            _logger.LogInformation("User {UserId} has required role. Required: {RequiredRoles}, User has: {UserRoles}", 
                loggedInUser, string.Join(", ", requirement.RequiredRoles), string.Join(", ", userRoles));
        }

        // Set tenant ID in context
        _tenantContext.TenantId = currentTenantId;
        return true;
    }
    
    private async Task<(bool success, string? loggedInUser, IEnumerable<string>? authorizedTenantIds, IEnumerable<string>? roles)> 
        ValidateToken(HttpContext httpContext, bool bypassCache = false)
    {
        var authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
        var currentTenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            _logger.LogWarning("No Bearer token found in Authorization header");
            return (false, null, null, null);
        }

        var token = authHeader.Substring("Bearer ".Length);

        // Try to get validation result from cache (unless bypassing)
        if (!bypassCache)
        {
            var (found, valid, userId, tenantIds) = await _tokenCache.GetValidation(token);
            if (found)
            {
                // Since we only cache successful validations, if found in cache it's always valid
                if (currentTenantId == null)
                {
                    _logger.LogWarning("No tenantId found in request header");
                    return (false, null, null, null);
                }

                var roles = await _roleCacheService.GetUserRolesAsync(userId!, currentTenantId);
                // Don't add roles here - they will be added in TryAuthorize method

                // Set the user name to the logged in user from cache
                httpContext.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, userId!)]));
                return (true, userId, tenantIds, roles);
            }
        }
        else
        {
            _logger.LogInformation("Bypassing token cache for fresh validation");
        }

        try
        {
            if (currentTenantId == null)
            {
                _logger.LogWarning("No tenantId found in request header");
                return (false, null, null, null);
            }
            // Validate token through auth provider
            var authProvider = _authProviderFactory.GetProvider();
            var (success, tokenUserId, tokenTenantIds) = await authProvider.ValidateToken(token);

            // Cache the result (always cache fresh validation results)
            await _tokenCache.CacheValidation(token, success, tokenUserId, tokenTenantIds);
            
            if (!success || string.IsNullOrEmpty(tokenUserId))
            {
                _logger.LogWarning("Token validation failed from auth provider");
                return (false, null, null, null);
            }
            
            // Set the user name to the logged in user
            httpContext.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Name, tokenUserId)]));

            var roles = await _roleCacheService.GetUserRolesAsync(tokenUserId, currentTenantId);
            // Don't add roles here - they will be added in TryAuthorize method

            return (true, tokenUserId, tokenTenantIds, roles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing JWT token");
            return (false, null, null, null);
        }
    }
} 