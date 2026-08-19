using Shared.Data.Models;
using Shared.Repositories;

namespace Features.AgentApi.Auth;

/// <summary>
/// What a certificate naming one user account acts as. Delegates to
/// <see cref="EmailIdentityResolution"/> so the user-id path and the email path cannot
/// drift: a locked-out account is unusable, and only an approved membership of the
/// certificate's tenant contributes roles.
/// </summary>
public static class CertificateUserAccess
{
    public const string LockedOutError = "User account is locked out";
    public const string InvalidUserError = "Invalid user ID";

    /// <summary>
    /// The identity this one account authenticates as, or an error when it cannot.
    /// </summary>
    public static (string? Error, EmailIdentityResolution? Identity) Resolve(
        User user, string tenantId, ILogger logger)
    {
        var identity = EmailIdentityResolution.From(
            string.IsNullOrWhiteSpace(user.Email) ? user.UserId : user.Email,
            new[] { user },
            tenantId,
            logger);

        if (identity != null)
        {
            return (null, identity);
        }

        return (user.IsLockedOut ? LockedOutError : InvalidUserError, null);
    }
}
