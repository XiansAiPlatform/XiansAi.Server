namespace Shared.Providers;

/// <summary>
/// True when Cache:Provider is set to "noop". Services that keep their own IMemoryCache
/// (roles, tenants, API keys, etc.) inject this to skip caching when it's true.
/// </summary>
public interface ICacheOperationMode
{
    bool IsNoOp { get; }
}

public sealed class CacheOperationMode : ICacheOperationMode
{
    public CacheOperationMode(bool isNoOp)
    {
        IsNoOp = isNoOp;
    }

    public bool IsNoOp { get; }
}
