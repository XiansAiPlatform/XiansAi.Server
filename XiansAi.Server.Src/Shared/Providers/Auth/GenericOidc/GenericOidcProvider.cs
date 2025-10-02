using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Security.Claims;
using Shared.Providers.Auth.Auth0;
using Shared.Services;
using Shared.Auth;

namespace Shared.Providers.Auth.GenericOidc;

public class GenericOidcProvider : IAuthProvider
{
    private readonly ILogger<GenericOidcProvider> _logger;
    private readonly IDynamicOidcValidator _oidcValidator;
    private readonly IServiceProvider _serviceProvider;

    public GenericOidcProvider(
        ILogger<GenericOidcProvider> logger,
        IDynamicOidcValidator oidcValidator,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _oidcValidator = oidcValidator;
        _serviceProvider = serviceProvider;
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        // Generic OIDC uses per-tenant configuration from database via DynamicOidcValidator
        // Configure JWT Bearer middleware with minimal settings to extract and parse tokens
        // Actual validation is done by DynamicOidcValidator in OnMessageReceived event
        
        // Disable default claim type mapping to preserve original claim types
        options.MapInboundClaims = false;
        
        // Skip default token validation - we'll use DynamicOidcValidator instead
        options.TokenValidationParameters.ValidateIssuer = false;
        options.TokenValidationParameters.ValidateAudience = false;
        options.TokenValidationParameters.ValidateLifetime = false;
        options.TokenValidationParameters.ValidateIssuerSigningKey = false;
        options.TokenValidationParameters.RequireSignedTokens = false;
        
        // Configure events for per-tenant validation and role management
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = async context =>
            {
                // Extract token from Authorization header
                var token = context.Request.Headers["Authorization"].FirstOrDefault()?.Replace("Bearer ", "");
                
                if (string.IsNullOrEmpty(token))
                {
                    _logger.LogWarning("No bearer token found in request");
                    context.Fail("No bearer token provided");
                    return;
                }

                // Get tenant ID from header
                var tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                
                if (string.IsNullOrEmpty(tenantId))
                {
                    _logger.LogWarning("No X-Tenant-Id header found in request");
                    context.Fail("X-Tenant-Id header is required");
                    return;
                }

                // Use DynamicOidcValidator for per-tenant validation
                try
                {
                    using var scope = context.HttpContext.RequestServices.CreateScope();
                    var validator = scope.ServiceProvider.GetRequiredService<IDynamicOidcValidator>();
                    
                    var (success, canonicalUserId, error) = await validator.ValidateAsync(tenantId, token);
                    
                    if (!success)
                    {
                        _logger.LogWarning("Token validation failed for tenant {TenantId}: {Error}", tenantId, error);
                        context.Fail($"Token validation failed: {error}");
                        return;
                    }

                    _logger.LogDebug("Token validated successfully for tenant {TenantId}, user {UserId}", tenantId, canonicalUserId);
                    
                    // Create claims identity from validated token
                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.NameIdentifier, canonicalUserId!),
                        new Claim("TenantId", tenantId)
                    };
                    
                    var identity = new ClaimsIdentity(claims, "JWT");
                    var principal = new ClaimsPrincipal(identity);
                    
                    // Set the token for downstream use
                    context.Token = token;
                    context.Principal = principal;
                    context.Success();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during token validation for tenant {TenantId}", tenantId);
                    context.Fail($"Token validation error: {ex.Message}");
                }
            },

            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    _logger.LogDebug("Token validated for Generic OIDC provider (per-tenant)");

                    // Extract user ID from claims
                    var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                    var tenantId = identity.FindFirst("TenantId")?.Value;

                    if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(tenantId))
                    {
                        // Load user roles for the tenant
                        using var scope = context.HttpContext.RequestServices.CreateScope();
                        var roleCacheService = scope.ServiceProvider.GetRequiredService<IRoleCacheService>();

                        var roles = await roleCacheService.GetUserRolesAsync(userId, tenantId);

                        // Add role claims
                        var uniqueRoles = roles.Distinct().ToList();
                        foreach (var role in uniqueRoles)
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Role, role));
                        }

                        _logger.LogInformation("Added {RoleCount} roles to Generic OIDC user {UserId} for tenant {TenantId}: {Roles}",
                            roles.Count(), userId, tenantId, string.Join(", ", roles));
                    }

                    // Set the User property of HttpContext
                    context.HttpContext.User = context.Principal;

                    _logger.LogDebug("Generic OIDC user authenticated: {UserId}, Tenant: {TenantId}, IsAuthenticated: {IsAuthenticated}",
                        userId, tenantId, context.HttpContext.User.Identity?.IsAuthenticated);
                }
            },

            OnAuthenticationFailed = context =>
            {
                _logger.LogError("JWT Bearer authentication failed: {Exception}", context.Exception?.Message);
                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                _logger.LogWarning("JWT Bearer challenge triggered: {Error}, {ErrorDescription}",
                    context.Error, context.ErrorDescription);
                return Task.CompletedTask;
            }
        };
    }

    public async Task<(bool success, string? userId)> ValidateToken(string token)
    {
        // Note: This method requires a tenant ID which should be available in the calling context
        // For Web API, tenant ID comes from X-Tenant-Id header (handled in ConfigureJwtBearer)
        // If called directly, caller must ensure tenant context is set
        
        try
        {
            // Try to get tenant ID from HttpContext if available
            var httpContextAccessor = _serviceProvider.GetService<IHttpContextAccessor>();
            var tenantId = httpContextAccessor?.HttpContext?.Request.Headers["X-Tenant-Id"].FirstOrDefault();
            
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("ValidateToken called without tenant ID in context");
                return (false, null);
            }

            var (success, canonicalUserId, error) = await _oidcValidator.ValidateAsync(tenantId, token);
            
            if (!success)
            {
                _logger.LogWarning("Token validation failed for tenant {TenantId}: {Error}", tenantId, error);
            }

            return (success, canonicalUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating token");
            return (false, null);
        }
    }

    public Task<UserInfo> GetUserInfo(string userId)
    {
        // Generic OIDC doesn't have a standard user info endpoint implementation
        // This would need to be customized based on the specific OIDC provider
        _logger.LogWarning("GetUserInfo not implemented for Generic OIDC provider. " +
            "Implement based on your specific OIDC provider's user info endpoint.");
        
        // Return a basic UserInfo object
        return Task.FromResult(new UserInfo
        {
            UserId = userId,
            Nickname = userId,
            AppMetadata = new AppMetadata { Tenants = Array.Empty<string>() },
            CreatedAt = DateTime.UtcNow,
            LastLogin = DateTime.UtcNow
        });
    }

    public Task<List<string>> GetUserTenants(string userId)
    {
        // Generic OIDC doesn't have a standard way to get user tenants
        // This would need to be implemented based on your specific OIDC provider or database
        _logger.LogWarning("GetUserTenants not implemented for Generic OIDC provider");
        return Task.FromResult(new List<string>());
    }

    public Task<string> SetNewTenant(string userId, string tenantId)
    {
        // Generic OIDC doesn't have a standard way to set tenants
        // This would need to be implemented based on your specific OIDC provider's API
        _logger.LogWarning("SetNewTenant not implemented for Generic OIDC provider");
        throw new NotImplementedException("SetNewTenant is not available for Generic OIDC provider. " +
            "Implement based on your specific OIDC provider's user management API.");
    }
}


