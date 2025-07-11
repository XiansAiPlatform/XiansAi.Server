using System.IdentityModel.Tokens.Jwt;

namespace Shared.Providers.Auth;

/// <summary>
/// Interface for provider-specific token operations
/// </summary>
public interface ITokenService
{
    /// <summary>
    /// Extract user ID from a token
    /// </summary>
    string? ExtractUserId(JwtSecurityToken token);

    /// <summary>
    /// Extract tenant IDs from a token
    /// </summary>
    IEnumerable<string> ExtractTenantIds(JwtSecurityToken token);

    /// <summary>
    /// Validate and process a JWT token
    /// </summary>
    Task<(bool success, string? userId)> ProcessToken(string token);

    /// <summary>
    /// Get an access token for the management API
    /// </summary>
    Task<string> GetManagementApiToken();
}