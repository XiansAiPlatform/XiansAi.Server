namespace Shared.Providers;

public interface ICacheInvalidationApplicator
{
    void Apply(CacheInvalidationEnvelope envelope);
}
