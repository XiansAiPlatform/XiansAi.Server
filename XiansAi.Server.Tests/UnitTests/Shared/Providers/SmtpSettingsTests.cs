using Microsoft.Extensions.Logging.Abstractions;
using Shared.Providers;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Providers;

public class SmtpSettingsTests
{
    private static SmtpSettings ValidSettings() => new()
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "mailer",
        Password = "secret",
        SenderEmail = "noreply@example.com"
    };

    [Fact]
    public void Validate_ValidSettings_ReturnsNull()
    {
        Assert.Null(ValidSettings().Validate());
    }

    [Fact]
    public void Validate_NoCredentials_ReturnsNull()
    {
        var settings = ValidSettings();
        settings.Username = null;
        settings.Password = null;

        Assert.Null(settings.Validate());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankHost_ReturnsError(string host)
    {
        var settings = ValidSettings();
        settings.Host = host;

        Assert.Equal("host is required", settings.Validate());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Validate_PortOutOfRange_ReturnsError(int port)
    {
        var settings = ValidSettings();
        settings.Port = port;

        Assert.Equal("port must be between 1 and 65535", settings.Validate());
    }

    [Fact]
    public void Validate_InvalidSenderEmail_ReturnsError()
    {
        var settings = ValidSettings();
        settings.SenderEmail = "not-an-email";

        Assert.Equal("senderEmail must be a valid email address", settings.Validate());
    }

    [Fact]
    public void Validate_PasswordWithoutUsername_ReturnsError()
    {
        var settings = ValidSettings();
        settings.Username = " ";

        Assert.Equal("username is required when a password is set", settings.Validate());
    }

    [Theory]
    [InlineData("user@example.com", true)]
    [InlineData(" user@example.com ", true)]
    [InlineData("Name <user@example.com>", false)]
    [InlineData("a@b.com, c@d.com", false)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidEmail_ReturnsExpected(string? value, bool expected)
    {
        Assert.Equal(expected, SmtpSettings.IsValidEmail(value));
    }

    [Fact]
    public async Task SmtpEmailSender_InvalidRecipient_ThrowsBeforeConnecting()
    {
        var sender = new SmtpEmailSender(NullLogger<SmtpEmailSender>.Instance);

        // The host does not exist; reaching the connect step would surface a socket error instead.
        var settings = ValidSettings();
        settings.Host = "smtp.invalid";

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            sender.SendAsync(settings, ["ok@example.com", "bad-address"], "subject", "body", isHtml: false));
        Assert.Contains("bad-address", ex.Message);
    }

    [Fact]
    public async Task SmtpEmailSender_NoRecipients_ThrowsBeforeConnecting()
    {
        var sender = new SmtpEmailSender(NullLogger<SmtpEmailSender>.Instance);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            sender.SendAsync(ValidSettings(), [" "], "subject", "body", isHtml: false));
    }
}
