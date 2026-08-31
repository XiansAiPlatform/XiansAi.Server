using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Shared.Providers;
using Shared.Services;
using StackExchange.Redis;

namespace Tests.UnitTests.Shared.Providers.Cache;

public class CacheProviderFactoryRegistrationTests
{
    [Fact]
    public void MemoryProvider_RegistersNoOpBusAndCoordinator()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "memory"
        });
        var services = CreateServicesWithSharedMemoryCache();
        CacheProviderFactory.RegisterProvider(services, config);

        var sp = services.BuildServiceProvider();

        Assert.IsType<NoOpCacheInvalidationBus>(sp.GetRequiredService<ICacheInvalidationBus>());
        Assert.IsType<NoOpPendingRequestCoordinator>(sp.GetRequiredService<IPendingRequestCoordinator>());
    }

    [Fact]
    public void DefaultProvider_RegistersNoOpBusAndInMemoryProvider()
    {
        var config = BuildConfig(new Dictionary<string, string?>());
        var services = CreateServicesWithSharedMemoryCache();
        CacheProviderFactory.RegisterProvider(services, config);

        var sp = services.BuildServiceProvider();

        Assert.IsType<InMemoryCacheProvider>(sp.GetRequiredService<ICacheProvider>());
        Assert.IsType<NoOpCacheInvalidationBus>(sp.GetRequiredService<ICacheInvalidationBus>());
        Assert.IsType<NoOpPendingRequestCoordinator>(sp.GetRequiredService<IPendingRequestCoordinator>());
    }

    [Fact]
    public void MemoryProvider_PreservesSizeLimitFromSharedConfiguration()
    {
        const long expectedSizeLimit = 100;
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "memory"
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache(options => options.SizeLimit = expectedSizeLimit);
        CacheProviderFactory.RegisterProvider(services, config);

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<MemoryCacheOptions>>();

        Assert.Equal(expectedSizeLimit, options.Value.SizeLimit);
    }

    [Fact]
    public void RedisProvider_RegistersRedisBusAndCoordinatorDescriptors()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "redis",
            ["Cache:Redis:ConnectionString"] = "localhost:6379,abortConnect=false,connectTimeout=1"
        });
        var services = CreateServicesWithSharedMemoryCache();
        CacheProviderFactory.RegisterProvider(services, config);

        Assert.Equal(typeof(RedisCacheProvider), GetImplementationType(services, typeof(ICacheProvider)));
        Assert.Contains(services, s => s.ServiceType == typeof(RedisCacheInvalidationBus));
        Assert.Contains(services, s => s.ServiceType == typeof(RedisPendingRequestCoordinator));
        Assert.Contains(services, s => s.ServiceType == typeof(IConnectionMultiplexer));

        var busDescriptor = services.Single(s => s.ServiceType == typeof(ICacheInvalidationBus));
        Assert.NotNull(busDescriptor.ImplementationFactory);
        Assert.Null(busDescriptor.ImplementationType);

        var coordinatorDescriptor = services.Single(s => s.ServiceType == typeof(IPendingRequestCoordinator));
        Assert.NotNull(coordinatorDescriptor.ImplementationFactory);
        Assert.Null(coordinatorDescriptor.ImplementationType);
    }

    [Fact]
    public void RedisProvider_RequiresConnectionString()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache:Provider"] = "redis"
        });
        var services = CreateServicesWithSharedMemoryCache();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            CacheProviderFactory.RegisterProvider(services, config));

        Assert.Contains("ConnectionString", ex.Message);
    }

    [Fact]
    public void DoubleUnderscoreFormatIsSupportedForRedis()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Cache__Provider"] = "redis",
            ["Cache__Redis__ConnectionString"] = "localhost:6379,abortConnect=false,connectTimeout=1"
        });
        var services = CreateServicesWithSharedMemoryCache();
        CacheProviderFactory.RegisterProvider(services, config);

        Assert.Equal(typeof(RedisCacheProvider), GetImplementationType(services, typeof(ICacheProvider)));
        Assert.Contains(services, s => s.ServiceType == typeof(RedisCacheInvalidationBus));
    }

    private static ServiceCollection CreateServicesWithSharedMemoryCache()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache(options => options.SizeLimit = 100);
        return services;
    }

    private static IConfiguration BuildConfig(IDictionary<string, string?> settings)
        => new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static Type? GetImplementationType(IServiceCollection services, Type serviceType)
        => services.Single(s => s.ServiceType == serviceType).ImplementationType;

}
