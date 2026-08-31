using Moq;
using Shared.Utils.Temporal;
using Temporalio.Client;

namespace XiansAi.Server.Tests.UnitTests.Shared.Utils;

public class TemporalClientCacheTests
{
    [Fact]
    public void BuildRequestKey_UsesBareTenantId_WhenAgentIsMissing()
    {
        Assert.Equal("acme", TemporalClientCache.BuildRequestKey("acme", null));
        Assert.Equal("acme", TemporalClientCache.BuildRequestKey("acme", string.Empty));
    }

    [Fact]
    public void BuildRequestKey_IncludesAgent_WhenProvided()
    {
        Assert.Equal("acme:Bot", TemporalClientCache.BuildRequestKey("acme", "Bot"));
    }

    [Fact]
    public void IsKeyForTenant_MatchesBareTenantAndAgentPrefixedKeys()
    {
        Assert.True(TemporalClientCache.IsKeyForTenant("acme", "acme"));
        Assert.True(TemporalClientCache.IsKeyForTenant("acme:Bot", "acme"));
        Assert.False(TemporalClientCache.IsKeyForTenant("acme", "ac"));
        Assert.False(TemporalClientCache.IsKeyForTenant("beta:Bot", "acme"));
    }

    [Fact]
    public void RemoveByTenant_EvictsBareTenantDefaultClient()
    {
        var cache = new TemporalClientCache();
        var client = Mock.Of<ITemporalClient>();
        cache.Add("acme", "acme", client);

        var removed = cache.RemoveByTenant("acme");

        Assert.Single(removed);
        Assert.Same(client, removed[0]);
        Assert.False(cache.TryGet("acme", out _));
    }

    [Fact]
    public void RemoveByTenant_EvictsOriginRoutedKeys_FromOtherTenants()
    {
        var cache = new TemporalClientCache();
        var originClient = Mock.Of<ITemporalClient>();
        var otherClient = Mock.Of<ITemporalClient>();

        cache.Add("acme", "acme", originClient);
        cache.Add("beta:Bot", "acme", originClient);
        cache.Add("beta", "beta", otherClient);

        var removed = cache.RemoveByTenant("acme");

        Assert.Single(removed);
        Assert.Same(originClient, removed[0]);
        Assert.False(cache.TryGet("acme", out _));
        Assert.False(cache.TryGet("beta:Bot", out _));
        Assert.True(cache.TryGet("beta", out var remaining));
        Assert.Same(otherClient, remaining);
    }

    [Fact]
    public void Add_IndexesConfigTenantKey_SoTenantDefaultLookupSucceeds()
    {
        var cache = new TemporalClientCache();
        var client = Mock.Of<ITemporalClient>();

        cache.Add("beta:Bot", "acme", client);

        Assert.True(cache.TryGet("acme", out var byTenant));
        Assert.Same(client, byTenant);
        Assert.True(cache.TryGetByConfigTenant("acme", out var byConfig));
        Assert.Same(client, byConfig);
    }
}
