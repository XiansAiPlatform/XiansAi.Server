using StackExchange.Redis;
using Shared.Services;

namespace Shared.Providers;

/// <summary>
/// Validates that Redis used for cross-replica cache coordination is configured with
/// authentication and TLS. Pub/sub channels and pending-result keys are trusted control-plane
/// traffic; network access to Redis must not be open without credentials.
/// </summary>
public static class RedisConnectionSecurity
{
    /// <summary>
    /// Returns null when the connection string looks sufficiently secured; otherwise a
    /// human-readable description of what is missing (auth and/or TLS).
    /// </summary>
    public static string? GetSecurityGap(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        ConfigurationOptions options;
        try
        {
            options = ConfigurationOptions.Parse(connectionString);
        }
        catch (Exception ex)
        {
            return $"connection string could not be parsed ({ex.Message})";
        }

        var hasAuth = !string.IsNullOrWhiteSpace(options.Password);
        var hasTls = options.Ssl;

        if (hasAuth && hasTls)
        {
            return null;
        }

        var missing = new List<string>(2);
        if (!hasAuth)
        {
            missing.Add("password/AUTH (e.g. password=...)");
        }

        if (!hasTls)
        {
            missing.Add("TLS (e.g. ssl=true)");
        }

        return string.Join(" and ", missing);
    }

    /// <summary>
    /// Enforces Redis AUTH+TLS for multi-instance deployments.
    /// Development environments and <paramref name="allowInsecure"/> only warn;
    /// all other environments throw when auth or TLS is missing.
    /// </summary>
    public static void ValidateOrThrow(
        string connectionString,
        bool isDevelopment,
        bool allowInsecure,
        Action<string> warn)
    {
        ArgumentNullException.ThrowIfNull(warn);

        var gap = GetSecurityGap(connectionString);
        if (gap is null)
        {
            return;
        }

        var message =
            "Cache:Redis:ConnectionString must use AUTH and TLS in multi-instance deployments " +
            $"(missing {gap}). Redis is a trusted control plane for cache invalidation " +
            $"({RedisCacheInvalidationBus.ChannelName}) and pending-request completion " +
            $"({RedisPendingRequestCoordinator.CompletionChannelName}). " +
            "Use a network-isolated Redis with password and ssl=true, or set " +
            "Cache:Redis:AllowInsecureConnection=true only for local/lab use.";

        if (isDevelopment || allowInsecure)
        {
            warn(message);
            return;
        }

        throw new InvalidOperationException(message);
    }
}
