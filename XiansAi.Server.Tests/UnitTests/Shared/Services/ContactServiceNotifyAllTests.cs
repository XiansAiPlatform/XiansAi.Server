using Microsoft.Extensions.Logging.Abstractions;
using MailKit.Net.Smtp;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// Covers who gets a contact notification, when it is skipped, and how the subject is kept safe.
/// </summary>
public class ContactServiceNotifyAllTests
{
    private const string TenantId = "acme";
    private const string Subject = "[Xians] Plugin failed: pr-reviewer (org/repo)";
    private const string Body = "A plugin run failed and did not complete.";

    private readonly Mock<IContactRepository> _contacts = new();
    private readonly Mock<IEmailService> _emailService = new();

    public ContactServiceNotifyAllTests()
    {
        _emailService.Setup(e => e.IsEnabledAsync(TenantId)).ReturnsAsync(true);
    }

    private ContactService CreateService() => new(
        _contacts.Object,
        Mock.Of<ITenantContext>(),
        _emailService.Object,
        NullLogger<ContactService>.Instance);

    private static Contact NewContact(string email) => new()
    {
        Id = Guid.NewGuid().ToString("N"),
        TenantId = TenantId,
        Name = email,
        Email = email,
        CreatedBy = "admin",
    };

    [Fact]
    public async Task NotifyAllAsync_EmailsEachContactSeparately()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync(
            [NewContact("a@example.com"), NewContact("b@example.com"), NewContact("A@example.com")]);

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data);
        _emailService.Verify(e => e.SendEmailAsync(TenantId, "a@example.com", Subject, Body, false), Times.Once);
        _emailService.Verify(e => e.SendEmailAsync(TenantId, "b@example.com", Subject, Body, false), Times.Once);
    }

    [Fact]
    public async Task NotifyAllAsync_EmailOff_SkipsWithoutReadingContacts()
    {
        _emailService.Setup(e => e.IsEnabledAsync(TenantId)).ReturnsAsync(false);

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data);
        _contacts.Verify(r => r.ListAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAllAsync_NoContacts_SendsNothing()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([]);

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data);
        _emailService.Verify(e => e.SendEmailAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAllAsync_OneRecipientFails_OthersStillSent()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync(
            [NewContact("bad@example.com"), NewContact("good@example.com")]);
        _emailService.Setup(e => e.SendEmailAsync(TenantId, "bad@example.com", It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "rejected"));

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data);
    }

    [Fact]
    public async Task NotifyAllAsync_AllRecipientsFail_ReturnsError()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com")]);
        _emailService.Setup(e => e.SendEmailAsync(TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new IOException("smtp down"));

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task NotifyAllAsync_Cancelled_IsNotSwallowed()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com"), NewContact("b@example.com")]);
        _emailService.Setup(e => e.SendEmailAsync(TenantId, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() => CreateService().NotifyAllAsync(TenantId, Subject, Body));
    }

    [Fact]
    public async Task NotifyAllAsync_SubjectWithLineBreaks_IsSentOnOneLine()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com")]);

        await CreateService().NotifyAllAsync(TenantId, "Plugin failed\r\nBcc: x@evil.com", Body);

        _emailService.Verify(e => e.SendEmailAsync(TenantId, "a@example.com",
            It.Is<string>(s => !s.Contains('\r') && !s.Contains('\n')), Body, false), Times.Once);
    }

    [Theory]
    [InlineData("", Subject, Body)]
    [InlineData(TenantId, " ", Body)]
    [InlineData(TenantId, Subject, "")]
    public async Task NotifyAllAsync_MissingInput_IsBadRequest(string tenantId, string subject, string body)
    {
        var result = await CreateService().NotifyAllAsync(tenantId, subject, body);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _emailService.Verify(e => e.SendEmailAsync(It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task NotifyAllAsync_BodyTooLong_IsBadRequest()
    {
        var result = await CreateService().NotifyAllAsync(TenantId, Subject, new string('x', ContactService.MaxBodyLength + 1));

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
    }
}
