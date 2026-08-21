using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// The single identity a credential that names an email address acts as, folded from every user
/// record holding that address.
///
/// Duplicates are legitimate: a record is keyed on the provider subject, so two identity providers
/// can each hold an account for the same person. Credentials that carry only an address — legacy
/// API keys storing an email in <c>CreatedBy</c>, certificates whose OU is an email — have no way
/// to say which of those accounts they meant, so the records are combined rather than one being
/// picked arbitrarily.
/// </summary>
/// <param name="PrimaryUserId">
/// The account the request acts as, chosen deterministically so it does not vary between requests.
/// Data scoping and ownership still resolve to this one id.
/// </param>
/// <param name="CandidateUserIds">Every record that contributed, oldest first.</param>
/// <param name="Roles">
/// The union of the contributing records' roles for the requested tenant, including
/// <see cref="SystemRoles.SysAdmin"/> when <paramref name="IsSysAdmin"/> holds.
/// </param>
/// <param name="IsSysAdmin">
/// True only when every contributing record holds the role, so it can never be assembled out of one
/// record that has it and one that does not.
/// </param>
/// <param name="IsAmbiguous">True when more than one record contributed.</param>
public sealed record EmailIdentityResolution(
    string PrimaryUserId,
    IReadOnlyList<string> CandidateUserIds,
    IReadOnlyList<string> Roles,
    bool IsSysAdmin,
    bool IsAmbiguous)
{
    /// <summary>
    /// Folds the records holding an address into the one identity to act as, or null when none of
    /// them is usable.
    /// </summary>
    /// <param name="email">The address being resolved, used only for logging.</param>
    /// <param name="records">Every user record holding the address, in any order.</param>
    /// <param name="tenantId">The tenant whose roles are being resolved.</param>
    /// <param name="logger">
    /// Records an ambiguous address, and a set that does not agree on SysAdmin.
    /// </param>
    public static EmailIdentityResolution? From(
        string email, IReadOnlyList<User> records, string tenantId, ILogger logger)
    {
        var candidates = UsableRecords(records);

        if (candidates.Count == 0)
        {
            return null;
        }

        // Only approved memberships contribute, so reaching a tenant this way still requires an
        // admin of that tenant to have granted the membership.
        var roles = candidates
            .SelectMany(user => user.TenantRoles)
            .Where(tenantRole => tenantRole.Tenant == tenantId && tenantRole.IsApproved)
            .SelectMany(tenantRole => tenantRole.Roles)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var isSysAdmin = ResolveSysAdmin(email, candidates, logger);
        if (isSysAdmin && !roles.Contains(SystemRoles.SysAdmin))
        {
            roles.Add(SystemRoles.SysAdmin);
        }

        if (candidates.Count > 1)
        {
            logger.LogWarning(
                "Email {Email} resolves to {Count} accounts [{UserIds}]; acting as {PrimaryUserId} " +
                "with their combined roles for tenant {TenantId}",
                LogSanitizer.RedactEmail(email), candidates.Count,
                LogSanitizer.Sanitize(string.Join(", ", candidates.Select(user => user.UserId))),
                LogSanitizer.RedactUserId(candidates[0].UserId), LogSanitizer.Sanitize(tenantId));
        }

        return new EmailIdentityResolution(
            PrimaryUserId: candidates[0].UserId,
            CandidateUserIds: candidates.Select(user => user.UserId).ToList(),
            Roles: roles,
            IsSysAdmin: isSysAdmin,
            IsAmbiguous: candidates.Count > 1);
    }

    /// <summary>
    /// The records an address can actually answer as: the disabled ones dropped, and the rest in a
    /// fixed order.
    ///
    /// Ordered so the identity a credential acts as does not change between requests. Unordered
    /// this is whatever the collection scan returned first, which is how the same credential could
    /// resolve to a different account on a later call.
    ///
    /// Exposed for callers that combine the records themselves because they answer a question this
    /// record does not model — one that spans every tenant rather than the one being resolved —
    /// and which must still drop disabled records and order the rest identically.
    /// </summary>
    public static IReadOnlyList<User> UsableRecords(IReadOnlyList<User> records) =>
        records
            .Where(user => !user.IsLockedOut)
            .OrderBy(user => user.CreatedAt)
            .ThenBy(user => user.UserId, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// SysAdmin is global and grants access to every tenant, so an address only carries it when
    /// every record answering to that address holds it. One record short and the answer is no: a
    /// second account at another provider is created for review and without the role, so a mixed
    /// set means nobody has yet accepted that the two are the same person.
    ///
    /// The cost is that enabling such an account without also granting it the role takes SysAdmin
    /// away from the account that had it, on the paths that name only an address. Completing the
    /// grant restores it.
    /// </summary>
    /// <param name="email">The address being resolved, used only for logging.</param>
    /// <param name="candidates">
    /// The records from <see cref="UsableRecords"/>. Passing disabled records would let one that
    /// nobody has reviewed withhold the role from the account that holds it.
    /// </param>
    /// <param name="logger">Records a set that does not agree on the role.</param>
    public static bool ResolveSysAdmin(string email, IReadOnlyList<User> candidates, ILogger logger)
    {
        // Safe on the single-record case, which is the ordinary one: the record's own flag decides.
        if (candidates.All(user => user.IsSysAdmin))
        {
            return true;
        }

        if (candidates.Any(user => user.IsSysAdmin))
        {
            logger.LogError(
                "Refusing SysAdmin for {Email}: it is held by a system administrator and by {Others}, " +
                "which have not been accepted as the same person. Grant the system administrator " +
                "role to every account holding this address, or leave the others disabled; until " +
                "then this address cannot authenticate as SysAdmin",
                LogSanitizer.RedactEmail(email),
                LogSanitizer.Sanitize(string.Join(", ", candidates
                    .Where(user => !user.IsSysAdmin)
                    .Select(user => user.UserId))));
        }

        return false;
    }
}
