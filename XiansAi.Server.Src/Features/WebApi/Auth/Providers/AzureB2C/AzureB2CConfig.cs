using System.Text.Json.Serialization;
using Features.WebApi.Auth.Providers.Auth0;

namespace Features.WebApi.Auth.Providers.AzureB2C;

public class AzureB2CConfig
{
    public string? TenantId { get; set; }
    public string? Domain { get; set; }
    public string? Policy { get; set; }
    public string? Audience { get; set; }
    public ManagementApiConfig? ManagementApi { get; set; }
}

public class AzureB2CUserInfo
{
    [JsonPropertyName("id")]
    public string? UserId { get; set; }
    
    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }
    
    [JsonPropertyName("identities")]
    public List<AzureB2CIdentity>? Identities { get; set; }
    
    [JsonPropertyName("extension_tenants")]
    public string[]? Tenants { get; set; } = [];
}

public class AzureB2CIdentity
{
    [JsonPropertyName("signInType")]
    public string? SignInType { get; set; }
    
    [JsonPropertyName("issuer")]
    public string? Issuer { get; set; }
    
    [JsonPropertyName("issuerAssignedId")]
    public string? IssuerAssignedId { get; set; }
} 