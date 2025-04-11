using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Utils;
using System.Text.Json;

namespace Features.AgentApi.Services.Lib;

/// <summary>
/// Options for cache entry expiration settings
/// </summary>
public class CacheOptions
{
    /// <summary>
    /// Absolute expiration time in minutes after which the cache entry will expire.
    /// Default is 24 hours (1440 minutes) if neither expiration type is specified.
    /// </summary>
    public int? RelativeExpirationMinutes { get; set; }
    
    /// <summary>
    /// Sliding expiration time in minutes. The cache entry will expire if not accessed for this amount of time.
    /// No default value.
    /// </summary>
    public int? SlidingExpirationMinutes { get; set; }
}

public interface IObjectCacheWrapperService
{
    Task<IResult> GetValue(string key);
    Task<IResult> SetValue(string key, JsonElement value, CacheOptions? options = null);
    Task<IResult> DeleteValue(string key);
}

/// <summary>
/// Wrapper service for cache operations accessible via API endpoints
/// </summary>
public class ObjectCacheWrapperService : IObjectCacheWrapperService
{
    private readonly ObjectCacheService _objectCacheService;
    private readonly ILogger<ObjectCacheWrapperService> _logger;

    /// <summary>
    /// Creates a new instance of the ObjectCacheWrapperService
    /// </summary>
    /// <param name="objectCacheService">The underlying cache service</param>
    /// <param name="logger">Logger for the service</param>
    public ObjectCacheWrapperService(
        ObjectCacheService objectCacheService,
        ILogger<ObjectCacheWrapperService> logger)
    {
        _objectCacheService = objectCacheService;
        _logger = logger;
    }

    /// <summary>
    /// Gets a value from cache by key
    /// </summary>
    /// <param name="key">The cache key to retrieve</param>
    /// <returns>The cached value or NotFound if not found</returns>
    public async Task<IResult> GetValue(string key)
    {
        _logger.LogInformation("Getting value for key: {Key}", key);
        var value = await _objectCacheService.GetAsync<object>(key);
        if (value == null)
        {
            _logger.LogWarning("No value found for key: {Key}", key);
            return Results.NotFound($"No value found for key: {key}");
        }

        _logger.LogInformation("Retrieved value for key {Key}: {Value}", key, JsonSerializer.Serialize(value));
        return Results.Ok(value);
    }

    /// <summary>
    /// Sets a value in cache with optional expiration settings
    /// </summary>
    /// <param name="key">The cache key</param>
    /// <param name="value">The value to cache</param>
    /// <param name="options">Optional expiration settings. 
    /// If not provided, default expiration of 24 hours will be used.</param>
    /// <returns>Success or error result</returns>
    public async Task<IResult> SetValue(string key, [FromBody] JsonElement value, CacheOptions? options = null)
    {
        _logger.LogInformation("Setting value for key: {Key}, Value: {Value}", key, value.ToString());
        
        if (value.ValueKind == JsonValueKind.Null)
        {
            _logger.LogWarning("Attempted to set null value for key: {Key}", key);
            return Results.BadRequest("Value cannot be null");
        }

        TimeSpan? relativeExpiration = null;
        if (options?.RelativeExpirationMinutes != null)
        {
            relativeExpiration = TimeSpan.FromMinutes(options.RelativeExpirationMinutes.Value);
        }
            
        TimeSpan? slidingExpiration = null;
        if (options?.SlidingExpirationMinutes != null)
        {
            slidingExpiration = TimeSpan.FromMinutes(options.SlidingExpirationMinutes.Value);
        }

        var success = await _objectCacheService.SetAsync(key, value, relativeExpiration, slidingExpiration);
        if (!success)
        {
            _logger.LogError("Failed to set value for key: {Key}, Value: {Value}", key, value.ToString());
            return Results.Json(new { message = "Failed to set value in cache" }, statusCode: 500);
        }

        _logger.LogInformation("Successfully set value for key: {Key}, Value: {Value}", key, value.ToString());
        return Results.Ok(new { message = "Value set successfully" });
    }

    /// <summary>
    /// Deletes a value from cache by key
    /// </summary>
    /// <param name="key">The cache key to delete</param>
    /// <returns>Success or error result</returns>
    public async Task<IResult> DeleteValue(string key)
    {
        _logger.LogInformation("Deleting value for key: {Key}", key);
        var success = await _objectCacheService.RemoveAsync(key);
        if (!success)
        {
            _logger.LogError("Failed to delete value for key: {Key}", key);
            return Results.Json(new { message = "Failed to delete value from cache" }, statusCode: 500);
        }

        _logger.LogInformation("Successfully deleted value for key: {Key}", key);
        return Results.Ok(new { message = "Value deleted successfully" });
    }
} 