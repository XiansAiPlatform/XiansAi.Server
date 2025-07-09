using Features.WebApi.Auth.Providers.Auth0;

namespace Features.WebApi.Auth.Providers.Keycloak;

public class KeycloakConfig
{
    public string? Realm { get; set; }
    public string? AuthServerUrl { get; set; }
    public ManagementApiConfig? ManagementApi { get; set; }
    public string? ValidIssuer { get; set; }
}
