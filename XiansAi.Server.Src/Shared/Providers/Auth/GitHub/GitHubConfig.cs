using System.Text.Json.Serialization;

namespace Shared.Providers.Auth.GitHub;

/// <summary>
/// Configuration for GitHub OAuth provider
/// </summary>
public class GitHubConfig
{
    /// <summary>
    /// GitHub OAuth App Client ID
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// GitHub OAuth App Client Secret
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// OAuth redirect URI (must match GitHub OAuth app settings)
    /// </summary>
    public string? RedirectUri { get; set; }

    /// <summary>
    /// OAuth scopes (e.g., "read:user user:email")
    /// </summary>
    public string Scopes { get; set; } = "read:user user:email";

    /// <summary>
    /// Issuer for JWTs minted by this server
    /// </summary>
    public string? JwtIssuer { get; set; }

    /// <summary>
    /// Audience for JWTs minted by this server
    /// </summary>
    public string? JwtAudience { get; set; }

    /// <summary>
    /// JWT access token lifetime in minutes
    /// </summary>
    public int JwtAccessTokenMinutes { get; set; } = 60;

    /// <summary>
    /// RSA private key in PEM format for asymmetric signing (recommended)
    /// </summary>
    public string? JwtPrivateKeyPem { get; set; }

    /// <summary>
    /// Key ID for JWT header (optional, for key rotation)
    /// </summary>
    public string? JwtKeyId { get; set; }

    /// <summary>
    /// HMAC secret for symmetric signing (dev-only, not recommended for production)
    /// </summary>
    public string? JwtHmacSecret { get; set; }
}

/// <summary>
/// GitHub OAuth token exchange response
/// </summary>
public class GitHubTokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

/// <summary>
/// GitHub user profile from /user API
/// </summary>
public class GitHubUser
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("login")]
    public string? Login { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("avatar_url")]
    public string? AvatarUrl { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// GitHub user email from /user/emails API
/// </summary>
public class GitHubEmail
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }

    [JsonPropertyName("verified")]
    public bool Verified { get; set; }

    [JsonPropertyName("visibility")]
    public string? Visibility { get; set; }
}

