using Features.AgentApi.Auth;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Providers.Auth;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Disabling an account is checked when an authorization decision is made, never when a cached one
/// is reused, so each cache in front of that check is a window during which a disabled account
/// keeps working. These cover that the window closes on the next request instead.
/// </summary>
public class UserCacheInvalidationTests
{
    private const string UserId = "provider-subject-abc123";
    private const string TenantId = "acme";

    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 100 });
    private readonly Mock<IUserRepository> _userRepo = new();

    private UserCacheIndex BuildIndex() => new(_cache, NullLogger<UserCacheIndex>.Instance);

    private static IConfiguration EmptyConfiguration() => new ConfigurationBuilder().Build();

    [Fact]
    public void Invalidate_RemovesEveryEntryTrackedForTheUser()
    {
        var index = BuildIndex();
        _cache.Set("first", "value", new MemoryCacheEntryOptions().SetSize(1));
        _cache.Set("second", "value", new MemoryCacheEntryOptions().SetSize(1));
        index.Track(UserId, "first");
        index.Track(UserId, "second");

        var removed = index.Invalidate(UserId);

        Assert.Equal(2, removed);
        Assert.False(_cache.TryGetValue("first", out _));
        Assert.False(_cache.TryGetValue("second", out _));
    }

    [Fact]
    public void Invalidate_LeavesAnotherUsersEntriesAlone()
    {
        var index = BuildIndex();
        _cache.Set("theirs", "value", new MemoryCacheEntryOptions().SetSize(1));
        index.Track("someone-else", "theirs");

        Assert.Equal(0, index.Invalidate(UserId));
        Assert.True(_cache.TryGetValue("theirs", out _));
    }

    [Fact]
    public void Forget_StopsTheIndexGrowingAsEntriesExpireOnTheirOwn()
    {
        var index = BuildIndex();
        _cache.Set("key", "value", new MemoryCacheEntryOptions().SetSize(1));
        index.Track(UserId, "key");

        index.Forget(UserId, "key");

        Assert.Equal(0, index.Invalidate(UserId));
    }

    [Fact]
    public async Task TokenCache_InvalidatesTokensCachedByAnEarlierRequest()
    {
        // The cache is registered scoped, so the instance that caches a token during one request is
        // not the instance that invalidates it when an administrator disables the account. An index
        // belonging to either instance would be empty in the other.
        var index = BuildIndex();
        var cachingRequest = BuildTokenCache(index);
        var lockingRequest = BuildTokenCache(index);
        await cachingRequest.CacheValidation("a-token", valid: true, UserId, new[] { TenantId });

        await lockingRequest.InvalidateUserTokens(UserId);

        var (found, _, _, _) = await cachingRequest.GetValidation("a-token");
        Assert.False(found);
    }

    private MemoryTokenValidationCache BuildTokenCache(IUserCacheIndex index) =>
        new(_cache, index, NullLogger<MemoryTokenValidationCache>.Instance, EmptyConfiguration());

    [Fact]
    public async Task RoleCache_DropsRolesForEveryTenantOnInvalidation()
    {
        var index = BuildIndex();
        _userRepo.Setup(x => x.GetUserRolesAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync(new List<string> { SystemRoles.TenantAdmin });
        var roleCache = new RoleCacheService(_cache, _userRepo.Object, index);
        await roleCache.GetUserRolesAsync(UserId, TenantId);
        await roleCache.GetUserRolesAsync(UserId, "other-tenant");

        index.Invalidate(UserId);

        // Both tenants are re-read, including the one the record no longer lists a membership for,
        // which the caller could not have worked out from the record alone.
        _userRepo.Invocations.Clear();
        await roleCache.GetUserRolesAsync(UserId, TenantId);
        await roleCache.GetUserRolesAsync(UserId, "other-tenant");
        _userRepo.Verify(x => x.GetUserRolesAsync(UserId, It.IsAny<string>()), Times.Exactly(2));
    }

    [Fact]
    public void CertificateCache_DropsAValidatedCertificateWhenItsAccountIsDisabled()
    {
        // The entry carries the roles and the SysAdmin flag, so a hit never revisits the record and
        // the agent would otherwise keep running for the lifetime of the entry.
        var index = BuildIndex();
        var certCache = new MemoryCertificateValidationCache(
            _cache, index, NullLogger<MemoryCertificateValidationCache>.Instance, EmptyConfiguration());
        certCache.CacheValidation("THUMBPRINT", new CachedCertificateValidation
        {
            IsValid = true,
            TenantId = TenantId,
            UserId = UserId,
            Roles = new[] { SystemRoles.TenantAdmin }
        });

        index.Invalidate(UserId);

        var (found, _) = certCache.GetValidation("THUMBPRINT");
        Assert.False(found);
    }

    [Fact]
    public async Task Invalidator_AlsoDropsTheAccountsSharingTheAddress()
    {
        // An address that names more than one account resolves through all of them, so disabling
        // one changes what the others resolve to — their cached decisions are stale as well.
        var index = BuildIndex();
        _cache.Set("theirs", "value", new MemoryCacheEntryOptions().SetSize(1));
        index.Track("the-sibling", "theirs");
        _userRepo.Setup(x => x.GetAllByUserEmailAsync("shared@example.com"))
            .ReturnsAsync(new List<User>
            {
                new() { UserId = UserId, Email = "shared@example.com" },
                new() { UserId = "the-sibling", Email = "shared@example.com" }
            });

        await BuildInvalidator(index).InvalidateAsync(
            new User { UserId = UserId, Email = "shared@example.com" });

        Assert.False(_cache.TryGetValue("theirs", out _));
    }

    [Fact]
    public async Task Invalidator_StillDropsTheAccountsOwnEntries_WhenTheSiblingLookupFails()
    {
        var index = BuildIndex();
        _cache.Set("mine", "value", new MemoryCacheEntryOptions().SetSize(1));
        index.Track(UserId, "mine");
        _userRepo.Setup(x => x.GetAllByUserEmailAsync(It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("the database is unreachable"));

        await BuildInvalidator(index).InvalidateAsync(
            new User { UserId = UserId, Email = "shared@example.com" });

        Assert.False(_cache.TryGetValue("mine", out _));
    }

    [Fact]
    public async Task Invalidator_PublishesUserAuthAfterRemovingLocalEntries()
    {
        var index = BuildIndex();
        _cache.Set("mine", "value", new MemoryCacheEntryOptions().SetSize(1));
        index.Track(UserId, "mine");
        var bus = new Mock<ICacheInvalidationBus>();
        bus.Setup(x => x.PublishAsync(
                It.IsAny<CacheInvalidationEnvelope>(),
                It.IsAny<CancellationToken>()))
            .Callback(() => Assert.False(_cache.TryGetValue("mine", out _)))
            .Returns(Task.CompletedTask);

        await BuildInvalidator(index, bus.Object).InvalidateAsync(
            new User { UserId = UserId, Email = string.Empty });

        bus.Verify(x => x.PublishAsync(
            It.Is<CacheInvalidationEnvelope>(envelope =>
                envelope.Type == CacheInvalidationType.UserAuth &&
                envelope.UserId == UserId &&
                envelope.TenantId == null &&
                envelope.Keys == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    private UserAuthorizationInvalidator BuildInvalidator(
        IUserCacheIndex index,
        ICacheInvalidationBus? invalidationBus = null) =>
        new(
            index,
            _userRepo.Object,
            NullLogger<UserAuthorizationInvalidator>.Instance,
            invalidationBus ?? Mock.Of<ICacheInvalidationBus>());
}
