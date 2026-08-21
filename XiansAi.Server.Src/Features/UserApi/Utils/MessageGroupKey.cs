using System.Text;

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
    /// Prefixes any separator that occurs inside a part, so the separators that
    /// actually divide the key stay distinguishable. Without this, a participant id
    /// containing the separator (for example the "provider|subject" form used when
    /// falling back to the logged-in user) could still collide with a different
    /// combination of identifiers.
    /// </summary>
    private const char EscapeChar = '\\';

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
            Escape(workflowId),
            Escape(participantId),
            Escape(tenantId));
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
            Escape(workflowId),
            Escape(tenantId));
    }

    /// <summary>
    /// Trims a part and escapes the characters that carry structural meaning, making
    /// the encoding of each part reversible and therefore collision free.
    ///
    /// Missing identifiers become empty parts rather than throwing, so a single
    /// malformed message cannot stop the change stream that fans messages out. Such a
    /// key simply matches no real subscriber.
    /// </summary>
    private static string Escape(string? value)
    {
        var trimmed = value?.Trim() ?? string.Empty;

        if (trimmed.IndexOf(EscapeChar) < 0 && trimmed.IndexOf(Separator) < 0)
        {
            return trimmed;
        }

        var escaped = new StringBuilder(trimmed.Length + 8);
        foreach (var character in trimmed)
        {
            // The escape character must itself be escaped, otherwise a literal "\|"
            // would be indistinguishable from an escaped separator.
            if (character == EscapeChar || character == Separator)
            {
                escaped.Append(EscapeChar);
            }

            escaped.Append(character);
        }

        return escaped.ToString();
    }
}
