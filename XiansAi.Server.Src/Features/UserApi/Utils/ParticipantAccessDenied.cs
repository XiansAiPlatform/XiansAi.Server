using Shared.Utils;

namespace Features.UserApi.Utils;

/// <summary>
/// The response for a request that names a participant the caller is not allowed to act as.
/// </summary>
public static class ParticipantAccessDenied
{
    /// <summary>
    /// Logs the rejected attempt and returns a 403. The detail deliberately does not say whether the
    /// participant exists, so this cannot be used to enumerate participants.
    /// </summary>
    public static IResult ToResult(string operation, string? requestedParticipantId, ILogger logger)
    {
        logger.LogWarning(
            "{Operation} called with a participantId the caller may not act as: {ParticipantId}",
            operation,
            LogSanitizer.Sanitize(requestedParticipantId));

        return Results.Problem(
            title: "Forbidden",
            detail: "The supplied participantId does not belong to the authenticated caller.",
            statusCode: StatusCodes.Status403Forbidden);
    }
}
