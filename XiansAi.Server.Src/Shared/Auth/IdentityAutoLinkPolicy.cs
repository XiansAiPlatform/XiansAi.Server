using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Auth;

/// <summary>
/// Decides which identity providers may have a sign-in attached to an existing account
/// automatically, on the strength of a verified email address alone.
///
/// This exists because the alternative — merging whenever the email matches — hands over any account
/// to anyone who can make a provider assert its address. Tenant administrators configure their own
/// tenant's OIDC providers here, so without this restriction one of them could point a tenant at a
/// directory they control, mint a token claiming a SysAdmin's address, and be merged into that
/// account. The trusted list is therefore read from deployment configuration only, which the tenant
/// OIDC API cannot write to.
///
/// Trusting a provider is a statement that the operator believes its verified-email claims: that the
/// provider checked the holder controls the address, and that no third party can make it say
/// otherwise. That is true of consumer providers like Google. It is emphatically not true of a
/// multi-tenant Microsoft endpoint, where any directory in the world may issue tokens and its
/// administrators choose their users' email addresses; a single Entra tenant may be trusted, since
/// that names one directory whose administrators the operator has decided to rely on.
/// </summary>
public class IdentityAutoLinkPolicy
{
    private const string ConfigurationKey = "Auth:AutoLinkTrustedProviders";

    /// <summary>
    /// Google verifies that a signed-in holder controls the address it puts in <c>email</c>, and
    /// issues <c>email_verified</c> to say so, so a match is meaningful without an operator opting in.
    /// </summary>
    private static readonly string[] DefaultTrustedAuthorities = ["https://accounts.google.com"];

    /// <summary>
    /// Microsoft endpoints that any directory can authenticate against. Tokens from these carry no
    /// statement about *which* organization vouched for the address, so an email match proves
    /// nothing about who the holder is.
    /// </summary>
    private static readonly string[] MultiTenantMicrosoftPaths = ["/common", "/organizations", "/consumers"];

    private readonly HashSet<string> _trustedAuthorities;

    public IdentityAutoLinkPolicy(IConfiguration configuration, ILogger<IdentityAutoLinkPolicy> logger)
    {
        var configured = configuration.GetSection(ConfigurationKey).Get<string[]>();
        var authorities = configured ?? DefaultTrustedAuthorities;

        _trustedAuthorities = new HashSet<string>(StringComparer.Ordinal);

        foreach (var authority in authorities)
        {
            var rejection = DescribeUntrustableAuthority(authority);
            if (rejection != null)
            {
                logger.LogError("Ignoring {ConfigurationKey} entry '{Authority}': {Reason}",
                    ConfigurationKey, LogSanitizer.Sanitize(authority), rejection);
                continue;
            }

            _trustedAuthorities.Add(LinkedIdentityKey.NormalizeAuthority(authority));
        }

        if (_trustedAuthorities.Count > 0)
        {
            logger.LogInformation(
                "A verified email from these providers will be attached to a matching account automatically: {Authorities}",
                string.Join(", ", _trustedAuthorities));
        }
    }

    /// <summary>
    /// Whether a sign-in authenticated by this authority may be attached to an existing account on
    /// the strength of a verified email. Everything not named in configuration is untrusted.
    /// </summary>
    public bool IsTrusted(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return false;
        }

        return _trustedAuthorities.Contains(LinkedIdentityKey.NormalizeAuthority(authority));
    }

    /// <summary>
    /// Why an authority cannot be trusted for automatic linking, or null when it can be. Applied when
    /// configuration is read so a mistake is reported at startup rather than silently widening who
    /// can be merged into an account.
    /// </summary>
    public static string? DescribeUntrustableAuthority(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return "the authority is empty";
        }

        if (!Uri.TryCreate(authority.Trim(), UriKind.Absolute, out var uri))
        {
            return "it is not an absolute URL";
        }

        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return "it is not https, so the tokens it issues cannot be attributed to it";
        }

        var isMultiTenantMicrosoft = MultiTenantMicrosoftPaths.Any(path =>
            uri.AbsolutePath.StartsWith(path, StringComparison.OrdinalIgnoreCase));

        if (isMultiTenantMicrosoft)
        {
            return "it accepts any Microsoft directory, whose administrators choose their users' " +
                   "email addresses, so an email match identifies nobody. Name a single Entra tenant " +
                   "authority instead";
        }

        return null;
    }
}
