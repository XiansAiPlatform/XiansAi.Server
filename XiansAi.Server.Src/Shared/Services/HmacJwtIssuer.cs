using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Shared.Services;

/// <summary>
/// Interface for JWT issuer service
/// </summary>
public interface IJwtIssuer
{
    string Issue(IEnumerable<Claim> claims, TimeSpan? ttl = null);
}

/// <summary>
/// HMAC-based JWT issuer for GitHub authentication
/// </summary>
public sealed class HmacJwtIssuer : IJwtIssuer
{
    private readonly string _issuer;
    private readonly string _audience;
    private readonly SymmetricSecurityKey _key;

    public HmacJwtIssuer(IConfiguration configuration)
    {
        _issuer = configuration["GitHubJwt:Issuer"] 
            ?? throw new InvalidOperationException("GitHubJwt:Issuer is required");
        _audience = configuration["GitHubJwt:Audience"] 
            ?? throw new InvalidOperationException("GitHubJwt:Audience is required");
        var signingKey = configuration["GitHubJwt:SigningKey"] 
            ?? throw new InvalidOperationException("GitHubJwt:SigningKey is required");
        
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
    }

    public string Issue(IEnumerable<Claim> claims, TimeSpan? ttl = null)
    {
        var credentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        
        var jwt = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: now.Add(ttl ?? TimeSpan.FromHours(8)),
            signingCredentials: credentials
        );

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}

