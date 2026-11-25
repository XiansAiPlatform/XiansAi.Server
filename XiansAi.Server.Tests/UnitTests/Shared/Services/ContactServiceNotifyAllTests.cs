using Microsoft.Extensions.Logging.Abstractions;
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
/// Per-recipient sending itself is covered by <see cref="EmailServiceSendToEachTests"/>.
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
        _emailService
            .Setup(e => e.SendToEachAsync(TenantId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync((string? _, IReadOnlyCollection<string> to, string _, string _, bool _) => new EmailBatchResult(true, to.Count, 0));
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

    private void VerifyNothingSent() =>
        _emailService.Verify(e => e.SendToEachAsync(It.IsAny<string?>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()), Times.Never);

    [Fact]
    public async Task NotifyAllAsync_SendsToEachContactOnce()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync(
            [NewContact("a@example.com"), NewContact("b@example.com"), NewContact("A@example.com")]);

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Data);
        _emailService.Verify(e => e.SendToEachAsync(TenantId,
            It.Is<IReadOnlyCollection<string>>(to => to.SequenceEqual(new[] { "a@example.com", "b@example.com" })),
            Subject, Body, false), Times.Once);
    }

    [Fact]
    public async Task NotifyAllAsync_EmailOff_ReturnsZero()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com")]);
        _emailService
            .Setup(e => e.SendToEachAsync(TenantId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync(EmailBatchResult.Off);

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data);
    }

    [Fact]
    public async Task NotifyAllAsync_NoContacts_SendsNothing()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([]);

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Data);
        VerifyNothingSent();
    }

    [Fact]
    public async Task NotifyAllAsync_SomeRecipientsFail_ReturnsHowManyWereSent()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync(
            [NewContact("bad@example.com"), NewContact("good@example.com")]);
        _emailService
            .Setup(e => e.SendToEachAsync(TenantId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync(new EmailBatchResult(true, 1, 1));

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Data);
    }

    [Fact]
    public async Task NotifyAllAsync_AllRecipientsFail_ReturnsError()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com")]);
        _emailService
            .Setup(e => e.SendToEachAsync(TenantId, It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<string>(), It.IsAny<string>(), false))
            .ReturnsAsync(new EmailBatchResult(true, 0, 1));

        var result = await CreateService().NotifyAllAsync(TenantId, Subject, Body);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task NotifyAllAsync_SubjectWithLineBreaks_IsSentOnOneLine()
    {
        _contacts.Setup(r => r.ListAsync(TenantId, null)).ReturnsAsync([NewContact("a@example.com")]);

        await CreateService().NotifyAllAsync(TenantId, "Plugin failed\r\nBcc: x@evil.com", Body);

        _emailService.Verify(e => e.SendToEachAsync(TenantId, It.IsAny<IReadOnlyCollection<string>>(),
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
        VerifyNothingSent();
    }

    [Fact]
    public async Task NotifyAllAsync_BodyTooLong_IsBadRequest()
    {
        var result = await CreateService().NotifyAllAsync(TenantId, Subject, new string('x', ContactService.MaxBodyLength + 1));

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
    }
}
