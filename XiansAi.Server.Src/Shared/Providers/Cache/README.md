# Cache Provider Pattern

## Overview

The Cache Provider Pattern in XiansAi.Server provides a flexible, priority-based caching system that automatically selects the best available cache provider. The system gracefully falls back from high-performance distributed caching (Redis) to in-memory caching when Redis is unavailable.

## Architecture

### Core Components

``` text
Shared/Providers/Cache/
├── ICacheProvider.cs              # Main cache abstraction
├── ICacheProviderRegistration.cs  # Self-registration interface
├── RedisCacheProvider.cs          # Redis implementation
├── InMemoryCacheProvider.cs       # In-memory implementation
└── CacheProviderFactory.cs        # Factory with priority-based selection
```

### 1. Cache Provider Interface

```csharp
public interface ICacheProvider
{
    Task<T?> GetAsync<T>(string key);
    Task SetAsync<T>(string key, T value, TimeSpan? expiration = null);
    Task RemoveAsync(string key);
    Task<bool> ExistsAsync(string key);
}
```

### 2. Provider Registration Interface

```csharp
public interface ICacheProviderRegistration
{
    static abstract string ProviderName { get; }
    static abstract int Priority { get; }
    static abstract bool CanRegister(IConfiguration configuration);
    static abstract void RegisterServices(IServiceCollection services, IConfiguration configuration);
}
```

### 3. Provider Implementations

#### Redis Provider (Priority: 1)

- **High Priority**: Preferred when available
- **Distributed**: Shared across multiple application instances
- **Persistent**: Survives application restarts
- **Configuration Required**: Redis connection string

#### In-Memory Provider (Priority: 100)

- **Fallback**: Used when Redis is unavailable
- **Single Instance**: Not shared across instances
- **Volatile**: Lost on application restart
- **Always Available**: No external dependencies

## Priority System

### How Priorities Work

- **Lower numbers = Higher priority**
- **Registration**: Providers register in priority order
- **Creation**: Factory tries providers in priority order until one succeeds

### Current Priorities

| Provider | Priority | Use Case |
|----------|----------|----------|
| Redis | 1 | Production, distributed scenarios |
| InMemory | 100 | Development, fallback, single-instance |

### Priority Ranges (Convention)

- **1-10**: High-priority external services (Redis, Memcached)
- **50-99**: Medium-priority services (specialized caches)
- **100+**: Fallback services (InMemory, NullCache)

## Configuration

### Redis Configuration

```json
{
  "RedisCache": {
    "ConnectionString": "localhost:6379"
  }
}
```

### No Configuration Required for InMemory

The in-memory provider is always available as a fallback.

## Usage

### Application Startup

```csharp
// In Program.cs or Startup.cs
CacheProviderFactory.RegisterProviders(services, configuration);
services.AddSingleton<ICacheProviderFactory, CacheProviderFactory>();
```

### Service Usage

```csharp
public class MyService
{
    private readonly ICacheProvider _cache;

    public MyService(ICacheProviderFactory factory)
    {
        _cache = factory.CreateCacheProvider();
    }

    public async Task<string> GetDataAsync(string key)
    {
        // Same API regardless of underlying provider
        var cached = await _cache.GetAsync<string>(key);
        if (cached != null) return cached;

        var data = await LoadDataFromDatabase(key);
        await _cache.SetAsync(key, data, TimeSpan.FromMinutes(15));
        return data;
    }
}
```

## Adding New Providers

### Step 1: Implement the Provider

```csharp
public class MemcachedProvider : ICacheProvider, ICacheProviderRegistration
{
    public static string ProviderName => "Memcached";
    public static int Priority => 50; // Between Redis and InMemory

    public static bool CanRegister(IConfiguration configuration)
    {
        return !string.IsNullOrEmpty(configuration.GetConnectionString("Memcached"));
    }

    public static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddMemcached(options => {
            options.Servers = configuration.GetConnectionString("Memcached");
        });
    }

    // Implement ICacheProvider methods...
}
```

### Step 2: Add to Factory

```csharp
private static readonly CacheProviderDefinition[] Providers = new[]
{
    // Existing providers...
    new CacheProviderDefinition
    {
        Name = MemcachedProvider.ProviderName,
        Priority = MemcachedProvider.Priority,
        CanRegister = MemcachedProvider.CanRegister,
        RegisterServices = MemcachedProvider.RegisterServices,
        CreateProvider = serviceProvider => {
            var memcached = serviceProvider.GetService<IMemcachedClient>();
            if (memcached != null)
            {
                var logger = serviceProvider.GetRequiredService<ILogger<MemcachedProvider>>();
                return new MemcachedProvider(memcached, logger);
            }
            return null;
        }
    }
};
```

### Step 3: Add Configuration

```json
{
  "ConnectionStrings": {
    "Memcached": "localhost:11211"
  }
}
```

**Result**: Memcached will automatically be tried after Redis but before InMemory.
