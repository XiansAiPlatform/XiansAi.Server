using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Shared.Providers;

/// <summary>
/// How the SMTP connection is secured.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SmtpSecurity
{
    /// <summary>Plain connection, no TLS.</summary>
    None,
    /// <summary>Connect in plain text, then upgrade with STARTTLS (usually port 587).</summary>
    StartTls,
    /// <summary>TLS from the start of the connection (usually port 465).</summary>
    SslOnConnect,
    /// <summary>Let MailKit decide based on the port.</summary>
    Auto
}

/// <summary>
/// Connection and sender details for an SMTP server. Used for both the server default
/// (<c>Email:Smtp:*</c>) and per-tenant overrides.
/// </summary>
public class SmtpSettings
{
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string SenderEmail { get; set; } = string.Empty;
    public string? SenderName { get; set; }

    /// <summary>
    /// Returns the first validation error, or null when the settings are usable.
    /// </summary>
    public string? Validate()
    {
        if (string.IsNullOrWhiteSpace(Host))
            return "host is required";
        if (Port is < 1 or > 65535)
            return "port must be between 1 and 65535";
        if (!IsValidEmail(SenderEmail))
            return "senderEmail must be a valid email address";
        if (!string.IsNullOrEmpty(Password) && string.IsNullOrWhiteSpace(Username))
            return "username is required when a password is set";
        return null;
    }

    /// <summary>
    /// True for a single plain address such as <c>user@example.com</c>.
    /// </summary>
    public static bool IsValidEmail([NotNullWhen(true)] string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && System.Net.Mail.MailAddress.TryCreate(value.Trim(), out var address)
            && address.Address == value.Trim();
    }
}
