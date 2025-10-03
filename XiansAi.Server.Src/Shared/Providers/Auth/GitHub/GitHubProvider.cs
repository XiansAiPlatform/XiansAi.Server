using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Shared.Providers.Auth.Auth0;
using Shared.Services;

namespace Shared.Providers.Auth.GitHub;

/// <summary>
/// GitHub authentication provider
/// </summary>
public sealed class GitHubProvider : IAuthProvider
{
    private readonly ILogger<GitHubProvider> _logger;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SymmetricSecurityKey _key;

    public GitHubProvider(ILogger<GitHubProvider> logger, IConfiguration configuration)
    {
        _logger = logger;
        
        _issuer = configuration["GitHubJwt:Issuer"] 
            ?? throw new InvalidOperationException("GitHubJwt:Issuer is missing");
        _audience = configuration["GitHubJwt:Audience"] 
            ?? throw new InvalidOperationException("GitHubJwt:Audience is missing");
        var signingKey = configuration["GitHubJwt:SigningKey"] 
            ?? throw new InvalidOperationException("GitHubJwt:SigningKey is missing");
        
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }

    public void ConfigureJwtBearer(JwtBearerOptions options, IConfiguration configuration)
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = _key,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromMinutes(2),
            NameClaimType = ClaimTypes.Name
        };

        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = ctx =>
            {
                _logger.LogWarning(ctx.Exception, "GitHub JWT authentication failed");
                return Task.CompletedTask;
            },
            OnTokenValidated = async ctx =>
            {
                if (ctx.Principal?.Identity is ClaimsIdentity identity)
                {
                    // Get user roles from database (similar to other providers)
                    var userId = identity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        var tenantId = ctx.HttpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
                        if (!string.IsNullOrEmpty(tenantId))
                        {
                            using var scope = ctx.HttpContext.RequestServices.CreateScope();
                            var roleCacheService = scope.ServiceProvider
                                .GetRequiredService<IRoleCacheService>();

                            var roles = await roleCacheService.GetUserRolesAsync(userId, tenantId);

                            // Remove existing role claims to prevent accumulation
                            var existingRoleClaims = identity.Claims.Where(c => c.Type == ClaimTypes.Role).ToList();
                            foreach (var existingClaim in existingRoleClaims)
                            {
                                identity.RemoveClaim(existingClaim);
                            }

                            // Deduplicate roles before adding claims
                            var uniqueRoles = roles.Distinct().ToList();

                            // Add fresh role claims
                            foreach (var role in uniqueRoles)
                            {
                                identity.AddClaim(new Claim(ClaimTypes.Role, role));
                            }
                        }
                    }

                    // Set the User property of HttpContext
                    ctx.HttpContext.User = ctx.Principal;
                }

                await Task.CompletedTask;
            }
        };
    }

    public async Task<(bool success, string? userId)> ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = _key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            var userId = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
            
            return (userId != null, userId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Token validation failed");
            return (false, null);
        }
    }

    public Task<UserInfo> GetUserInfo(string userId)
    {
        // For GitHub, userId format is "github|{login}"
        var login = userId.StartsWith("github|") ? userId.Substring(7) : userId;
        
        return Task.FromResult(new UserInfo 
        { 
            UserId = userId,
            Nickname = login
        });
    }

    public Task<List<string>> GetUserTenants(string userId)
    {
        // Tenants are managed in the database, not by GitHub
        return Task.FromResult(new List<string>());
    }

    public Task<string> SetNewTenant(string userId, string tenantId)
    {
        // Tenants are managed in the database, not by GitHub
        return Task.FromResult($"Tenant {tenantId} set for user {userId}");
    }
}

