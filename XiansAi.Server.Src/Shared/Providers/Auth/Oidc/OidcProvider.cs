using Microsoft.AspNetCore.Authentication.JwtBearer;
using Shared.Auth;
using Shared.Providers.Auth.Auth0;
using Shared.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Shared.Providers.Auth.Oidc;

public class OidcProvider : IAuthProvider
{
    private readonly ILogger<OidcProvider> _logger;
    private readonly OidcConfig _config;
    private readonly OidcTokenService _tokenService;

    public OidcProvider(
        ILogger<OidcProvider> logger,
        OidcTokenService tokenService,
        IConfiguration configuration)
    {
        _logger = logger;
        _tokenService = tokenService;

        // Load OIDC configuration
        _config = configuration.GetSection("Oidc").Get<OidcConfig>() ??
            throw new ArgumentException("OIDC configuration is missing");

        if (string.IsNullOrEmpty(_config.Authority))
            throw new ArgumentException("OIDC Authority is missing");

        if (string.IsNullOrEmpty(_config.Audience))
            throw new ArgumentException("OIDC Audience is missing");

        _logger.LogInformation("Initialized OIDC Provider: {ProviderName}, Authority: {Authority}",
            _config.ProviderName, _config.Authority);
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        options.Authority = _config.Authority;
        options.RequireHttpsMetadata = _config.RequireHttpsMetadata;

        options.TokenValidationParameters.ValidateIssuer = true;
        options.TokenValidationParameters.ValidIssuer = _config.Issuer ?? _config.Authority;
        
        options.TokenValidationParameters.ValidateAudience = true;
        options.TokenValidationParameters.ValidAudience = _config.Audience;
        
        options.TokenValidationParameters.ValidateLifetime = true;
        options.TokenValidationParameters.ClockSkew = TimeSpan.FromMinutes(5);
        
        options.TokenValidationParameters.RequireSignedTokens = _config.RequireSignedTokens;
        options.TokenValidationParameters.ValidAlgorithms = _config.AcceptedAlgorithms;

        // Map claims properly
        options.MapInboundClaims = false;
        
        // Clear default claim type mappings to use original claim names
        JwtSecurityTokenHandler.DefaultInboundClaimTypeMap.Clear();

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                _logger.LogError("JWT Bearer authentication failed: {Exception}", context.Exception?.Message);
                _logger.LogError("Exception details: {FullException}", context.Exception?.ToString());
                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                _logger.LogWarning("JWT Bearer challenge triggered: {Error}, {ErrorDescription}",
                    context.Error, context.ErrorDescription);
                return Task.CompletedTask;
            },

            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    _logger.LogInformation("Original identity authenticated: {IsAuthenticated}, Type: {AuthenticationType}",
                        identity.IsAuthenticated, identity.AuthenticationType);

                    // Ensure the identity is properly authenticated
                    if (!identity.IsAuthenticated)
                    {
                        _logger.LogInformation("Creating new authenticated identity with JWT authentication type");
                        var authenticatedIdentity = new ClaimsIdentity(
                            identity.Claims,
                            "JWT",
                            identity.NameClaimType,
                            identity.RoleClaimType);
                        context.Principal = new ClaimsPrincipal(authenticatedIdentity);
                        identity = authenticatedIdentity;

                        _logger.LogInformation("New identity authenticated: {IsAuthenticated}, Type: {AuthenticationType}",
                            authenticatedIdentity.IsAuthenticated, authenticatedIdentity.AuthenticationType);
                    }

                    // Extract user ID using configured claim mapping
                    var userId = identity.FindFirst(_config.ClaimMappings.UserIdClaim)?.Value;

                    // Fallback to standard claims if custom mapping not found
                    if (string.IsNullOrEmpty(userId))
                    {
                        userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                            ?? identity.FindFirst("sub")?.Value;
                    }

                    if (!string.IsNullOrEmpty(userId))
                    {
                        // Add NameIdentifier claim if not present
                        if (identity.FindFirst(ClaimTypes.NameIdentifier) == null)
                        {
                            identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
                        }

                        _logger.LogInformation("Extracted user ID: {UserId}", userId);

                        // Handle tenant-based roles
                        var tenantId = context.HttpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();

                        if (!string.IsNullOrEmpty(tenantId))
                        {
                            using var scope = context.HttpContext.RequestServices.CreateScope();
                            var roleCacheService = scope.ServiceProvider
                                .GetRequiredService<IRoleCacheService>();

                            var roles = await roleCacheService.GetUserRolesAsync(userId, tenantId);

                            // Remove existing role claims to prevent accumulation
                            var existingRoleClaims = identity.Claims
                                .Where(c => c.Type == ClaimTypes.Role)
                                .ToList();
                            foreach (var existingClaim in existingRoleClaims)
                            {
                                identity.RemoveClaim(existingClaim);
                            }

                            // Add fresh role claims
                            var uniqueRoles = roles.Distinct().ToList();
                            foreach (var role in uniqueRoles)
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                            }

                            _logger.LogInformation("Added {RoleCount} roles to user {UserId}: {Roles}",
                                roles.Count(), userId, string.Join(", ", roles));
                        }
                        else
                        {
                            // No tenant ID header - assign default TenantUser role
                            _logger.LogInformation("No X-Tenant-Id header found, assigning default TenantUser role to user {UserId}", userId);
                            
                            // Remove existing role claims
                            var existingRoleClaims = identity.Claims
                                .Where(c => c.Type == ClaimTypes.Role)
                                .ToList();
                            foreach (var existingClaim in existingRoleClaims)
                            {
                                identity.RemoveClaim(existingClaim);
                            }
                            
                            // Add default role
                            identity.AddClaim(new Claim(ClaimTypes.Role, SystemRoles.TenantUser));
                        }
                    }

                    // Set the User property of HttpContext
                    context.HttpContext.User = context.Principal;

                    _logger.LogInformation("OIDC user authenticated: {UserId}, IsAuthenticated: {IsAuthenticated}",
                        userId, context.HttpContext.User.Identity?.IsAuthenticated);
                }
                else
                {
                    _logger.LogWarning("No principal or identity found in OnTokenValidated");
                }
            }
        };
    }

    public async Task<(bool success, string? userId)> ValidateToken(string token)
    {
        return await _tokenService.ProcessToken(token);
    }

    public Task<UserInfo> GetUserInfo(string userId)
    {
        // For generic OIDC, we don't have a management API to fetch user info
        // Return basic info from token claims
        _logger.LogInformation("GetUserInfo called for generic OIDC provider - returning minimal user info");
        
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
        // Generic OIDC doesn't have tenant management built-in
        return Task.FromResult(new List<string>());
    }

    public Task<string> SetNewTenant(string userId, string tenantId)
    {
        // Generic OIDC doesn't support setting tenants via management API
        _logger.LogWarning("SetNewTenant called for generic OIDC provider - not supported");
        return Task.FromResult("Generic OIDC provider does not support tenant management");
    }
}

