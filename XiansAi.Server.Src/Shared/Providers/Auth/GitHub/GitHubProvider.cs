using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Shared.Providers.Auth.Auth0;
using Shared.Services;

namespace Shared.Providers.Auth.GitHub;

/// <summary>
/// GitHub authentication provider that validates self-issued JWTs
/// </summary>
public class GitHubProvider : IAuthProvider
{
    private readonly ILogger<GitHubProvider> _logger;
    private readonly GitHubConfig _config;
    private readonly GitHubTokenService _tokenService;
    private SecurityKey? _signingKey;

    public GitHubProvider(
        ILogger<GitHubProvider> logger,
        GitHubTokenService tokenService,
        IConfiguration configuration)
    {
        _logger = logger;
        _tokenService = tokenService;

        _config = configuration.GetSection("GitHub").Get<GitHubConfig>()
            ?? throw new ArgumentException("GitHub configuration is missing");

        ValidateConfiguration();
        InitializeSigningKey();
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(_config.JwtIssuer))
            throw new ArgumentException("GitHub JwtIssuer is required");
        if (string.IsNullOrEmpty(_config.JwtAudience))
            throw new ArgumentException("GitHub JwtAudience is required");
    }

    private void InitializeSigningKey()
    {
        // Initialize the same signing key used by GitHubTokenService
        if (!string.IsNullOrEmpty(_config.JwtPrivateKeyPem))
        {
            try
            {
                var rsa = RSA.Create();
                rsa.ImportFromPem(_config.JwtPrivateKeyPem);
                _signingKey = new RsaSecurityKey(rsa)
                {
                    KeyId = _config.JwtKeyId ?? "github-key-1"
                };
                _logger.LogInformation("Initialized RSA signing key for GitHub JWT validation");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse RSA private key from PEM");
                throw new ArgumentException("Invalid GitHub JwtPrivateKeyPem format", ex);
            }
        }
        else if (!string.IsNullOrEmpty(_config.JwtHmacSecret))
        {
            _signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config.JwtHmacSecret));
            _logger.LogWarning("Using HMAC secret for GitHub JWT validation. RSA is recommended for production.");
        }
        else
        {
            throw new ArgumentException("Either GitHub JwtPrivateKeyPem or JwtHmacSecret must be configured");
        }
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        // We validate our own JWTs, not external authority
        options.RequireHttpsMetadata = false; // Set to true in production if using HTTPS

        // Configure token validation parameters for self-issued JWTs
        options.TokenValidationParameters = new TokenValidationParameters
        {
            // Issuer validation
            ValidateIssuer = true,
            ValidIssuer = _config.JwtIssuer,

            // Audience validation
            ValidateAudience = true,
            ValidAudience = _config.JwtAudience,

            // Lifetime validation
            ValidateLifetime = true,
            RequireExpirationTime = true,

            // Signing key validation
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _signingKey,
            RequireSignedTokens = true,

            // Clock skew
            ClockSkew = TimeSpan.FromMinutes(5),

            // Claim mappings
            NameClaimType = "sub",
            RoleClaimType = ClaimTypes.Role
        };

        // Add event to enrich identity with roles from database
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    _logger.LogDebug("GitHub JWT validated, enriching with roles");

                    // Extract user ID (GitHub user ID stored as 'sub')
                    var userId = identity.FindFirst(ClaimTypes.NameIdentifier)?.Value
                        ?? identity.FindFirst("sub")?.Value;

                    if (string.IsNullOrEmpty(userId))
                    {
                        _logger.LogWarning("No user ID found in GitHub JWT");
                        return;
                    }

                    // Ensure NameIdentifier claim is present
                    if (identity.FindFirst(ClaimTypes.NameIdentifier) == null && !string.IsNullOrEmpty(userId))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, userId));
                    }

                    // Load roles based on X-Tenant-Id header
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

                        // Deduplicate and add fresh role claims
                        var uniqueRoles = roles.Distinct().ToList();
                        foreach (var role in uniqueRoles)
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Role, role));
                        }

                        _logger.LogDebug("Added {RoleCount} roles for GitHub user {UserId} in tenant {TenantId}",
                            uniqueRoles.Count, userId, tenantId);
                    }
                    else
                    {
                        // No tenant ID header - assign default TenantUser role
                        if (!identity.Claims.Any(c => c.Type == ClaimTypes.Role && c.Value == SystemRoles.TenantUser))
                        {
                            identity.AddClaim(new Claim(ClaimTypes.Role, SystemRoles.TenantUser));
                            _logger.LogDebug("No X-Tenant-Id header, assigned default TenantUser role to {UserId}", userId);
                        }
                    }

                    // Set the User property of HttpContext
                    context.HttpContext.User = context.Principal;
                }
            },

            OnAuthenticationFailed = context =>
            {
                _logger.LogError("GitHub JWT authentication failed: {Exception}", context.Exception?.Message);
                return Task.CompletedTask;
            },

            OnChallenge = context =>
            {
                _logger.LogWarning("GitHub JWT challenge triggered: {Error}, {ErrorDescription}",
                    context.Error, context.ErrorDescription);
                return Task.CompletedTask;
            }
        };
    }

    public Task<(bool success, string? userId)> ValidateToken(string token)
    {
        return _tokenService.ProcessToken(token);
    }

    public Task<UserInfo> GetUserInfo(string userId)
    {
        // For GitHub, user info is embedded in the JWT claims
        // We could optionally call GitHub API here if needed
        _logger.LogInformation("GetUserInfo called for GitHub user {UserId}", userId);

        // Return minimal user info (could be enhanced to call GitHub API)
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
        // GitHub tokens don't contain tenant info by default
        // Tenants are managed in the database
        return Task.FromResult(new List<string>());
    }

    public Task<string> SetNewTenant(string userId, string tenantId)
    {
        // Tenants are managed in the database, not in GitHub
        _logger.LogInformation("SetNewTenant called for GitHub user {UserId}, tenant {TenantId}",
            userId, tenantId);
        return Task.FromResult("Tenant association managed via database");
    }
}

