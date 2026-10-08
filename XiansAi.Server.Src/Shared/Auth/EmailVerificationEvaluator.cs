using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Services;

namespace Shared.Auth;

/// <param name="Admitted">Whether <paramref name="Email"/> may be used to find a user.</param>
/// <param name="Email">The address to look the user up by. Null when not admitted.</param>
/// <param name="Reason">Reason.</param>
public sealed record EmailVerificationResult(bool Admitted, string? Email, string Reason);

/// <summary>
/// Decides whether a token's email may identify a user.
/// </summary>
public static class EmailVerificationEvaluator
{
    // Only where providers put the sign-in address. upn and preferred_username are left out
    // because the verify claims do not describe them.
    private static readonly string[] VerifiedEmailClaims = ["email", "emails", "signInNames.emailAddress", "emailAddress"];

    public static EmailVerificationResult Evaluate(EmailVerificationRule? rule, JsonWebToken jwt)
    {
        if (rule == null && ClaimMatches(jwt, "email_verified", "false"))
        {
            return new(false, null, "provider marked the email unverified");
        }

        if (rule == null || rule.AllowUnverifiedEmail)
        {
            return new(true, Normalize(OidcTokenInspector.GetEmail(jwt)), "email verification not required");
        }

        var verifyClaims = rule.VerifyClaims ?? [];
        var trustedValues = (rule.TrustedValues ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToList();
        var trustedClaim = string.IsNullOrWhiteSpace(rule.TrustedClaim) || trustedValues.Count == 0
            ? null
            : rule.TrustedClaim.Trim();

        var configError = DescribeConfigError(verifyClaims, trustedClaim);
        if (configError != null)
        {
            return new(false, null, "invalid emailVerification config: " + configError);
        }

        if (verifyClaims.Count > 0)
        {
            var email = GetVerifiableEmail(jwt);
            var failed = verifyClaims
                .Where(check => !ClaimMatches(jwt, check.Claim, OidcTokenInspector.ToComparableString(check.Value)!))
                .Select(check => check.Claim)
                .ToList();

            // The verify claims describe the email claim, so the lookup uses exactly that address.
            if (email != null && failed.Count == 0)
            {
                return new(true, email, "verified email");
            }

            if (trustedClaim == null)
            {
                return new(false, null, email == null
                    ? "no email claim in token"
                    : "claim check failed: " + string.Join(", ", failed));
            }
        }

        if (ClaimValues(jwt, trustedClaim!).Any(value => trustedValues.Contains(value, StringComparer.OrdinalIgnoreCase)))
        {
            return new(true, Normalize(OidcTokenInspector.GetEmail(jwt)), "trusted " + trustedClaim);
        }

        var emailNote = verifyClaims.Count > 0 ? "email not verified and " : "";
        return new(false, null, $"{emailNote}{trustedClaim} not trusted");
    }

    private static string? DescribeConfigError(List<EmailVerificationClaim> verifyClaims, string? trustedClaim)
    {
        foreach (var check in verifyClaims)
        {
            if (string.IsNullOrWhiteSpace(check.Claim) ||
                string.IsNullOrWhiteSpace(OidcTokenInspector.ToComparableString(check.Value)))
            {
                return $"verifyClaims entry '{check.Claim}' needs both claim and value";
            }
        }

        return verifyClaims.Count == 0 && trustedClaim == null
            ? "verification is required but neither verifyClaims nor trustedClaim/trustedValues is set"
            : null;
    }

    private static string? GetVerifiableEmail(JsonWebToken jwt)
    {
        foreach (var claimType in VerifiedEmailClaims)
        {
            var value = Normalize(ClaimValues(jwt, claimType).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)));
            if (value != null)
            {
                return value;
            }
        }

        return null;
    }

    private static string? Normalize(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    // A JSON array claim surfaces as one claim per element.
    private static IEnumerable<string> ClaimValues(JsonWebToken jwt, string claimType) =>
        jwt.Claims.Where(claim => claim.Type == claimType).Select(claim => claim.Value);

    private static bool ClaimMatches(JsonWebToken jwt, string claimType, string expected)
    {
        bool? expectedBoolean = expected.Trim().ToLowerInvariant() switch
        {
            "true" => true,
            "false" => false,
            _ => null
        };

        return ClaimValues(jwt, claimType).Any(actual => expectedBoolean.HasValue
            ? ToBoolean(actual) == expectedBoolean
            : string.Equals(actual, expected, StringComparison.Ordinal));
    }

    // Boolean conversion to handle "true"/"false" and "1"/"0".
    private static bool? ToBoolean(string value) => value.Trim().ToLowerInvariant() switch
    {
        "true" or "1" => true,
        "false" or "0" => false,
        _ => null
    };
}
