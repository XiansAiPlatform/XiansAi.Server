using Shared.Providers.Auth.Auth0;
using Shared.Providers.Auth.AzureB2C;
using Shared.Providers.Auth.Keycloak;
using Shared.Providers.Auth.Oidc;

namespace Shared.Providers.Auth;

public interface ITokenServiceFactory
{
    ITokenService GetTokenService();
}

public class TokenServiceFactory : ITokenServiceFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public TokenServiceFactory(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public ITokenService GetTokenService()
    {
        var providerConfig = _configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ??
            new AuthProviderConfig();

        return providerConfig.Provider switch
        {
            AuthProviderType.Auth0 => _serviceProvider.GetRequiredService<Auth0TokenService>(),
            AuthProviderType.AzureB2C => _serviceProvider.GetRequiredService<AzureB2CTokenService>(),
            AuthProviderType.Keycloak => _serviceProvider.GetRequiredService<KeycloakTokenService>(),
            AuthProviderType.Oidc => _serviceProvider.GetRequiredService<OidcTokenService>(),
            _ => throw new ArgumentException($"Unsupported authentication provider: {providerConfig.Provider}")
        };
    }
}