namespace Features.UserApi.Utils;

/// <summary>
/// Builds the composite keys that decide which SignalR groups and SSE connections
/// are allowed to receive a conversation message.
/// </summary>
public static class MessageGroupKey
{
    /// <summary>
    /// Separates the parts of a key. Plain concatenation would be ambiguous: workflow
    /// "acme:Sales:Flow" with participant "ab" produces the same string as workflow
    /// "acme:Sales:Flowa" with participant "b", which would route a message to the
    /// wrong subscriber.
    /// </summary>
    private const char Separator = '|';

    /// <summary>
    /// Kind markers keep participant keys and tenant keys in separate namespaces so
    /// they can never match each other, whatever the identifiers contain.
    /// </summary>
    private const string ParticipantKind = "participant";
    private const string TenantKind = "tenant";

    /// <summary>
    /// Key for one participant's conversation with one workflow.
    /// </summary>
    public static string ForParticipant(string? workflowId, string? participantId, string? tenantId)
    {
        return string.Join(
            Separator,
            ParticipantKind,
            Normalize(workflowId),
            Normalize(participantId),
            Normalize(tenantId));
    }

    /// <summary>
    /// Key covering every participant's conversation with one workflow in a tenant.
    /// Anything subscribed to this key sees other participants' messages, so it must
    /// only be used where the subscriber explicitly opted in and is authorized for it.
    /// </summary>
    public static string ForTenant(string? workflowId, string? tenantId)
    {
        return string.Join(
            Separator,
            TenantKind,
            Normalize(workflowId),
            Normalize(tenantId));
    }

    /// <summary>
    /// Missing identifiers become empty parts rather than throwing, so a single
    /// malformed message cannot stop the change stream that fans messages out. Such a
    /// key simply matches no real subscriber.
    /// </summary>
    private static string Normalize(string? value)
    {
        return value?.Trim() ?? string.Empty;
    }
}
