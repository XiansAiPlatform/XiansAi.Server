using System.Security.Claims;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Services;

namespace Shared.Auth;

/// <summary>
/// Where a subject was read from, so that the caller can tell a claim OIDC guarantees to be stable
/// apart from a fallback that a user may be able to change at their identity provider.
/// </summary>
public readonly record struct ResolvedSubject(string? Value, string? ClaimType, bool IsStableClaim);

/// <summary>
/// Reads identity out of a validated token and applies the per-provider rule checks.
///
/// Claims are read straight off the parsed token rather than through a <see cref="ClaimsPrincipal"/>,
/// so no inbound claim-type mapping is involved and the raw JWT claim names are what these lookups
/// see.
/// </summary>
public static class OidcTokenInspector
{
    /// <summary>
    /// Claims OIDC guarantees to identify the subject stably: <c>sub</c>, and <c>oid</c> which is
    /// the equivalent immutable object id in Entra tokens.
    /// </summary>
    private static readonly string[] StableSubjectClaims = ["sub", "oid"];

    /// <summary>
    /// Everything else a subject has historically been read from. These are mutable at many
    /// identity providers, so a user who can edit their own profile can change what they resolve
    /// to here.
    /// </summary>
    private static readonly string[] FallbackSubjectClaims =
        [ClaimTypes.NameIdentifier, "preferred_username", "email", "upn", "nameid", "name"];

    /// <summary>
    /// Claim names that must not be nominated as <c>userIdClaim</c>. Distinct from
    /// <see cref="FallbackSubjectClaims"/> only in that it also names B2C's <c>emails</c> and
    /// Entra's <c>unique_name</c>, which are not historical fallbacks but are equally mutable and
    /// equally unsuitable as a stable user id.
    /// </summary>
    private static readonly HashSet<string> MutableSubjectClaims = new(StringComparer.OrdinalIgnoreCase)
    {
        "email",
        ClaimTypes.Email,
        "emails",
        "emailAddress",
        "preferred_username",
        "upn",
        "name",
        ClaimTypes.Name,
        "nameid",
        "unique_name",
        ClaimTypes.NameIdentifier
    };

    /// <summary>
    /// Resolves the subject. A claim the provider nominates explicitly always wins and is treated
    /// as deliberate; otherwise the stable claims are tried first, and the mutable fallbacks only
    /// when <paramref name="allowFallbackClaims"/> permits.
    /// </summary>
    public static ResolvedSubject ResolveSubject(
        OidcProviderRule providerRule,
        JsonWebToken jwt,
        bool allowFallbackClaims)
    {
        var configured = GetConfiguredSubjectClaims(providerRule);
        if (configured.Count > 0)
        {
            var (value, claimType) = FirstPresent(jwt, configured);
            return new ResolvedSubject(value, claimType, IsStableClaim: true);
        }

        var stable = FirstPresent(jwt, StableSubjectClaims);
        if (stable.Value != null)
        {
            return new ResolvedSubject(stable.Value, stable.ClaimType, IsStableClaim: true);
        }

        if (!allowFallbackClaims)
        {
            return new ResolvedSubject(null, null, IsStableClaim: true);
        }

        var fallback = FirstPresent(jwt, FallbackSubjectClaims);
        return new ResolvedSubject(fallback.Value, fallback.ClaimType, IsStableClaim: false);
    }

    /// <summary>
    /// Why <paramref name="claimName"/> must not be used as a subject claim, or null when it may.
    ///
    /// Only known-mutable names are refused. An unknown name is allowed so a directory's genuine
    /// immutable custom claim is not blocked; the refusal is about addresses and display names,
    /// not about every claim that is not <c>sub</c> or <c>oid</c>.
    /// </summary>
    public static string? DescribeMutableSubjectClaim(string? claimName)
    {
        if (string.IsNullOrWhiteSpace(claimName))
        {
            return null;
        }

        if (!MutableSubjectClaims.Contains(claimName.Trim()))
        {
            return null;
        }

        return $"'{claimName.Trim()}' is mutable at many identity providers — the stored user id " +
               "becomes that value, which will not match the portal's stable subject (sub/oid), " +
               "and changing the claim later strands conversation history keyed on the previous id";
    }

