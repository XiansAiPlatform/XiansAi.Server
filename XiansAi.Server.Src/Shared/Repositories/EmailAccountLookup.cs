using Shared.Data.Models;

namespace Shared.Repositories;

/// <summary>
/// The one account an address names, for operations that act on a single account rather than on the
/// combined identity <see cref="EmailIdentityResolution"/> produces.
///
/// Granting a membership or a role names one person, and an address does not always name one
/// account: two providers can each hold a record for the same address. Resolving to whichever
/// record a collection scan returned first would put a different account in the tenant than the
/// operator meant, so an ambiguous address is refused and the operator is asked for a user id.
/// </summary>
/// <param name="Account">The single record holding the address, or null when it is not exactly one.</param>
/// <param name="MatchCount">How many records hold it, including disabled ones.</param>
public sealed record EmailAccountLookup(User? Account, int MatchCount)
{
    /// <summary>More than one record holds the address, so it does not name a single account.</summary>
    public bool IsAmbiguous => MatchCount > 1;

    /// <summary>
    /// Shared wording so that every refusal points at the same way through. Disabled records are
    /// counted, so an address awaiting a system administrator review is ambiguous here even though
    /// the fold resolves it without difficulty.
    /// </summary>
    public const string AmbiguousError =
        "More than one account holds this email address, so it does not name a single person. " +
        "Use the user id of the intended account.";

    public static EmailAccountLookup From(IReadOnlyList<User>? records)
    {
        var owners = records ?? (IReadOnlyList<User>)Array.Empty<User>();
        return new EmailAccountLookup(owners.Count == 1 ? owners[0] : null, owners.Count);
    }
}
