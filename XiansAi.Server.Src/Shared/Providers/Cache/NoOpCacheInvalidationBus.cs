namespace Shared.Providers;

public sealed class NoOpCacheInvalidationBus : ICacheInvalidationBus
{
    public Task PublishAsync(CacheInvalidationEnvelope envelope, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