    /// <summary>
    /// The claim(s) a provider nominates for the subject, via either <c>userIdClaim</c> for a
    /// single name or <c>userIdClaims</c> for a comma-separated preference order. Empty when the
    /// provider relies on the default stable-then-fallback resolution.
    /// </summary>
    public static IReadOnlyList<string> GetConfiguredSubjectClaims(OidcProviderRule providerRule)
    {
        var settings = providerRule.ProviderSpecificSettings;
        if (settings == null)
        {
            return Array.Empty<string>();
        }

        if (settings.TryGetValue("userIdClaim", out var single))
        {
            var value = single?.ToString();
            return string.IsNullOrWhiteSpace(value) ? Array.Empty<string>() : new[] { value };
        }

        if (settings.TryGetValue("userIdClaims", out var list))
        {
            var value = list?.ToString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// The address to record for display and contact.
    ///
    /// <c>emails</c> is Azure AD B2C's array spelling. Some B2C user flows (including custom
    /// policies) instead issue a single <c>emailAddress</c> string — without that spelling a B2C
    /// sign-in produces a user record with no address at all, which cannot be matched to an existing
    /// account and so silently becomes a second one.
    /// The <c>emails</c> claim is a JSON array; the parsed token exposes each entry separately and the
    /// first is taken, matching B2C's own treatment of it as the sign-in address.
    /// </summary>
    public static string? GetEmail(JsonWebToken jwt) =>
        FirstPresent(jwt, ["email", ClaimTypes.Email, "emails", "emailAddress", "preferred_username", "upn"]).Value;

    /// <summary>
    /// The display name to record.
    ///
    /// A directory that issues the given and family names separately carries no single name claim,
    /// so without composing one the record is created with no name at all. Both the OIDC spellings
    /// and the camelCase spellings some directories use are accepted.
    ///
    /// The composed name is preferred over <c>preferred_username</c>, which is usually an address
    /// and makes a poor display name when the person's actual name is in the same token.
    /// </summary>
    public static string? GetName(JsonWebToken jwt) =>
        FirstPresent(jwt, ["name", ClaimTypes.Name]).Value
        ?? ComposeName(jwt)
        ?? FirstPresent(jwt, ["preferred_username"]).Value;

    private static string? ComposeName(JsonWebToken jwt)
    {
        var givenName = FirstPresent(jwt, ["given_name", ClaimTypes.GivenName, "firstName"]).Value;
        var familyName = FirstPresent(jwt, ["family_name", ClaimTypes.Surname, "lastName"]).Value;

        // Either one alone is still a better name than none.
        var parts = new[] { givenName, familyName }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim());

        var composed = string.Join(' ', parts);
        return string.IsNullOrWhiteSpace(composed) ? null : composed;
    }

    /// <summary>
    /// Checks that the token carries every scope the provider requires, or returns null when the
    /// provider requires none.
    /// </summary>
    public static string? DescribeMissingScope(OidcProviderRule providerRule, JsonWebToken jwt)
    {
        if (string.IsNullOrWhiteSpace(providerRule.Scope))
        {
            return null;
        }

        var tokenScope = GetExactClaim(jwt, "scope") ?? GetExactClaim(jwt, "scp");
        if (string.IsNullOrWhiteSpace(tokenScope))
        {
            return "Missing scope claim";
        }

        const StringSplitOptions splitOptions = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;
        var tokenScopes = tokenScope.Split(' ', splitOptions).ToHashSet(StringComparer.Ordinal);

        foreach (var required in providerRule.Scope.Split(' ', splitOptions))
        {
            if (!tokenScopes.Contains(required))
            {
                return $"Required scope missing: {required}";
            }
        }

        return null;
    }

    /// <summary>
    /// Applies the provider's custom claim checks, returning the first that fails.
    /// </summary>
    public static string? DescribeFailedClaimCheck(OidcProviderRule providerRule, JsonWebToken jwt)
    {
        if (providerRule.AdditionalClaims == null)
        {
            return null;
        }

        foreach (var check in providerRule.AdditionalClaims)
        {
            if (!EvaluateClaim(GetExactClaim(jwt, check.Claim), check))
            {
                return $"Claim check failed: {check.Claim}";
            }
        }

        return null;
    }

    public static DateTimeOffset? ExpiresAt(JsonWebToken jwt) =>
        jwt.ValidTo == DateTime.MinValue
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(jwt.ValidTo, DateTimeKind.Utc));

