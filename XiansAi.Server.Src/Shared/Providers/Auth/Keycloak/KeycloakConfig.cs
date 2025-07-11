namespace Shared.Providers.Auth.Keycloak;

public class KeycloakConfig
{
    public string? Realm { get; set; }
    public string? AuthServerUrl { get; set; }
    public string? ValidIssuer { get; set; }
}
