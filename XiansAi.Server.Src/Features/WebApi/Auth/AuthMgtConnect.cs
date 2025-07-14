using Features.WebApi.Auth.Providers;
using Features.WebApi.Auth.Providers.Auth0;

namespace Features.WebApi.Auth;

public interface IAuthMgtConnect    
{
    /// <summary>
    /// Gets user information from the authentication provider
    /// </summary>
    Task<UserInfo> GetUserInfo(string userId);

    /// <summary>
    /// Gets user tenant information
    /// </summary>
    /// <param name="userId">The user id</param>
    /// <returns>The user tenant information</returns>
    Task<List<string>> GetUserTenants(string userId);

    /// <summary>
    /// Adds a new tenant to the user
    /// <param name="userId">The user id</param>
    /// <param name="tenantId">The tenant id</param>
    /// </summary>
    Task<string> SetNewTenant(string userId, string tenantId);
}

public class AuthMgtConnect : IAuthMgtConnect
{
    private readonly IAuthProviderFactory _authProviderFactory;
    private readonly ILogger<AuthMgtConnect> _logger;

    public AuthMgtConnect(
        IAuthProviderFactory authProviderFactory,
        ILogger<AuthMgtConnect> logger)
    {
        _authProviderFactory = authProviderFactory;
        _logger = logger;
    }

    public async Task<UserInfo> GetUserInfo(string userId)
    {
        try
        {
            var provider = _authProviderFactory.GetProvider();
            return await provider.GetUserInfo(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user info for userId: {UserId}", userId);
            throw;
        }
    }

    public async Task<List<string>> GetUserTenants(string userId)
    {
        try
        {
            var provider = _authProviderFactory.GetProvider();
            return await provider.GetUserTenants(userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user tenant for user {UserId}", userId);
            throw;
        }
    }

    public async Task<string> SetNewTenant(string userId, string tenantId)
    {
        try
        {
            var provider = _authProviderFactory.GetProvider();
            return await provider.SetNewTenant(userId, tenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set new tenant {TenantId} for user {UserId}", tenantId, userId);
            throw;
        }
    }
}