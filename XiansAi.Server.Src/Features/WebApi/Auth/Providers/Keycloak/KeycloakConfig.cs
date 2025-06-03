using Features.WebApi.Auth.Providers.Auth0;

namespace Features.WebApi.Auth.Providers.Keycloak;

public class KeycloakConfig
{
    public string? Realm { get; set; }
    public string? AuthServerUrl { get; set; }
    public string? Resource { get; set; }
    public string? Secret { get; set; }
    public string? Audience { get; set; }
    public ManagementApiConfig? ManagementApi { get; set; }
}
