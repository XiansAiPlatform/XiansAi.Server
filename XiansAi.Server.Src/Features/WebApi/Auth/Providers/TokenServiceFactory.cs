using Microsoft.Extensions.DependencyInjection;

namespace Features.WebApi.Auth.Providers.Tokens;

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
            _ => throw new ArgumentException($"Unsupported authentication provider: {providerConfig.Provider}")
        };
    }
} 