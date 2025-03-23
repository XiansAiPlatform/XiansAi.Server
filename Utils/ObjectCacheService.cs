using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;

namespace XiansAi.Server.Utils;

public class ObjectCacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<ObjectCacheService> _logger;
    private readonly DistributedCacheEntryOptions _cacheOptions;

    public ObjectCacheService(
        IDistributedCache cache,
        ILogger<ObjectCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
        _cacheOptions = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24)
        };
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        try
        {
            var value = await _cache.GetStringAsync(key);
            if (string.IsNullOrEmpty(value))
                return default;

            return JsonSerializer.Deserialize<T>(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving value from cache for key: {Key}", key);
            return default;
        }
    }

    public async Task<bool> SetAsync<T>(string key, T value)
    {
        try
        {
            var serializedValue = JsonSerializer.Serialize(value);
            await _cache.SetStringAsync(key, serializedValue, _cacheOptions);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting value in cache for key: {Key}", key);
            return false;
        }
    }

    public async Task<bool> RemoveAsync(string key)
    {
        try
        {
            await _cache.RemoveAsync(key);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing value from cache for key: {Key}", key);
            return false;
        }
    }
} 