# Cache Provider Pattern

## Overview

The Cache Provider Pattern in XiansAi.Server provides a simple, configuration-based caching system that selects the appropriate cache provider based on application settings. The system supports Redis for distributed caching and in-memory caching for development or single-instance scenarios.

## Architecture

### Core Components

```text
Shared/Providers/Cache/
├── ICacheProvider.cs              # Main cache abstraction
├── RedisCacheProvider.cs          # Redis implementation
├── InMemoryCacheProvider.cs       # In-memory implementation
├── CacheProviderFactory.cs        # Simple factory with configuration-based selection
├── ICacheInvalidationBus.cs       # Cross-replica invalidation publish API
├── RedisCacheInvalidationBus.cs   # Redis pub/sub transport (redis provider only)
├── NoOpCacheInvalidationBus.cs    # No-op when provider is memory
├── ICacheInvalidationApplicator.cs
└── CacheInvalidationApplicator.cs # Applies invalidation envelopes to local L1 caches

Shared/Services/
├── IPendingRequestCoordinator.cs
├── RedisPendingRequestCoordinator.cs  # Sync /converse coordination (redis provider only)
└── NoOpPendingRequestCoordinator.cs   # In-process only (memory provider)
```

### 1. Cache Provider Interface

```csharp
public interface ICacheProvider
{
    Task<T?> GetAsync<T>(string key);
    Task<bool> SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null);
    Task<bool> RemoveAsync(string key);
}
```

### 2. Provider Implementations

#### Redis Provider

- **Distributed**: Shared across multiple application instances
- **Persistent**: Survives application restarts (depending on Redis configuration)
- **Production Ready**: Suitable for production environments
- **Configuration Required**: Redis connection string

#### In-Memory Provider

- **Single Instance**: Not shared across instances
- **Volatile**: Lost on application restart
- **Development/Testing**: Useful for development and testing scenarios
- **No Dependencies**: Always available

### 3. Cross-Replica Invalidation (`ICacheInvalidationBus`)

When `Cache:Provider=redis`, services publish `CacheInvalidationEnvelope` messages through `ICacheInvalidationBus` whenever auth or messaging L1 caches should be cleared (user disable, API key revoke, tenant change, activation deactivate, thread origin updates, and similar). `RedisCacheInvalidationBus` sends envelopes over Redis pub/sub (`xians:cache:invalidate`); each replica subscribes and forwards received envelopes to `ICacheInvalidationApplicator`, which evicts the matching keys from that instance's in-memory caches.

With `Cache:Provider=memory`, `NoOpCacheInvalidationBus` is registered — publishes are ignored and invalidation stays local to the process.

### 4. Pending Request Coordination (`IPendingRequestCoordinator`)

Synchronous `/converse` flows use `IPendingRequestCoordinator` so a waiter on one replica can receive a completion handled on another. When Redis is configured, `RedisPendingRequestCoordinator` stores the response in a short-lived Redis key and signals completion over pub/sub (`xians:pending:complete`). With the memory provider, `NoOpPendingRequestCoordinator` keeps coordination in-process only.

**Multiple server replicas require `Cache:Provider=redis`.** Without it, auth/messaging caches can stay stale until TTL on other instances, and `/converse` may time out when the waiter and completer hit different replicas.

## Configuration

### Cache Provider Configuration

```json
{
  "Cache": {
    "Provider": "redis", // or "memory"/"inmemory"
    "Redis": {
      "ConnectionString": "localhost:6379"
    }
  }
}
```

### Configuration Options

| Provider | Configuration Key | Required Settings |
|----------|------------------|-------------------|
| Redis | `"redis"` | `Cache:Redis:ConnectionString` |
| In-Memory | `"memory"` or `"inmemory"` | None |

### Redis Configuration Examples

#### Local Development

```json
{
  "Cache": {
    "Provider": "redis",
    "Redis": {
      "ConnectionString": "localhost:6379"
    }
  }
}
```

#### Production with Authentication

```json
{
  "Cache": {
    "Provider": "redis",
    "Redis": {
      "ConnectionString": "your-redis-server:6379,password=your-password,ssl=true"
    }
  }
}
```

#### Azure Redis Cache

```json
{
  "Cache": {
    "Provider": "redis",
    "Redis": {
      "ConnectionString": "your-cache-name.redis.cache.windows.net:6380,password=your-access-key,ssl=True,abortConnect=False"
    }
  }
}
```

### In-Memory Configuration

```json
{
  "Cache": {
    "Provider": "memory"
  }
}
```

## Usage

### Application Startup

