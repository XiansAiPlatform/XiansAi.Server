namespace Shared.Providers;

public interface ICacheInvalidationBus
{
    Task PublishAsync(CacheInvalidationEnvelope envelope, CancellationToken cancellationToken = default);
}
