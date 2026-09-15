using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;

namespace Tests.UnitTests.Shared.Services;

public class TenantCacheServiceTests
{
    private const string TenantId = "acme";

    private readonly Mock<ITenantRepository> _repository = new();
    private readonly Mock<ICacheInvalidationBus> _bus = new();

    private TenantCacheService BuildService(bool isNoOp = false)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _repository.Object);
        var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

        return new TenantCacheService(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            scopeFactory,
            new ConfigurationBuilder().Build(),
            NullLogger<TenantCacheService>.Instance,
            _bus.Object,
            new CacheOperationMode(isNoOp));
    }

    private static Tenant BuildTenant(string name = "Acme") => new()
    {
        Id = "id-1",
        TenantId = TenantId,
        Name = name,
        CreatedAt = DateTime.UtcNow,
        CreatedBy = "test"
    };

    [Fact]
    public async Task GetByTenantIdAsync_CachesResult_SoASecondCallDoesNotHitTheRepository()
    {
        _repository.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTenant());
        var service = BuildService();

        await service.GetByTenantIdAsync(TenantId);
        await service.GetByTenantIdAsync(TenantId);

        _repository.Verify(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetByTenantIdAsync_BypassesCache_WhenNoOp()
    {
        // Nothing is cached in this mode, so the per-key semaphore is skipped entirely and every
        // call re-reads the repository instead of serializing behind a lock for no benefit.
        _repository.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTenant());
        var service = BuildService(isNoOp: true);

        var first = await service.GetByTenantIdAsync(TenantId);
        var second = await service.GetByTenantIdAsync(TenantId);

        Assert.Equal(TenantId, first?.TenantId);
        Assert.Equal(TenantId, second?.TenantId);
        _repository.Verify(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task GetByTenantIdAsync_BypassCacheFlag_StillRefreshesTheCacheForLaterReads()
    {
        _repository.SetupSequence(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildTenant())
            .ReturnsAsync(BuildTenant("Renamed"));
        var service = BuildService();

        await service.GetByTenantIdAsync(TenantId);
        var refreshed = await service.GetByTenantIdAsync(TenantId, bypassCache: true);
        var cached = await service.GetByTenantIdAsync(TenantId);

        Assert.Equal("Renamed", refreshed?.Name);
        Assert.Equal("Renamed", cached?.Name);
        _repository.Verify(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
