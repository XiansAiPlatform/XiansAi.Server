using Microsoft.Extensions.DependencyInjection;
using Shared.Providers.Auth.Auth0;
using Shared.Providers.Auth.AzureB2C;
using Shared.Providers.Auth.Keycloak;

namespace Shared.Providers.Auth;



public interface ITokenServiceFactory
{
    ITokenService GetTokenService(ApiType api = ApiType.WebApi);
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

    public ITokenService GetTokenService(ApiType api = ApiType.WebApi)
    {
        // Load the correct config section based on api
        string sectionName = api == ApiType.UserApi ? "UserApi" : "WebApi";
        var section = _configuration.GetSection(sectionName);
        var providerConfig = section.GetSection("AuthProvider").Get<AuthProviderConfig>() ?? new AuthProviderConfig();
        // Pass the api type to the token service constructor
        return providerConfig.Provider switch
        {
            AuthProviderType.Auth0 => ActivatorUtilities.CreateInstance<Auth0TokenService>(_serviceProvider, _configuration, api),
            AuthProviderType.AzureB2C => ActivatorUtilities.CreateInstance<AzureB2CTokenService>(_serviceProvider, _configuration, api),
            AuthProviderType.Keycloak => ActivatorUtilities.CreateInstance<KeycloakTokenService>(_serviceProvider, _configuration, api),
            _ => throw new ArgumentException($"Unsupported authentication provider: {providerConfig.Provider}")
        };
    }
}