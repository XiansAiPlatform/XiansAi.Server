using Shared.Data.Models;
using Shared.Providers;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// A tenant's SMTP settings as sent by the admin UI/API. A blank password keeps the saved one.
/// </summary>
public class TenantSmtpConfigRequest
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;
    public SmtpSecurity Security { get; set; } = SmtpSecurity.StartTls;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
}

/// <summary>
/// Sends a test email with the given (unsaved) settings to <see cref="To"/>.
/// </summary>
public class SendTestEmailRequest : TenantSmtpConfigRequest
{
    public string? To { get; set; }
}

/// <summary>
/// The tenant's email state. <see cref="Source"/> is <c>None</c> when email is off. The password
/// is never returned; <see cref="HasPassword"/> says whether one is saved. Connection fields are
/// only filled when the tenant has its own config.
/// </summary>
public class TenantSmtpConfigResponse
{
    public EmailSource Source { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public SmtpSecurity? Security { get; set; }
    public string? Username { get; set; }
    public bool HasPassword { get; set; }
    public string? SenderEmail { get; set; }
    public string? SenderName { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public interface ITenantSmtpConfigService
{
    Task<ServiceResult<TenantSmtpConfigResponse>> GetForTenantAsync(string tenantId);
    Task<ServiceResult<TenantSmtpConfigResponse>> UpsertAsync(string tenantId, TenantSmtpConfigRequest request, string actor);
    Task<ServiceResult<TenantSmtpConfigResponse>> DeleteAsync(string tenantId, string actor);
    Task<ServiceResult<bool>> SendTestEmailAsync(string tenantId, SendTestEmailRequest request);
}

/// <summary>
/// Manages per-tenant SMTP overrides. Normal sending goes through <see cref="IEmailService"/>;
/// only the test email is sent here, with settings that are not saved yet.
/// </summary>
public class TenantSmtpConfigService : ITenantSmtpConfigService
{
    private const string TestEmailSubject = "Xians test email";
    private const string TestEmailBody = "This is a test email from Xians. Your email server settings work.";

    private readonly ITenantSmtpConfigRepository _repository;
    private readonly IEmailService _emailService;
    private readonly ISmtpEmailSender _smtpSender;
    private readonly ILogger<TenantSmtpConfigService> _logger;
    private readonly IWebhookEventPublisher _webhookEventPublisher;
    private readonly IAuditLogService _auditLogService;

    public TenantSmtpConfigService(
        ITenantSmtpConfigRepository repository,
        IEmailService emailService,
        ISmtpEmailSender smtpSender,
        ILogger<TenantSmtpConfigService> logger,
        IWebhookEventPublisher webhookEventPublisher,
        IAuditLogService auditLogService)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
        _smtpSender = smtpSender ?? throw new ArgumentNullException(nameof(smtpSender));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _webhookEventPublisher = webhookEventPublisher ?? throw new ArgumentNullException(nameof(webhookEventPublisher));
        _auditLogService = auditLogService ?? throw new ArgumentNullException(nameof(auditLogService));
    }

    public async Task<ServiceResult<TenantSmtpConfigResponse>> GetForTenantAsync(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<TenantSmtpConfigResponse>.BadRequest("tenantId is required");

        try
        {
            return ServiceResult<TenantSmtpConfigResponse>.Success(await BuildResponseAsync(tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SMTP config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantSmtpConfigResponse>.InternalServerError("Failed to load email configuration");
        }
    }

    public async Task<ServiceResult<TenantSmtpConfigResponse>> UpsertAsync(string tenantId, TenantSmtpConfigRequest request, string actor)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<TenantSmtpConfigResponse>.BadRequest("tenantId is required");
        if (request == null)
            return ServiceResult<TenantSmtpConfigResponse>.BadRequest("request body is required");

        try
        {
            var settings = ToSettings(request, await _repository.GetAsync(tenantId));
            var error = settings.Validate();
            if (error != null)
                return ServiceResult<TenantSmtpConfigResponse>.BadRequest(error);

            await _repository.UpsertAsync(tenantId, settings, actor);

            DomainEventEmitter.Emit(
                _webhookEventPublisher,
                _auditLogService,
                DomainEventTypes.TenantSmtpUpdated,
                new { tenantId, actor, settings.Host, settings.Port, Security = settings.Security.ToString() },
                tenantId,
                description: $"Email (SMTP) configuration for tenant '{tenantId}' was updated by '{actor}' (server '{settings.Host}:{settings.Port}').");

            return ServiceResult<TenantSmtpConfigResponse>.Success(await BuildResponseAsync(tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving SMTP config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantSmtpConfigResponse>.InternalServerError("Failed to save email configuration");
        }
    }

    public async Task<ServiceResult<TenantSmtpConfigResponse>> DeleteAsync(string tenantId, string actor)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<TenantSmtpConfigResponse>.BadRequest("tenantId is required");

        try
        {
            var deleted = await _repository.DeleteAsync(tenantId);
            if (!deleted)
                return ServiceResult<TenantSmtpConfigResponse>.NotFound("No email configuration found for the tenant");

            DomainEventEmitter.Emit(
                _webhookEventPublisher,
                _auditLogService,
                DomainEventTypes.TenantSmtpDeleted,
                new { tenantId, actor },
                tenantId,
                description: $"Email (SMTP) configuration for tenant '{tenantId}' was removed by '{actor}'; the server default applies.");

            return ServiceResult<TenantSmtpConfigResponse>.Success(await BuildResponseAsync(tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting SMTP config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantSmtpConfigResponse>.InternalServerError("Failed to delete email configuration");
        }
    }

    public async Task<ServiceResult<bool>> SendTestEmailAsync(string tenantId, SendTestEmailRequest request)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<bool>.BadRequest("tenantId is required");
        if (request == null)
            return ServiceResult<bool>.BadRequest("request body is required");
        if (!SmtpSettings.IsValidEmail(request.To))
            return ServiceResult<bool>.BadRequest("to must be a valid email address");

        SmtpSettings settings;
        try
        {
            settings = ToSettings(request, await _repository.GetAsync(tenantId));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading SMTP config for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("Failed to load email configuration");
        }

        var error = settings.Validate();
        if (error != null)
            return ServiceResult<bool>.BadRequest(error);

        try
        {
            await _smtpSender.SendAsync(settings, [request.To.Trim()], TestEmailSubject, TestEmailBody, isHtml: false);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Test email failed for tenant {TenantId} via {Host}:{Port}",
                LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(settings.Host), settings.Port);
            return ServiceResult<bool>.BadRequest("Could not send test email. Check the SMTP settings and try again.");
        }
    }

    /// <summary>
    /// Maps a request to settings. A blank password falls back to the saved one.
    /// </summary>
    private static SmtpSettings ToSettings(TenantSmtpConfigRequest request, TenantSmtpConfig? existing) => new()
    {
        Host = request.Host?.Trim() ?? string.Empty,
        Port = request.Port,
        Security = request.Security,
        Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
        Password = string.IsNullOrEmpty(request.Password) ? existing?.Password : request.Password,
        SenderEmail = request.SenderEmail?.Trim() ?? string.Empty,
        SenderName = string.IsNullOrWhiteSpace(request.SenderName) ? null : request.SenderName.Trim()
    };

    private async Task<TenantSmtpConfigResponse> BuildResponseAsync(string tenantId)
    {
        var doc = await _repository.GetAsync(tenantId);
        if (doc == null)
        {
            // No tenant override: report the server default.
            return new TenantSmtpConfigResponse { Source = await _emailService.GetSourceAsync(tenantId: null) };
        }

        return new TenantSmtpConfigResponse
        {
            Source = EmailSource.Tenant,
            Host = doc.Host,
            Port = doc.Port,
            Security = doc.Security,
            Username = doc.Username,
            HasPassword = !string.IsNullOrEmpty(doc.Password),
            SenderEmail = doc.SenderEmail,
            SenderName = doc.SenderName,
            UpdatedAt = doc.UpdatedAt,
            UpdatedBy = doc.UpdatedBy
        };
    }
}
