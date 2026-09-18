using Microsoft.Extensions.Logging;
using Shared.Auth;
using Shared.Data.Models.Validation;
using Shared.Utils;
using System.Text.RegularExpressions;

namespace Features.AdminApi.Auth;

/// <summary>
/// Applies an optional <c>X-On-Behalf-Of</c> header to <see cref="ITenantContext.ParticipantId"/>
/// so trusted Admin API clients (for example Agent Studio) can attribute audit rows to the human
/// signed into their UI, while <see cref="ITenantContext.LoggedInUser"/> remains the API-key owner.
///
/// The header is an attribution assertion, not impersonation: it is accepted only for
/// <see cref="UserType.UserApiKey"/> callers, it does not change authorization, and a missing or
/// invalid value is ignored so the participant id continues to fall back to the key owner.
/// </summary>
public static class AdminOnBehalfOfBinder
{
    public const string HeaderName = "X-On-Behalf-Of";

    /// <summary>
    /// Matches <see cref="Shared.Data.Models.AuditLogEntry.ParticipantId"/>'s maximum stored length.
    /// </summary>
    public const int MaxLength = 200;

    private static readonly Regex AllowedIdentity = new(
        ValidationHelpers.UnicodeSafeNamePattern,
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static void Apply(HttpRequest? request, ITenantContext tenantContext, ILogger logger)
    {
        if (request == null || tenantContext.UserType != UserType.UserApiKey)
        {
            return;
        }

        var raw = request.Headers[HeaderName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        var sanitized = ValidationHelpers.SanitizeString(raw);
        if (string.IsNullOrEmpty(sanitized) || sanitized.Length > MaxLength || !AllowedIdentity.IsMatch(sanitized))
        {
            logger.LogWarning(
                "Ignoring {Header} header: value is empty after sanitization, exceeds {MaxLength} characters, or contains disallowed characters",
                HeaderName,
                MaxLength);
            return;
        }

        tenantContext.ParticipantId = sanitized;
        logger.LogDebug(
            "AdminApi on-behalf-of identity set: ParticipantId={ParticipantId}, LoggedInUser={LoggedInUser}",
            LogSanitizer.RedactUserId(sanitized),
            LogSanitizer.RedactUserId(tenantContext.LoggedInUser));
    }
}
