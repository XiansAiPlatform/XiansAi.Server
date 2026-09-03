using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Providers;
using Shared.Services;

namespace Tests.UnitTests.Shared.Providers.Cache;

public class CacheInvalidationApplicatorTests
{
    [Fact]
    public void Apply_UserAuth_CallsUserCacheIndexInvalidate()
    {
        using var memory = CreateMemoryCache();
        var userCacheIndex = new Mock<IUserCacheIndex>();
        var incomingOriginCache = new Mock<IIncomingOriginCache>();
        var applicator = CreateApplicator(userCacheIndex, memory, incomingOriginCache);

        applicator.Apply(CreateEnvelope(CacheInvalidationType.UserAuth, userId: "user-1"));

        userCacheIndex.Verify(index => index.Invalidate("user-1"), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void Apply_UserAuth_WithMissingUserId_DoesNotInvalidate(string? userId)
    {
        using var memory = CreateMemoryCache();
        var userCacheIndex = new Mock<IUserCacheIndex>();
        var incomingOriginCache = new Mock<IIncomingOriginCache>();
        var applicator = CreateApplicator(userCacheIndex, memory, incomingOriginCache);

        applicator.Apply(CreateEnvelope(CacheInvalidationType.UserAuth, userId: userId));

        userCacheIndex.Verify(index => index.Invalidate(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Apply_Tenant_RemovesProvidedKeys()
    {
        using var memory = CreateMemoryCache();
        memory.Set("tenant:custom:t1", new object(), EntryOptions());
        memory.Set("tenant:other:t1", new object(), EntryOptions());
        var applicator = CreateApplicator(memory: memory);

        applicator.Apply(CreateEnvelope(
            CacheInvalidationType.Tenant,
            tenantId: "t1",
            keys: ["tenant:custom:t1", "tenant:other:t1"]));

        Assert.False(memory.TryGetValue("tenant:custom:t1", out _));
        Assert.False(memory.TryGetValue("tenant:other:t1", out _));
    }

    [Fact]
    public void Apply_Tenant_WithoutKeys_RemovesConventionalTenantKey()
    {
        using var memory = CreateMemoryCache();
        memory.Set("tenant:byid:t1", new object(), EntryOptions());
        var applicator = CreateApplicator(memory: memory);

        applicator.Apply(CreateEnvelope(CacheInvalidationType.Tenant, tenantId: "t1"));

        Assert.False(memory.TryGetValue("tenant:byid:t1", out _));
    }

    [Theory]
    [InlineData(CacheInvalidationType.ApiKey)]
    [InlineData(CacheInvalidationType.Activation)]
    [InlineData(CacheInvalidationType.AgentWorkflowTypes)]
    [InlineData(CacheInvalidationType.ThreadId)]
    public void Apply_KeyBasedType_RemovesEveryProvidedKey(CacheInvalidationType type)
    {
        using var memory = CreateMemoryCache();
        memory.Set("key-1", new object(), EntryOptions());
        memory.Set("key-2", new object(), EntryOptions());
        var applicator = CreateApplicator(memory: memory);

        applicator.Apply(CreateEnvelope(type, keys: ["key-1", "key-2"]));

        Assert.False(memory.TryGetValue("key-1", out _));
        Assert.False(memory.TryGetValue("key-2", out _));
    }

    [Fact]
    public void Apply_ThreadOrigin_InvalidatesThreadUsingFirstKey()
    {
        using var memory = CreateMemoryCache();
        var incomingOriginCache = new Mock<IIncomingOriginCache>();
        var applicator = CreateApplicator(memory: memory, incomingOriginCache: incomingOriginCache);

        applicator.Apply(CreateEnvelope(
            CacheInvalidationType.ThreadOrigin,
            keys: ["thread-1", "ignored-thread"]));

        incomingOriginCache.Verify(cache => cache.InvalidateThread("thread-1", false), Times.Once);
        incomingOriginCache.Verify(
            cache => cache.InvalidateThread("ignored-thread", It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public void Apply_ThreadOrigin_WithoutKeys_DoesNotInvalidate()
    {
        using var memory = CreateMemoryCache();
        var incomingOriginCache = new Mock<IIncomingOriginCache>();
        var applicator = CreateApplicator(memory: memory, incomingOriginCache: incomingOriginCache);

        applicator.Apply(CreateEnvelope(CacheInvalidationType.ThreadOrigin));

        incomingOriginCache.Verify(
            cache => cache.InvalidateThread(It.IsAny<string>(), It.IsAny<bool>()),
            Times.Never);
    }

    private static CacheInvalidationApplicator CreateApplicator(
        Mock<IUserCacheIndex>? userCacheIndex = null,
        IMemoryCache? memory = null,
        Mock<IIncomingOriginCache>? incomingOriginCache = null)
    {
        return new CacheInvalidationApplicator(
            (userCacheIndex ?? new Mock<IUserCacheIndex>()).Object,
            memory ?? CreateMemoryCache(),
            (incomingOriginCache ?? new Mock<IIncomingOriginCache>()).Object,
            NullLogger<CacheInvalidationApplicator>.Instance);
    }

    private static MemoryCache CreateMemoryCache() =>
        new(new MemoryCacheOptions { SizeLimit = 100 });

    private static MemoryCacheEntryOptions EntryOptions() =>
        new MemoryCacheEntryOptions().SetSize(1);

    private static CacheInvalidationEnvelope CreateEnvelope(
        CacheInvalidationType type,
        string? userId = null,
        string? tenantId = null,
        IReadOnlyList<string>? keys = null) =>
        new(type, userId, tenantId, keys, DateTimeOffset.UtcNow);
}
