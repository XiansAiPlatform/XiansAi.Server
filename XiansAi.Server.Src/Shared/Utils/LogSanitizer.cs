namespace Shared.Utils;

/// <summary>
/// Sanitizes user-supplied strings before they are written to log entries,
/// preventing log-forging / log-injection attacks (CWE-117 / cs/log-forging).
/// Newline and carriage-return characters are replaced with a space so that an
/// attacker cannot inject fake log lines by embedding line-breaks in input.
/// </summary>
public static class LogSanitizer
{
    /// <summary>
    /// Returns a sanitized copy of <paramref name="value"/> safe for logging,
    /// or <c>null</c> when the input is <c>null</c>.
    /// </summary>
    public static string? Sanitize(string? value)
    {
        if (value is null)
            return null;

        return value
            .Replace('\r', ' ')
            .Replace('\n', ' ');
    }

    /// <summary>
    /// Redacts an email address for logging to minimize PII retention
    /// (CWE-359 / cs/exposure-of-sensitive-information). The local part is
    /// masked while the domain is preserved for troubleshooting, producing
    /// the form "***@domain". Returns a placeholder for empty or malformed
    /// values so the original address is never written to a log entry.
    /// </summary>
    public static string RedactEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return "[empty]";

        var atIndex = email.IndexOf('@');
        if (atIndex < 0)
            return "***@[no-domain]";

        var domain = email[(atIndex + 1)..].Replace('\r', ' ').Replace('\n', ' ');
        return "***@" + domain;
    }
}
