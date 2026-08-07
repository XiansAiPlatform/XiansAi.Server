using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Auth;

/// <summary>
/// Decides which identity providers may have a sign-in attached to an existing account
/// automatically, on the strength of a verified email address alone.
///
/// This exists because the alternative — merging whenever the email matches — hands over any account
/// to anyone who can make a provider assert its address.
///
/// Configuring a provider is a separate decision from believing what it says about identity, which
/// is why this list is separate from tenant OIDC configuration even though only a SysAdmin may now
/// write either. Adding a partner's directory so their staff can sign in says nothing about whether
/// an address it emits that matches an existing account is the same person, and that inference
/// should be made deliberately rather than fall out of a provider being added.
///
/// The separation is also a matter of blast radius in both directions. Tenant OIDC config governs
/// one tenant, whereas the account an auto-link attaches to may be in any tenant or be a SysAdmin,
/// so a per-tenant record must not be able to widen a deployment-wide decision. And keeping this in
/// deployment configuration means a stolen SysAdmin token cannot extend it: that takes deploy
/// access, which is a materially harder thing to obtain.
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
    /// Providers that verify addresses but do not say so in the token, which the operator is
    /// asserting on their behalf. Azure AD B2C is the case this exists for: it issues its address as
    /// <c>emails</c> with no verification claim, so <see cref="ConfigurationKey"/> alone can never
    /// match one of its sign-ins however it is set.
    ///
    /// Naming a provider here is a stronger statement than trusting it, so it is also trusted.
    /// </summary>
    private const string UnverifiedEmailConfigurationKey = "Auth:AutoLinkProvidersWithoutVerifiedEmailClaim";

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
    private readonly HashSet<string> _authoritiesWithoutVerifiedEmailClaim;

    public IdentityAutoLinkPolicy(IConfiguration configuration, ILogger<IdentityAutoLinkPolicy> logger)
    {
        _trustedAuthorities = ReadAuthorities(configuration, ConfigurationKey, DefaultTrustedAuthorities, logger);
        _authoritiesWithoutVerifiedEmailClaim = ReadAuthorities(configuration, UnverifiedEmailConfigurationKey, [], logger);

        _trustedAuthorities.UnionWith(_authoritiesWithoutVerifiedEmailClaim);

        if (_trustedAuthorities.Count > 0)
        {
            logger.LogInformation(
                "A verified email from these providers will be attached to a matching account automatically: {Authorities}",
                string.Join(", ", _trustedAuthorities));
        }

        if (_authoritiesWithoutVerifiedEmailClaim.Count > 0)
        {
            // Worth stating plainly at startup: this is the operator standing in for a verification
            // claim the provider never sends, and an address that provider gets wrong hands over
            // the account holding it.
            logger.LogWarning(
                "These providers will be attached on an email match with no verification claim in the token, " +
                "on the operator's assertion that the directory verifies addresses out of band: {Authorities}",
                string.Join(", ", _authoritiesWithoutVerifiedEmailClaim));
        }
    }

    private static HashSet<string> ReadAuthorities(
        IConfiguration configuration, string configurationKey, string[] fallback, ILogger logger)
    {
        var authorities = configuration.GetSection(configurationKey).Get<string[]>() ?? fallback;
        var accepted = new HashSet<string>(StringComparer.Ordinal);

        foreach (var authority in authorities)
        {
            var rejection = DescribeUntrustableAuthority(authority);
            if (rejection != null)
            {
                logger.LogError("Ignoring {ConfigurationKey} entry '{Authority}': {Reason}",
                    configurationKey, LogSanitizer.Sanitize(authority), rejection);
                continue;
            }

            accepted.Add(LinkedIdentityKey.NormalizeAuthority(authority));
        }

        return accepted;
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
    /// Whether this authority's email may be acted on even though the token contains no claim saying
    /// the address was verified.
    ///
    /// This substitutes the operator's word for the provider's. It is meaningful only where every
    /// address in the directory got there under control the operator can account for — an
    /// invite-only or admin-provisioned directory, or one whose sign-up proves ownership of the
    /// address. Where a person can bring an address in from elsewhere without proving they own it,
    /// enabling this lets whoever supplies that address take over the account already holding it.
    /// </summary>
    public bool VouchesForUnverifiedEmail(string? authority)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            return false;
        }

        return _authoritiesWithoutVerifiedEmailClaim.Contains(LinkedIdentityKey.NormalizeAuthority(authority));
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
