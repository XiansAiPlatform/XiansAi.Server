using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Services.Web;
using System.Text.Json;

namespace XiansAi.Server.Services.Lib;

public class ObjectCacheEndpoint
{
    private readonly ObjectCacheService _objectCacheService;
    private readonly ILogger<ObjectCacheEndpoint> _logger;

    public ObjectCacheEndpoint(
        ObjectCacheService objectCacheService,
        ILogger<ObjectCacheEndpoint> logger)
    {
        _objectCacheService = objectCacheService;
        _logger = logger;
    }

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

    public async Task<IResult> SetValue(string key, [FromBody] JsonElement value)
    {
        _logger.LogInformation("Setting value for key: {Key}, Value: {Value}", key, value.ToString());
        
        if (value.ValueKind == JsonValueKind.Null)
        {
            _logger.LogWarning("Attempted to set null value for key: {Key}", key);
            return Results.BadRequest("Value cannot be null");
        }

        var success = await _objectCacheService.SetAsync(key, value);
        if (!success)
        {
            _logger.LogError("Failed to set value for key: {Key}, Value: {Value}", key, value.ToString());
            return Results.Json(new { message = "Failed to set value in cache" }, statusCode: 500);
        }

        _logger.LogInformation("Successfully set value for key: {Key}, Value: {Value}", key, value.ToString());
        return Results.Ok(new { message = "Value set successfully" });
    }

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