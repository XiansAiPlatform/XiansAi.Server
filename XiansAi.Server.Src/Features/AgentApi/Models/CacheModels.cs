using System.Text.Json;

namespace Features.AgentApi.Endpoints.Models;

// Request models for cache operations
public class CacheKeyRequest
{
    public string Key { get; set; } = string.Empty;
}

public class CacheSetRequest
{
    public string Key { get; set; } = string.Empty;
    public JsonElement Value { get; set; }
    public int? RelativeExpirationMinutes { get; set; }
    public int? SlidingExpirationMinutes { get; set; }
} 