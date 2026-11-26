using Features.AgentApi.Endpoints;
using Features.Shared.Configuration;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Features.AgentApi;

/// <summary>
/// Covers the per-tenant notification quota: it runs out, each tenant has its own, and it can be turned off.
/// </summary>
public class NotificationRateLimiterTests
{
    private static NotificationRateLimiter Limiter(int permitLimit = 3, bool enabled = true) => new(new RateLimitSettings
    {
        Enabled = enabled,
        Notification = new RateLimitPolicySettings { PermitLimit = permitLimit, WindowSeconds = 60 }
    });

    [Fact]
    public void TryAcquire_QuotaUsedUp_IsRejectedWithRetryAfter()
    {
        using var limiter = Limiter();
        for (var i = 0; i < 3; i++)
            Assert.True(limiter.TryAcquire("acme", out _));

        Assert.False(limiter.TryAcquire("acme", out var retryAfter));
        Assert.NotNull(retryAfter);
    }

    [Fact]
    public void TryAcquire_EachTenantHasItsOwnQuota_IgnoringCase()
    {
        using var limiter = Limiter(permitLimit: 1);

        Assert.True(limiter.TryAcquire("acme", out _));
        Assert.False(limiter.TryAcquire("ACME", out _));
        Assert.True(limiter.TryAcquire("other", out _));
    }

    [Fact]
    public void TryAcquire_RateLimitingDisabled_AlwaysAllowed()
    {
        using var limiter = Limiter(permitLimit: 1, enabled: false);

        Assert.True(limiter.TryAcquire("acme", out _));
        Assert.True(limiter.TryAcquire("acme", out _));
    }
}
