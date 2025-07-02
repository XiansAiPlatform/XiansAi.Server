using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.AzureB2C;
using Features.WebApi.Auth.Providers.Keycloak;

namespace Features.WebApi.Auth.Providers;

public interface IAuthProviderFactory
{
    IAuthProvider GetProvider();
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
    
    public IAuthProvider GetProvider()
    {
        var providerConfig = _configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ?? 
            new AuthProviderConfig();
            
        return providerConfig.Provider switch
        {
            AuthProviderType.Auth0 => _serviceProvider.GetRequiredService<Auth0Provider>(),
            AuthProviderType.AzureB2C => _serviceProvider.GetRequiredService<AzureB2CProvider>(),
            AuthProviderType.Keycloak => _serviceProvider.GetRequiredService<KeycloakProvider>(),
            _ => throw new ArgumentException($"Unsupported authentication provider: {providerConfig.Provider}")
        };
    }
} 