using XiansAi.Server.Utils;

namespace Features.WebApi.Services;

public interface IAuthorizationCacheService
{
    Task<string> CacheAuthorization(string token);
    Task<string?> GetAuthorization(string guid);
}

public class AuthorizationCacheService : IAuthorizationCacheService
{
    private readonly ObjectCache _cache;
    private readonly ILogger<AuthorizationCacheService> _logger;

    public AuthorizationCacheService(
        ObjectCache cache,
        ILogger<AuthorizationCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<string> CacheAuthorization(string authorization)
    {
        var guid = Guid.NewGuid().ToString();
        var success = await _cache.SetAsync(guid, authorization, TimeSpan.FromMinutes(60));
        if (!success)
        {
            throw new Exception("Failed to cache authorization");
        }

        return guid;
    }

    public async Task<string?> GetAuthorization(string guid)
    {
        var authorization = await _cache.GetAsync<string>(guid);
        if (authorization == null)
        {
            _logger.LogWarning("No authorization found for guid: {Guid}", guid);
            return null;
        }

        _logger.LogInformation("Retrieved authorization: {Authorization}", authorization);
        return authorization;
    }
} 