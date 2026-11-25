using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// Covers sending one email per recipient: the SMTP settings are looked up once, a failed recipient
/// doesn't stop the others, and only a few SMTP connections are open at a time.
/// </summary>
public class EmailServiceSendToEachTests
{
    private const string TenantId = "acme";
    private const string Subject = "Subject";
    private const string Body = "Body";

    private readonly Mock<ITenantSmtpConfigRepository> _smtpConfigs = new();
    private readonly Mock<ISmtpEmailSender> _smtpSender = new();

    public EmailServiceSendToEachTests()
    {
        _smtpConfigs.Setup(r => r.GetAsync(TenantId)).ReturnsAsync(new TenantSmtpConfig
        {
            TenantId = TenantId,
            Host = "smtp.example.com",
            SenderEmail = "alerts@example.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "admin",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "admin",
        });
    }

    private EmailService CreateService(SmtpSettings? serverSmtp = null) =>
        new(_smtpConfigs.Object, _smtpSender.Object, NullLogger<EmailService>.Instance, serverSmtp);

    private static bool OnlyRecipient(IEnumerable<string> to, string recipient) => to.SequenceEqual(new[] { recipient });

    [Fact]
    public async Task SendToEachAsync_SendsOneEmailPerRecipient_LooksUpSettingsOnce()
    {
        var result = await CreateService().SendToEachAsync(TenantId, ["a@example.com", "b@example.com", "c@example.com"], Subject, Body);

        Assert.Equal(new EmailBatchResult(true, 3, 0), result);
        foreach (var recipient in new[] { "a@example.com", "b@example.com", "c@example.com" })
        {
            _smtpSender.Verify(s => s.SendAsync(It.Is<SmtpSettings>(x => x.Host == "smtp.example.com"),
                It.Is<IEnumerable<string>>(to => OnlyRecipient(to, recipient)), Subject, Body, false, default), Times.Once);
        }
        _smtpConfigs.Verify(r => r.GetAsync(TenantId), Times.Once);
    }

    [Fact]
    public async Task SendToEachAsync_EmailOff_SendsNothing()
    {
        _smtpConfigs.Setup(r => r.GetAsync(TenantId)).ReturnsAsync((TenantSmtpConfig?)null);

        var result = await CreateService(serverSmtp: null).SendToEachAsync(TenantId, ["a@example.com"], Subject, Body);

        Assert.Equal(EmailBatchResult.Off, result);
        _smtpSender.Verify(s => s.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SendToEachAsync_OneRecipientFails_OthersStillSent()
    {
        _smtpSender.Setup(s => s.SendAsync(It.IsAny<SmtpSettings>(), It.Is<IEnumerable<string>>(to => OnlyRecipient(to, "bad@example.com")),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable, "rejected"));

        var result = await CreateService().SendToEachAsync(TenantId, ["bad@example.com", "good@example.com"], Subject, Body);

        Assert.Equal(new EmailBatchResult(true, 1, 1), result);
    }

    [Fact]
    public async Task SendToEachAsync_Cancelled_IsNotSwallowed()
    {
        _smtpSender.Setup(s => s.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            CreateService().SendToEachAsync(TenantId, ["a@example.com", "b@example.com"], Subject, Body));
    }

    [Fact]
    public async Task SendToEachAsync_KeepsAtMostMaxParallelSendsOpen()
    {
        var counter = new object();
        var open = 0;
        var maxOpen = 0;
        _smtpSender.Setup(s => s.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<IEnumerable<string>>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                lock (counter)
                {
                    open++;
                    maxOpen = Math.Max(maxOpen, open);
                }
                await Task.Delay(20);
                lock (counter)
                {
                    open--;
                }
            });
        var recipients = Enumerable.Range(0, 20).Select(i => $"user{i}@example.com").ToList();

        var result = await CreateService().SendToEachAsync(TenantId, recipients, Subject, Body);

        Assert.Equal(20, result.Sent);
        Assert.InRange(maxOpen, 2, EmailService.MaxParallelSends);
    }
}
