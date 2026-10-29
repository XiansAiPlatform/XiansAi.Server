namespace Shared.Services.Privacy;

/// <summary>
/// Reports why a request cannot be processed. The message must not contain the person's identifiers, because it is
/// stored on the request.
/// </summary>
public sealed class PrivacyRequestException(string message) : Exception(message);

public static class PrivacyErrorText
{
    /// <summary>The text to store on a request for a failure. Only a <see cref="PrivacyRequestException"/> keeps its message.</summary>
    // Driver and framework messages can quote the values they failed on, such as the email in a duplicate key error.
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception is PrivacyRequestException ? exception.Message : exception.GetType().FullName!;
    }
}
