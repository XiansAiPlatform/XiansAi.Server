using Shared.Auth;

namespace Features.UserApi.Utils;

/// <summary>
/// The participant id a request acts on, plus the id previously used for the same caller.
/// </summary>
/// <param name="ParticipantId">The id to read and write threads under.</param>
/// <param name="LegacyParticipantId">
/// The id threads were stored under before participant ids were split out from the canonical
/// `provider|subject` login id, or null when there is nothing to fall back to.
/// </param>
public readonly record struct ResolvedParticipant(string ParticipantId, string? LegacyParticipantId);

public static class ParticipantIdResolver
{
    /// <summary>
    /// Resolves the participant id for a request. An id naming someone else wins; naming yourself,
    /// by any of the identifiers you answer to, is the same as naming no one and yields your own
    /// participant id.
    ///
    /// That equivalence is what lets a client keep sending an email address whose owner is not
    /// unique. The address cannot namespace threads — two accounts hold it, and one namespace would
    /// merge their conversations — but it still identifies the caller, so it resolves to the id
    /// they were actually issued rather than being refused.
    ///
    /// Ids are lowercased because participant ids are frequently email addresses, and callers are
    /// inconsistent about casing.
    ///
    /// When the resolved id is the caller's own, <see cref="ResolvedParticipant.LegacyParticipantId"/>
    /// carries the canonical login id that their threads were stored under before the two ids were
    /// split apart, so read paths can fall back to it. This is transitional: new messages accumulate
    /// under the new id, so the fallback stops applying once a thread exists under it.
    ///
    /// This does not check whether the caller is allowed to act as the requested participant — use
    /// <see cref="TryResolve"/> on request paths.
    /// </summary>
    public static ResolvedParticipant Resolve(string? requestedParticipantId, ITenantContext tenantContext)
    {
        var ownParticipantId = (tenantContext.ParticipantId ?? string.Empty).ToLowerInvariant();
        var canonicalLoginId = (tenantContext.LoggedInUser ?? string.Empty).ToLowerInvariant();

        var namesSomeoneElse =
            !string.IsNullOrEmpty(requestedParticipantId) &&
            !NamesSelf(requestedParticipantId, tenantContext);

        var participantId = namesSomeoneElse
            ? requestedParticipantId!.ToLowerInvariant()
            : ownParticipantId;

        // The fallback is only meaningful for the caller's own threads, and only when the two ids
        // actually differ.
        var legacyApplies =
            string.Equals(participantId, ownParticipantId, StringComparison.Ordinal) &&
            !string.Equals(ownParticipantId, canonicalLoginId, StringComparison.Ordinal) &&
            canonicalLoginId.Length > 0;

        return new ResolvedParticipant(participantId, legacyApplies ? canonicalLoginId : null);
    }

    /// <summary>
    /// <see cref="Resolve"/> plus an ownership check. Returns false when the caller may not act as
    /// the requested participant, in which case <paramref name="participant"/> is not meaningful.
    /// </summary>
    public static bool TryResolve(
        string? requestedParticipantId,
        ITenantContext tenantContext,
        out ResolvedParticipant participant)
    {
        participant = Resolve(requestedParticipantId, tenantContext);

        // A defaulted id is the caller's own by construction, so only an explicit one can name
        // someone else.
        if (string.IsNullOrEmpty(requestedParticipantId))
        {
            return true;
        }

        return CanActAs(requestedParticipantId, tenantContext);
    }

    /// <summary>
    /// Whether the caller may act as <paramref name="participantId"/>.
    ///
    /// API keys are tenant-scoped service credentials held by trusted callers that legitimately act
    /// on behalf of many end users, so they may name any participant. A token holder may only name
    /// themselves.
    /// </summary>
    public static bool CanActAs(string? participantId, ITenantContext tenantContext)
    {
        if (tenantContext.UserType == UserType.UserApiKey)
        {
            return true;
        }

        return !string.IsNullOrEmpty(participantId) && NamesSelf(participantId, tenantContext);
    }

    /// <summary>
    /// Whether the id is one the caller answers to: their conversation participant id, canonical
    /// login id, account email, or raw provider subject — clients pass any of these depending on
    /// era and client code.
    /// </summary>
    private static bool NamesSelf(string participantId, ITenantContext tenantContext)
    {
        return MatchesOwnId(participantId, tenantContext.ParticipantId) ||
               MatchesOwnId(participantId, tenantContext.LoggedInUser) ||
               MatchesOwnId(participantId, tenantContext.Email) ||
               MatchesOwnId(participantId, tenantContext.ProviderSubject);
    }

    private static bool MatchesOwnId(string participantId, string? ownId)
    {
        return !string.IsNullOrEmpty(ownId) &&
               ownId.Equals(participantId, StringComparison.OrdinalIgnoreCase);
    }
}
