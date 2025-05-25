using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;

namespace XiansAi.Server.Providers;

/// <summary>
/// Factory for creating cache providers based on configuration
/// </summary>
public interface ICacheProviderFactory
{
    /// <summary>
    /// Creates a cache provider based on the current configuration
    /// </summary>
    /// <returns>The appropriate cache provider implementation</returns>
    ICacheProvider CreateCacheProvider();

}

/// <summary>
/// Provider definition for cache providers
/// </summary>
public class CacheProviderDefinition
{
    public required string Name { get; init; }
    public required int Priority { get; init; }
    public required Func<IConfiguration, bool> CanRegister { get; init; }
    public required Action<IServiceCollection, IConfiguration> RegisterServices { get; init; }
    public required Func<IServiceProvider, ICacheProvider?> CreateProvider { get; init; }
}

/// <summary>
/// Implementation of cache provider factory that also handles service registration
/// </summary>
public class CacheProviderFactory : ICacheProviderFactory
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CacheProviderFactory> _logger;

    /// <summary>
    /// All known cache providers in a single definition
    /// </summary>
    private static readonly CacheProviderDefinition[] Providers = new[]
    {
        new CacheProviderDefinition
        {
            Name = RedisCacheProvider.ProviderName,
            Priority = RedisCacheProvider.Priority,
            CanRegister = RedisCacheProvider.CanRegister,
            RegisterServices = RedisCacheProvider.RegisterServices,
            CreateProvider = serviceProvider =>
            {
                var distributedCache = serviceProvider.GetService<IDistributedCache>();
                if (distributedCache != null)
                {
                    var logger = serviceProvider.GetRequiredService<ILogger<RedisCacheProvider>>();
                    return new RedisCacheProvider(distributedCache, logger);
                }
                return null;
            }
        },
        new CacheProviderDefinition
        {
            Name = InMemoryCacheProvider.ProviderName,
            Priority = InMemoryCacheProvider.Priority,
            CanRegister = InMemoryCacheProvider.CanRegister,
            RegisterServices = InMemoryCacheProvider.RegisterServices,
            CreateProvider = serviceProvider =>
            {
                var memoryCache = serviceProvider.GetService<IMemoryCache>();
                if (memoryCache != null)
                {
                    var logger = serviceProvider.GetRequiredService<ILogger<InMemoryCacheProvider>>();
                    return new InMemoryCacheProvider(memoryCache, logger);
                }
                return null;
            }
        }
    };

    /// <summary>
    /// Creates a new instance of the CacheProviderFactory
    /// </summary>
    /// <param name="serviceProvider">Service provider for dependency resolution</param>
    /// <param name="logger">Logger for the factory</param>
    public CacheProviderFactory(
        IServiceProvider serviceProvider,
        ILogger<CacheProviderFactory> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers all available cache providers in priority order
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    public static void RegisterProviders(IServiceCollection services, IConfiguration configuration)
    {
        // Sort by priority (lower numbers = higher priority) and register
        foreach (var provider in Providers.OrderBy(p => p.Priority))
        {
            if (provider.CanRegister(configuration))
            {
                try
                {
                    provider.RegisterServices(services, configuration);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Failed to register cache provider: {provider.Name} - {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Creates a cache provider based on priority order
    /// </summary>
    /// <returns>The appropriate cache provider implementation</returns>
    public ICacheProvider CreateCacheProvider()
    {
        // Try providers in priority order (lower numbers = higher priority)
        foreach (var provider in Providers.OrderBy(p => p.Priority))
        {
            try
            {
                var instance = provider.CreateProvider(_serviceProvider);
                if (instance != null)
                {
                    _logger.LogInformation("Successfully created cache provider: {ProviderName} (Priority: {Priority})", 
                        provider.Name, provider.Priority);
                    return instance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create cache provider: {ProviderName}. Trying next provider.", provider.Name);
            }
        }

        throw new InvalidOperationException("No cache providers could be created. Ensure at least one cache provider is properly registered.");
    }

} 