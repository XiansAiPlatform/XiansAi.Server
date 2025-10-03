using System.Text.Json.Serialization;

namespace Shared.Providers.Auth.GitHub;

/// <summary>
/// Configuration for GitHub OAuth
/// </summary>
public class GitHubOAuthConfig
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public string? Scope { get; set; } = "read:user user:email";
}

/// <summary>
/// Configuration for GitHub JWT signing
/// </summary>
public class GitHubJwtConfig
{
    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? SigningKey { get; set; }
}

/// <summary>
/// GitHub OAuth token response
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
/// GitHub user information
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
}

/// <summary>
/// GitHub callback request DTO
/// </summary>
public record GitHubCallbackDto(string Code, string? RedirectUri);

