using Shared.Providers;

namespace Tests.UnitTests.Shared.Providers.Cache;

public class NoOpCacheProviderTests
{
    [Fact]
    public async Task GetAsync_AlwaysReportsAMiss()
    {
        var provider = new NoOpCacheProvider();

        var result = await provider.GetAsync<string>("any-key");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_AlwaysReportsAMiss_EvenAfterSetAsync()
    {
        var provider = new NoOpCacheProvider();

        await provider.SetAsync("key", "value", TimeSpan.FromMinutes(5));
        var result = await provider.GetAsync<string>("key");

        Assert.Null(result);
    }

    [Fact]
    public async Task SetAsync_AlwaysReturnsFalse_MeaningNothingWasStored()
    {
        var provider = new NoOpCacheProvider();

        var stored = await provider.SetAsync("key", "value", TimeSpan.FromMinutes(5));

        Assert.False(stored);
    }

    [Fact]
    public async Task RemoveAsync_AlwaysReturnsTrue_SinceThereIsNothingToRemove()
    {
        var provider = new NoOpCacheProvider();

        var removed = await provider.RemoveAsync("key");

        Assert.True(removed);
    }
}
