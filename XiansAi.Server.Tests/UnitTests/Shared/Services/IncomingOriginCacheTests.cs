using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Providers;
using Shared.Services;

namespace Tests.UnitTests.Shared.Services;

public class IncomingOriginCacheTests
{
    private const string TenantId = "tenant-a";
    private const string ThreadId = "68b1f0c2a1b2c3d4e5f60718";

    private static IncomingOriginCache BuildCache(ICacheInvalidationBus? invalidationBus = null)
    {
        return new IncomingOriginCache(
            new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }),
            NullLogger<IncomingOriginCache>.Instance,
            invalidationBus ?? new NoOpCacheInvalidationBus());
    }

    [Fact]
    public void Get_ReturnsNull_WhenNothingCached()
    {
        var cache = BuildCache();

        Assert.Null(cache.Get(TenantId, ThreadId, "topic1"));
    }

    [Fact]
    public void Get_ReturnsCachedEntry_ForMatchingScope()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData("app:slack", new { channel = "C1" }));

        var cached = cache.Get(TenantId, ThreadId, "topic1");

        Assert.NotNull(cached);
        Assert.Equal("app:slack", cached!.Origin);
        Assert.NotNull(cached.Data);
    }

    [Fact]
    public void Get_TreatsNullEmptyAndWhitespaceScopeAsTheSameDefaultScope()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, null, new IncomingOriginData("app:web", null));

        Assert.Equal("app:web", cache.Get(TenantId, ThreadId, "")!.Origin);
        Assert.Equal("app:web", cache.Get(TenantId, ThreadId, "   ")!.Origin);
    }

    [Fact]
    public void Get_DoesNotLeakAcrossTenantsOrScopes()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData("app:slack", null));

        Assert.Null(cache.Get("tenant-b", ThreadId, "topic1"));
        Assert.Null(cache.Get(TenantId, ThreadId, "topic2"));
    }

    [Fact]
    public void Get_ReturnsCachedNegativeResult()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData(null, null));

        var cached = cache.Get(TenantId, ThreadId, "topic1");

        Assert.NotNull(cached);
        Assert.Null(cached!.Origin);
        Assert.Null(cached.Data);
    }

    [Fact]
    public void InvalidateThread_DropsEveryScopeAndTenantOfThatThread()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, null, new IncomingOriginData("app:web", null));
        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData("app:slack", null));
        cache.Set("tenant-b", ThreadId, "topic2", new IncomingOriginData("app:teams", null));

        cache.InvalidateThread(ThreadId);

        Assert.Null(cache.Get(TenantId, ThreadId, null));
        Assert.Null(cache.Get(TenantId, ThreadId, "topic1"));
        Assert.Null(cache.Get("tenant-b", ThreadId, "topic2"));
    }

    [Fact]
    public void InvalidateThread_LeavesOtherThreadsCached()
    {
        var cache = BuildCache();
        const string otherThreadId = "68b1f0c2a1b2c3d4e5f60999";
        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData("app:slack", null));
        cache.Set(TenantId, otherThreadId, "topic1", new IncomingOriginData("app:teams", null));

        cache.InvalidateThread(ThreadId);

        Assert.Null(cache.Get(TenantId, ThreadId, "topic1"));
        Assert.Equal("app:teams", cache.Get(TenantId, otherThreadId, "topic1")!.Origin);
    }

    [Fact]
    public void Set_AfterInvalidateThread_CachesAgain()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData("app:slack", null));
        cache.InvalidateThread(ThreadId);

        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData("app:teams", null));

        Assert.Equal("app:teams", cache.Get(TenantId, ThreadId, "topic1")!.Origin);
    }

    [Fact]
    public void InvalidateScope_DropsOnlyThatScope()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, null, new IncomingOriginData("app:web", null));
        cache.Set(TenantId, ThreadId, "topic1", new IncomingOriginData("app:slack", null));

        cache.InvalidateScope(TenantId, ThreadId, "topic1");

        Assert.Null(cache.Get(TenantId, ThreadId, "topic1"));
        Assert.Equal("app:web", cache.Get(TenantId, ThreadId, null)!.Origin);
    }

    [Fact]
    public void InvalidateScope_DropsDefaultScope_WhenScopeIsNull()
    {
        var cache = BuildCache();
        cache.Set(TenantId, ThreadId, null, new IncomingOriginData("app:web", null));

        cache.InvalidateScope(TenantId, ThreadId, null);

        Assert.Null(cache.Get(TenantId, ThreadId, null));
    }

    [Fact]
    public void InvalidateScope_PublishesThreadOriginUsingThreadId()
    {
        var bus = new Mock<ICacheInvalidationBus>();
        var cache = BuildCache(bus.Object);

        cache.InvalidateScope(TenantId, ThreadId, "topic1");

        bus.Verify(x => x.PublishAsync(
            It.Is<CacheInvalidationEnvelope>(envelope =>
                envelope.Type == CacheInvalidationType.ThreadOrigin &&
                envelope.Keys != null &&
                envelope.Keys.SequenceEqual(new[] { ThreadId })),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void InvalidateThread_WhenApplyingRemoteEvent_DoesNotRepublish()
    {
        var bus = new Mock<ICacheInvalidationBus>();
        var cache = BuildCache(bus.Object);

        cache.InvalidateThread(ThreadId, publish: false);

        bus.Verify(x => x.PublishAsync(
            It.IsAny<CacheInvalidationEnvelope>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }
}
