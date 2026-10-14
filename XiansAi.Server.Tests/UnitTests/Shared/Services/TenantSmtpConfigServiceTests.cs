using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Services;

/// <summary>
/// Covers request validation, the blank-password fallback, and what the service returns to callers.
/// </summary>
public class TenantSmtpConfigServiceTests
{
    private const string TenantId = "acme";
    private const string SavedPassword = "saved-secret";

    private readonly Mock<ITenantSmtpConfigRepository> _repository = new();
    private readonly Mock<IEmailService> _emailService = new();
    private readonly Mock<ISmtpEmailSender> _smtpSender = new();

    private TenantSmtpConfigService CreateService() => new(
        _repository.Object,
        _emailService.Object,
        _smtpSender.Object,
        NullLogger<TenantSmtpConfigService>.Instance,
        Mock.Of<IWebhookEventPublisher>(),
        Mock.Of<IAuditLogService>());

    private void SetupSavedConfig() =>
        _repository.Setup(r => r.GetAsync(TenantId)).ReturnsAsync(new TenantSmtpConfig
        {
            Id = "existing",
            TenantId = TenantId,
            Host = "smtp.example.com",
            Port = 587,
            Username = "mailer",
            Password = SavedPassword,
            SenderEmail = "noreply@example.com",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "admin",
            UpdatedAt = DateTime.UtcNow,
            UpdatedBy = "admin"
        });

    private static TenantSmtpConfigRequest ValidRequest(string? password = null) => new()
    {
        Host = " smtp.example.com ",
        Port = 587,
        Username = "mailer",
        Password = password,
        SenderEmail = "noreply@example.com"
    };

    private static SendTestEmailRequest ValidTestRequest(string? password = null) => new()
    {
        Host = "smtp.example.com",
        Port = 587,
        Username = "mailer",
        Password = password,
        SenderEmail = "noreply@example.com",
        To = "admin@example.com"
    };

    [Fact]
    public async Task Upsert_BlankPassword_KeepsSavedPassword()
    {
        SetupSavedConfig();

        var result = await CreateService().UpsertAsync(TenantId, ValidRequest(password: ""), "admin");

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.UpsertAsync(TenantId,
            It.Is<SmtpSettings>(s => s.Password == SavedPassword && s.Host == "smtp.example.com"), "admin"), Times.Once);
    }

    [Fact]
    public async Task Upsert_NewPassword_ReplacesSavedPassword()
    {
        SetupSavedConfig();

        await CreateService().UpsertAsync(TenantId, ValidRequest(password: "new-secret"), "admin");

        _repository.Verify(r => r.UpsertAsync(TenantId,
            It.Is<SmtpSettings>(s => s.Password == "new-secret"), "admin"), Times.Once);
    }

    [Fact]
    public async Task Upsert_InvalidSettings_ReturnsBadRequestAndDoesNotSave()
    {
        var request = ValidRequest();
        request.Host = "";

        var result = await CreateService().UpsertAsync(TenantId, request, "admin");

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _repository.Verify(r => r.UpsertAsync(It.IsAny<string>(), It.IsAny<SmtpSettings>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Get_SavedConfig_ReportsPasswordWithoutReturningIt()
    {
        SetupSavedConfig();

        var result = await CreateService().GetForTenantAsync(TenantId);

        Assert.True(result.IsSuccess);
        Assert.Equal(EmailSource.Tenant, result.Data!.Source);
        Assert.True(result.Data.HasPassword);
        Assert.DoesNotContain(typeof(TenantSmtpConfigResponse).GetProperties(), p => p.Name == "Password");
    }

    [Fact]
    public async Task Get_NoTenantConfig_ReportsServerDefault()
    {
        _emailService.Setup(e => e.GetSourceAsync(null)).ReturnsAsync(EmailSource.Server);

        var result = await CreateService().GetForTenantAsync(TenantId);

        Assert.Equal(EmailSource.Server, result.Data!.Source);
        Assert.Null(result.Data.Host);
    }

    [Fact]
    public async Task Delete_NoTenantConfig_ReturnsNotFound()
    {
        _repository.Setup(r => r.DeleteAsync(TenantId)).ReturnsAsync(false);

        var result = await CreateService().DeleteAsync(TenantId, "admin");

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task SendTestEmail_InvalidRecipient_ReturnsBadRequestWithoutSending()
    {
        var request = ValidTestRequest();
        request.To = "not-an-email";

        var result = await CreateService().SendTestEmailAsync(TenantId, request);

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _smtpSender.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SendTestEmail_BlankPassword_UsesSavedPassword()
    {
        SetupSavedConfig();

        var result = await CreateService().SendTestEmailAsync(TenantId, ValidTestRequest(password: null));

        Assert.True(result.IsSuccess);
        _smtpSender.Verify(s => s.SendAsync(
            It.Is<SmtpSettings>(x => x.Password == SavedPassword),
            It.Is<IEnumerable<string>>(to => to.Single() == "admin@example.com"),
            It.IsAny<string>(), It.IsAny<string>(), false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendTestEmail_SendFails_DoesNotLeakExceptionMessage()
    {
        _smtpSender.Setup(s => s.SendAsync(It.IsAny<SmtpSettings>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Connection refused by 10.0.0.5:25"));

        var result = await CreateService().SendTestEmailAsync(TenantId, ValidTestRequest());

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.DoesNotContain("10.0.0.5", result.ErrorMessage);
    }
}
