namespace Shared.Exceptions;

/// <summary>
/// Thrown when a caller supplies a workflow identifier that cannot be resolved into a
/// workflow id for the current tenant. This is a caller mistake, not a server fault,
/// so it is surfaced as a 400 with the full explanation.
/// </summary>
public class InvalidWorkflowIdentifierException : Exception
{
    /// <summary>
    /// The identifier the caller supplied, echoed back for diagnostics. Null when no
    /// identifier was supplied at all, which is itself one of the rejected cases.
    /// </summary>
    public string? Identifier { get; }

    public InvalidWorkflowIdentifierException(string? identifier, string message)
        : base(message)
    {
        Identifier = identifier;
    }
}