```csharp
// In Program.cs or Startup.cs
// Cache providers are automatically registered when you call:
services.AddInfrastructureServices(configuration);

// This automatically handles:
// - CacheProviderFactory.RegisterProvider(services, configuration);
```

### Service Usage

```csharp
public class MyService
{
    private readonly ICacheProvider _cache;

    public MyService(ICacheProvider cache)
    {
        _cache = cache;
    }

    public async Task<string> GetDataAsync(string key)
    {
        // Try to get from cache first
        var cached = await _cache.GetAsync<string>(key);
        if (cached != null) 
            return cached;

        // Load from database if not in cache
        var data = await LoadDataFromDatabase(key);
        
        // Cache for 15 minutes
        await _cache.SetAsync(key, data, TimeSpan.FromMinutes(15));
        
        return data;
    }

    public async Task<UserProfile> GetUserProfileAsync(int userId)
    {
        var cacheKey = $"user_profile_{userId}";
        
        // Check cache first
        var profile = await _cache.GetAsync<UserProfile>(cacheKey);
        if (profile != null)
            return profile;

        // Load from database
        profile = await LoadUserProfileFromDatabase(userId);
        
        // Cache with sliding expiration (extends on access)
        await _cache.SetAsync(cacheKey, profile, 
            absoluteExpiration: TimeSpan.FromHours(1),
            slidingExpiration: TimeSpan.FromMinutes(20));
        
        return profile;
    }

    public async Task InvalidateUserCacheAsync(int userId)
    {
        var cacheKey = $"user_profile_{userId}";
        await _cache.RemoveAsync(cacheKey);
    }
}
```

### Using the Factory (Advanced)

```csharp
public class AdvancedCacheService
{
    private readonly ICacheProvider _cache;

    public AdvancedCacheService(ICacheProviderFactory factory)
    {
        _cache = factory.CreateCacheProvider();
    }

    public async Task<bool> TrySetCacheAsync<T>(string key, T value, TimeSpan expiration)
    {
        // Direct cache usage with success/failure handling
        return await _cache.SetAsync(key, value, expiration);
    }
}
```

## Adding New Providers

### Step 1: Implement the Provider

```csharp
public class MemcachedProvider : ICacheProvider
{
    private readonly IMemcachedClient _client;
    private readonly ILogger<MemcachedProvider> _logger;

    public MemcachedProvider(IMemcachedClient client, ILogger<MemcachedProvider> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<T?> GetAsync<T>(string key)
    {
        // Implement Memcached get logic
        // ...
    }

    public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? absoluteExpiration = null, TimeSpan? slidingExpiration = null)
    {
        // Implement Memcached set logic
        // ...
    }

    public async Task<bool> RemoveAsync(string key)
    {
        // Implement Memcached remove logic
        // ...
    }
}
```

### Step 2: Add to Factory

```csharp
// In CacheProviderFactory.RegisterProvider method, add a new case:
case "memcached":
    var servers = configuration["Cache:Memcached:Servers"];
    if (string.IsNullOrEmpty(servers))
    {
        throw new InvalidOperationException("Memcached cache provider requires Cache:Memcached:Servers");
    }
    services.AddMemcached(options => options.Servers = servers);
    services.AddScoped<ICacheProvider, MemcachedProvider>();
    break;
```

### Step 3: Add Configuration

```json
{
  "Cache": {
    "Provider": "memcached",
    "Memcached": {
      "Servers": "localhost:11211"
    }
  }
}
```

## Cache Expiration Strategies

### Absolute Expiration
```csharp
// Cache expires after exactly 1 hour, regardless of access
await _cache.SetAsync("key", value, TimeSpan.FromHours(1));
```

### Sliding Expiration
```csharp
// Cache expires 30 minutes after last access
await _cache.SetAsync("key", value, 
    absoluteExpiration: null, 
    slidingExpiration: TimeSpan.FromMinutes(30));
```

### Combined Expiration
```csharp
// Cache expires after 2 hours OR 30 minutes of inactivity, whichever comes first
await _cache.SetAsync("key", value, 
    absoluteExpiration: TimeSpan.FromHours(2), 
    slidingExpiration: TimeSpan.FromMinutes(30));
```

## Best Practices

1. **Use Meaningful Keys**: Include context in cache keys (e.g., `"user_profile_123"`, `"product_details_456"`)

2. **Set Appropriate Expiration**: Balance between performance and data freshness

3. **Handle Cache Misses Gracefully**: Always have a fallback to load data from the source

4. **Consider Cache Invalidation**: Remove or update cached data when the underlying data changes

5. **Monitor Cache Performance**: Log cache hit/miss ratios to optimize caching strategy

6. **Use Consistent Serialization**: The providers use JSON serialization by default