    private static (string? Value, string? ClaimType) FirstPresent(JsonWebToken jwt, IReadOnlyList<string> claimTypes)
    {
        foreach (var claimType in claimTypes)
        {
            var value = GetClaim(jwt, claimType);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return (value, claimType);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Reads a claim by its exact name. Used for the rule checks, where the configured name is
    /// matched against the token as written.
    /// </summary>
    private static string? GetExactClaim(JsonWebToken jwt, string claimType) =>
        jwt.TryGetClaim(claimType, out var claim) ? claim.Value : null;

    /// <summary>
    /// Reads an identity claim, preferring an exact match and falling back to a case-insensitive
    /// scan. Claim names are case-sensitive by specification, but providers have not been
    /// consistent about it and the lenient match is long-standing behaviour here.
    /// </summary>
    private static string? GetClaim(JsonWebToken jwt, string claimType)
    {
        if (jwt.TryGetClaim(claimType, out var claim))
        {
            return claim.Value;
        }

        foreach (var candidate in jwt.Claims)
        {
            if (string.Equals(candidate.Type, claimType, StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Value;
            }
        }

        return null;
    }

    private static bool EvaluateClaim(string? claimValue, CustomClaimCheck check)
    {
        if (claimValue == null) return false;

        // Support multi-type values (string, number, bool, arrays) coming from JSON as JsonElement
        // Normalize expected value(s) to string(s) for comparison
        var op = check.Op?.ToLowerInvariant();

        if (check.Value is JsonElement je && je.ValueKind == JsonValueKind.Array)
        {
            var expectedValues = new List<string>();
            foreach (var item in je.EnumerateArray())
            {
                var s = JsonElementToComparableString(item);
                if (s != null) expectedValues.Add(s);
            }

            return op switch
            {
                "equals" => expectedValues.Any(v => string.Equals(claimValue, v, StringComparison.Ordinal)),
                "not_equals" => expectedValues.All(v => !string.Equals(claimValue, v, StringComparison.Ordinal)),
                "contains" => expectedValues.Any(v => claimValue.Contains(v, StringComparison.Ordinal)),
                _ => false
            };
        }

        var expected = ToComparableString(check.Value);

        return op switch
        {
            "equals" => string.Equals(claimValue, expected, StringComparison.Ordinal),
            "not_equals" => !string.Equals(claimValue, expected, StringComparison.Ordinal),
            "contains" => expected != null && claimValue.Contains(expected, StringComparison.Ordinal),
            _ => false
        };
    }

    private static string? ToComparableString(object? value)
    {
        if (value == null) return null;
        if (value is string s) return s;
        if (value is bool b) return b ? "true" : "false";
        if (value is JsonElement je) return JsonElementToComparableString(je);
        return value.ToString();
    }

    private static string? JsonElementToComparableString(JsonElement je)
    {
        return je.ValueKind switch
        {
            JsonValueKind.String => je.GetString(),
            JsonValueKind.Number => je.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            // For objects/arrays, use raw text to allow exact matching if needed
            JsonValueKind.Object => je.GetRawText(),
            JsonValueKind.Array => je.GetRawText(),
            _ => je.GetRawText()
        };
    }
}
