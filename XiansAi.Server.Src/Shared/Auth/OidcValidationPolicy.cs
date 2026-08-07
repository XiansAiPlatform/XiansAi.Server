using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Caching.Memory;

namespace Shared.Auth;

/// <summary>
/// The rules a tenant's own OIDC configuration is not allowed to weaken.
///
/// Tenant OIDC settings are stored per tenant and edited at runtime through an API. Writing them
/// requires SysAdmin, but they are still records rather than reviewed deployment configuration —
/// they can be wrong, stale, or inherited from a looser era — and treating every one as
/// authoritative would let a single record turn off its tenant's authentication. This class is
/// where the deployment draws the line: some settings are simply overridden, and the ones that need
/// a migration before they can be enforced are gated behind a switch that starts off, warns about
/// every affected tenant, and can be turned on once the warnings stop.
/// </summary>
public class OidcValidationPolicy
{
    /// <summary>Longest a successful validation may be reused. Never exceeds the token's own expiry.</summary>
    public TimeSpan ValidationCacheDuration { get; }

    /// <summary>How long to wait for a provider's discovery document.</summary>
    public TimeSpan DiscoveryTimeout { get; }

    /// <summary>
    /// Whether a provider must declare the audiences it accepts.
    ///
    /// Off by default because existing tenant configurations were not required to set one. While
    /// off, a provider with no audience accepts any token that issuer signed — including tokens
    /// minted for an unrelated application at the same identity provider — and every such request
    /// is warned about. Turn it on once no tenant is being warned about any more.
    /// </summary>
    public bool RequireAudience { get; }

    /// <summary>
    /// Whether the subject may only be read from the claims OIDC guarantees to be stable
    /// (<c>sub</c>, and <c>oid</c> for Entra), rather than falling back to mutable ones such as
    /// <c>email</c> or <c>name</c>.
    ///
    /// Off by default because turning it on changes the user id of anyone currently signing in
    /// through a fallback claim, orphaning their record. A provider can always nominate its own
    /// claim via the <c>userIdClaim</c> setting, which is honoured either way, so a tenant that
    /// needs the old behaviour can name the claim explicitly and keep it.
    /// </summary>
    public bool RequireStandardSubjectClaim { get; }

    /// <summary>
    /// Whether a provider may point discovery at a plaintext or private-network address.
    ///
    /// Permitted outside Production so that local development and test environments can run
    /// against a container on localhost. In Production this is always false: the authority is
    /// tenant-supplied and the server fetches it unauthenticated, so allowing it would turn
    /// authentication into a request forger for anything reachable from the cluster.
    /// </summary>
    public bool AllowInsecureAuthority { get; }

    private readonly OidcWarningThrottle _warnings;
    private readonly ILogger _logger;

    public OidcValidationPolicy(
        IConfiguration configuration,
        IHostEnvironment environment,
        IMemoryCache cache,
        ILogger<OidcValidationPolicy> logger)
    {
        _logger = logger;
        _warnings = new OidcWarningThrottle(cache, TimeSpan.FromMinutes(
            configuration.GetValue<double>("Auth:OidcWarningIntervalMinutes", 15)));

        ValidationCacheDuration = TimeSpan.FromSeconds(
            configuration.GetValue<double>("Auth:OidcValidationCacheDurationSeconds", 60));
        DiscoveryTimeout = TimeSpan.FromSeconds(
            configuration.GetValue<double>("Auth:OidcDiscoveryTimeoutSeconds", 30));
        RequireAudience = configuration.GetValue("Auth:RequireOidcAudience", false);
        RequireStandardSubjectClaim = configuration.GetValue("Auth:StrictSubjectClaim", false);
        AllowInsecureAuthority = !environment.IsProduction();

        if (AllowInsecureAuthority)
        {
            _logger.LogWarning(
                "Environment is {Environment}, so OIDC providers may use plaintext or private-network " +
                "authorities. This is never permitted in Production.", environment.EnvironmentName);
        }
    }

    /// <summary>
    /// Logs a configuration problem that recurs on every request from an affected tenant, at most
    /// once per interval per subject so that it is visible without flooding the log.
    /// </summary>
    public void WarnAboutConfiguration(string subject, string message, params object?[] args)
    {
        if (_warnings.ShouldWarn(subject))
        {
            _logger.LogWarning(message, args);
        }
    }

    /// <summary>
    /// The signing algorithms a token may use: whatever the provider lists, else whatever its
    /// discovery document advertises, else no restriction beyond what the signing keys support.
    ///
    /// <c>none</c> is stripped from either list. It is not an algorithm but the absence of one, and
    /// accepting it would mean accepting a token any caller could write by hand.
    /// </summary>
    public static IEnumerable<string>? ResolveAllowedAlgorithms(
        IReadOnlyCollection<string>? providerAlgorithms,
        IEnumerable<string>? discoveryAlgorithms)
    {
        var configured = providerAlgorithms ?? discoveryAlgorithms?.ToArray();
        if (configured is not { Count: > 0 })
        {
            return null;
        }

        var allowed = configured
            .Where(alg => !string.Equals(alg, "none", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return allowed.Length > 0 ? allowed : null;
    }

    /// <summary>
    /// Explains why a discovery document must not be fetched from <paramref name="authority"/>, or
    /// returns null when it is acceptable.
    ///
    /// This blocks addresses written directly into the configuration. It cannot stop a hostname
    /// that resolves to an internal address, which would need egress control at the network layer.
    /// </summary>
    public string? DescribeUnsafeAuthority(string? authority)
    {
        if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri))
        {
            return "authority is not an absolute URL";
        }

        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
        {
            return $"authority scheme '{uri.Scheme}' is not http or https";
        }

        if (AllowInsecureAuthority)
        {
            return null;
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return "authority must use https";
        }

        if (uri.IsLoopback)
        {
            return "authority resolves to the loopback interface";
        }

        if (IPAddress.TryParse(uri.Host.Trim('[', ']'), out var address) && IsPrivate(address))
        {
            return "authority is a private or link-local address";
        }

        return null;
    }

    /// <summary>
    /// Addresses that are unreachable from the public internet, and so can only name something
    /// inside the deployment's own network — including the cloud instance metadata endpoint.
    /// </summary>
    private static bool IsPrivate(IPAddress address)
    {
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return true;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            // fe80::/10 link-local and fc00::/7 unique-local.
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
        }

        var octets = address.GetAddressBytes();
        return octets[0] switch
        {
            10 => true,
            127 => true,
            169 => octets[1] == 254,
            172 => octets[1] >= 16 && octets[1] <= 31,
            192 => octets[1] == 168,
            _ => false
        };
    }
}

/// <summary>
/// Keeps a recurring warning to one line per interval per subject.
/// </summary>
internal sealed class OidcWarningThrottle
{
    private const string CacheKeyPrefix = "oidc_warned:";

    private readonly IMemoryCache _cache;
    private readonly TimeSpan _interval;

    public OidcWarningThrottle(IMemoryCache cache, TimeSpan interval)
    {
        _cache = cache;
        _interval = interval;
    }

    public bool ShouldWarn(string subject)
    {
        var key = CacheKeyPrefix + subject;
        if (_cache.TryGetValue(key, out _))
        {
            return false;
        }

        _cache.Set(key, true, new MemoryCacheEntryOptions()
            .SetAbsoluteExpiration(_interval)
            .SetSize(1));
        return true;
    }
}
