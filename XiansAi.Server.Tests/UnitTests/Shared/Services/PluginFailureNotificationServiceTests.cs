using Microsoft.Extensions.Logging.Abstractions;
using MailKit.Net.Smtp;
using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// Covers who gets a plugin failure alert, when it is skipped, and what it says.
/// </summary>
public class PluginFailureNotificationServiceTests
{
    private const string TenantId = "acme";

    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IEmailService> _emailService = new();

    public PluginFailureNotificationServiceTests()
    {
        _emailService.Setup(e => e.IsEnabledAsync(TenantId)).ReturnsAsync(true);
    }

    private PluginFailureNotificationService CreateService() => new(
        _contacts.Object,
        _emailService.Object,
        NullLogger<PluginFailureNotificationService>.Instance);

    private static Contact NewContact(string email) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        TenantId = TenantId,
        Name = email,
        Email = email,
        CreatedBy = "admin",
    };

    private static PluginFailureNotification Notification(string? error = "Budget exceeded") => new()
    {
        AgentName = "Xianix AI-DLC Agent",
        Plugins = ["pr-reviewer"],
        Repository = "org/repo",
        Trigger = "PR #42",
        ExecutionId = "abc123",
        Error = error,
    };

    [Fact]
    public async Task NotifyAsync_EmailsEachContactSeparately()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync(
            [NewContact("a@example.com"), NewContact("b@example.com"), NewContact("A@example.com")]);

        var result = await CreateService().NotifyAsync(TenantId, Notification());

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data);
        _emailService.Verify(e => e.SendEmailAsync(TenantId, "a@example.com", It.IsAny<string>(), It.IsAny<string>(), false), Times.Once);
        _emailService.Verify(e => e.SendEmailAsync(TenantId, "b@example.com", It.IsAny<string>(), It.IsAny<string>(), false), Times.Once);
    }

    [Fact]
    public async Task NotifyAsync_EmailOff_SkipsWithoutReadingContacts()
    {
        _emailService.Setup(e => e.IsEnabledAsync(TenantId)).ReturnsAsync(false);

        var result = await CreateService().NotifyAsync(TenantId, Notification());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data);
        _contacts.Verify(r => r.ListAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAsync_NoContacts_SendsNothing()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([]);

        var result = await CreateService().NotifyAsync(TenantId, Notification());

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data);
        _emailService.Verify(e => e.SendEmailAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAsync_OneRecipientFails_OthersStillSent()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync(
            [NewContact("bad@example.com"), NewContact("good@example.com")]);
        _emailService.Setup(e => e.SendEmailAsync(TenantId, "bad@example.com", It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "rejected"));

        var result = await CreateService().NotifyAsync(TenantId, Notification());

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data);
    }

    [Fact]
    public async Task NotifyAsync_AllRecipientsFail_ReturnsError()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com")]);
        _emailService.Setup(e => e.SendEmailAsync(TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new IOException("smtp down"));

        var result = await CreateService().NotifyAsync(TenantId, Notification());

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task NotifyAsync_Cancelled_IsNotSwallowed()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com"), NewContact("b@example.com")]);
        _emailService.Setup(e => e.SendEmailAsync(TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService().NotifyAsync(TenantId, Notification()));
    }

    [Fact]
    public async Task NotifyAsync_MissingTenant_IsBadRequest()
    {
        var result = await CreateService().NotifyAsync("", Notification());

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public void BuildSubject_NamesPluginAndRepository_OnOneLine()
    {
        var subject = PluginFailureNotificationService.BuildSubject(Notification() with { Repository = "org/repo\r\nBcc: x@evil.com" });

        Assert.StartsWith("[Xians] Plugin failed: pr-reviewer (org/repo", subject);
        Assert.DoesNotContain('\n', subject);
        Assert.DoesNotContain('\r', subject);
    }

    [Fact]
    public void BuildBody_IncludesContextAndKeepsTheTailOfLongErrors()
    {
        var error = new string('x', PluginFailureNotificationService.MaxErrorLength + 100) + "root cause";

        var body = PluginFailureNotificationService.BuildBody(TenantId, Notification(error));

        Assert.Contains("Tenant: acme", body);
        Assert.Contains("Plugin(s): pr-reviewer", body);
        Assert.Contains("Trigger: PR #42", body);
        Assert.Contains("Execution ID: abc123", body);
        Assert.Contains("root cause", body);
        Assert.DoesNotContain(new string('x', PluginFailureNotificationService.MaxErrorLength + 1), body);
    }

    [Fact]
    public void BuildBody_NoError_SaysSo()
    {
        var body = PluginFailureNotificationService.BuildBody(TenantId, Notification(error: null));

        Assert.Contains("No error details were reported.", body);
    }
}
