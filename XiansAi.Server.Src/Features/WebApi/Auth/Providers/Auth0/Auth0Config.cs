using System.Text.Json.Serialization;

namespace Features.WebApi.Auth.Providers.Auth0;

public class Auth0Config
{
    public string? Domain { get; set; }
    public string? Audience { get; set; }
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
    public ManagementApiConfig? ManagementApi { get; set; }
}

public class ManagementApiConfig
{
    public string? ClientId { get; set; }
    public string? ClientSecret { get; set; }
}

public class AppMetadata
{
    [JsonPropertyName("tenants")]
    public required string[] Tenants { get; set; } = [];
}

public class TokenResponse
{
    [JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }
}

public class UserInfo
{
    [JsonPropertyName("nickname")]
    public string? Nickname { get; set; }
    
    [JsonPropertyName("user_id")]
    public string? UserId { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("picture")]
    public string? Picture { get; set; }

    [JsonPropertyName("app_metadata")]
    public AppMetadata? AppMetadata { get; set; }

    [JsonPropertyName("last_login")]
    public DateTime LastLogin { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("last_ip")]
    public string? LastIp { get; set; }
} 