using Shared.Providers;

namespace Tests.UnitTests.Shared.Providers.Cache;

public class NoOpCacheInvalidationBusTests
{
    [Fact]
    public async Task PublishAsync_Completes_WithoutThrowing()
    {
        var bus = new NoOpCacheInvalidationBus();
        var envelope = new CacheInvalidationEnvelope(
            CacheInvalidationType.UserAuth,
            UserId: "user-1",
            TenantId: null,
            Keys: null,
            PublishedAtUtc: DateTimeOffset.UtcNow);

        await bus.PublishAsync(envelope);
    }
}
