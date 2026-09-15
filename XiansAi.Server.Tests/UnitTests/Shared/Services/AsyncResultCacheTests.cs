using Microsoft.Extensions.Caching.Memory;
using Shared.Providers;
using Shared.Services;

namespace Tests.UnitTests.Shared.Services;

public class AsyncResultCacheTests
{
    private static AsyncResultCache BuildCache(bool isNoOp = false) =>
        new(new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 }), new CacheOperationMode(isNoOp));

    [Fact]
    public async Task GetOrAddAsync_CallsFactoryOnce_ForRepeatedCalls_WhenNotNoOp()
    {
        var cache = BuildCache();
        var calls = 0;

        await cache.GetOrAddAsync("key", _ => { calls++; return Task.FromResult("value"); }, TimeSpan.FromMinutes(5));
        await cache.GetOrAddAsync("key", _ => { calls++; return Task.FromResult("value"); }, TimeSpan.FromMinutes(5));

        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task GetOrAddAsync_CallsFactoryEveryTime_WhenNoOp()
    {
        var cache = BuildCache(isNoOp: true);
        var calls = 0;

        await cache.GetOrAddAsync("key", _ => { calls++; return Task.FromResult("value"); }, TimeSpan.FromMinutes(5));
        await cache.GetOrAddAsync("key", _ => { calls++; return Task.FromResult("value"); }, TimeSpan.FromMinutes(5));

        // Nothing is cached, so the caller's factory runs on every call instead of once.
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task GetOrAddAsync_ReturnsTheFreshlyComputedValue_WhenNoOp()
    {
        var cache = BuildCache(isNoOp: true);

        var result = await cache.GetOrAddAsync("key", _ => Task.FromResult("computed"), TimeSpan.FromMinutes(5));

        Assert.Equal("computed", result);
    }
}
