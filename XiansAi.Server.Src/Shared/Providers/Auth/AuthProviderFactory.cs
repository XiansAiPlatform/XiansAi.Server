using Shared.Providers.Auth.Auth0;
using Shared.Providers.Auth.AzureB2C;
using Shared.Providers.Auth.Keycloak;

namespace Shared.Providers.Auth;

public interface IAuthProviderFactory
{
    IAuthProvider GetProvider(ApiType api = ApiType.WebApi);
}

public class AuthProviderFactory : IAuthProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;

    public AuthProviderFactory(IServiceProvider serviceProvider, IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
    }

    public IAuthProvider GetProvider(ApiType api = ApiType.WebApi)
    {
        //var providerConfig = _configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ??
        //    new AuthProviderConfig();
        string sectionName = api == ApiType.UserApi ? "UserApi" : "WebApi";
        var section = _configuration.GetSection(sectionName);
        var providerConfig = section.GetSection("AuthProvider").Get<AuthProviderConfig>() ?? new AuthProviderConfig();

        //return providerConfig.Provider switch
        //{
        //    AuthProviderType.Auth0 => _serviceProvider.GetRequiredService<Auth0Provider>(),
        //    AuthProviderType.AzureB2C => _serviceProvider.GetRequiredService<AzureB2CProvider>(),
        //    AuthProviderType.Keycloak => _serviceProvider.GetRequiredService<KeycloakProvider>(),
        //    _ => throw new ArgumentException($"Unsupported authentication provider: {providerConfig.Provider}")
        //};

        return providerConfig.Provider switch
        {
            AuthProviderType.Auth0 => ActivatorUtilities.CreateInstance<Auth0Provider>(_serviceProvider, _configuration, api),
            AuthProviderType.AzureB2C => ActivatorUtilities.CreateInstance<AzureB2CProvider>(_serviceProvider, _configuration, api),
            AuthProviderType.Keycloak => ActivatorUtilities.CreateInstance<KeycloakProvider>(_serviceProvider, _configuration, api),
            _ => throw new ArgumentException($"Unsupported authentication provider: {providerConfig.Provider}")
        };
    }
}