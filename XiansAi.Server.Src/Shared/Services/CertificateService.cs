using System.Security.Cryptography.X509Certificates;
using System.ComponentModel.DataAnnotations;
using Shared.Auth;
using Shared.Data.Models;
using Features.AgentApi.Repositories;
using Features.AgentApi.Models;
using Shared.Utils;

namespace Shared.Services;

public class FlowServerSettings
{
    public required string FlowServerUrl { get; set; }
    public required string FlowServerNamespace { get; set; }
    public string? FlowServerCertBase64 { get; set; }
    public string? FlowServerPrivateKeyBase64 { get; set; }
}

public class CertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly CertificateGenerator _certificateGenerator;
    private readonly ICertificateRepository _certificateRepository;
    private readonly IWebhookEventPublisher _webhookEventPublisher;

    public CertificateService(
        ILogger<CertificateService> logger,
        ITenantContext tenantContext,
        CertificateGenerator certificateGenerator,
        ICertificateRepository certificateRepository,
        IWebhookEventPublisher webhookEventPublisher)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _certificateGenerator = certificateGenerator;
        _certificateRepository = certificateRepository;
        _webhookEventPublisher = webhookEventPublisher;
    }

    public async Task<FlowServerSettings> GetFlowServerSettingsAsync()
    {
        var temporalConfig = await _tenantContext.GetTemporalConfigAsync();
        _logger.LogInformation($"GetFlowServerSettings for Tenant:{_tenantContext.TenantId} FlowServerUrl:{temporalConfig.FlowServerUrl} FlowServerNamespace:{temporalConfig.FlowServerNamespace}");
        return new FlowServerSettings
        {
            FlowServerUrl = temporalConfig.FlowServerUrlExternal ?? temporalConfig.FlowServerUrl ?? throw new Exception($"FlowServerUrl not found for Tenant:{_tenantContext.TenantId}"),
            FlowServerNamespace = temporalConfig.FlowServerNamespace ?? throw new Exception($"FlowServerNamespace not found for Tenant:{_tenantContext.TenantId}"),
            FlowServerCertBase64 = temporalConfig.CertificateBase64,
            FlowServerPrivateKeyBase64 = temporalConfig.PrivateKeyBase64
        };
    }

    private async Task<X509Certificate2> GenerateAndStoreCertificate(string name, string userId, bool revokePrevious, string? friendlyName = null)
    {
        // Revoke previous certificates for this user
        if (revokePrevious)
        {
            var previousCerts = await _certificateRepository.GetByUserAsync(_tenantContext.TenantId, userId);
            foreach (var prevCert in previousCerts)
            {
                if (!prevCert.IsRevoked)
                {
                    var deleted = await _certificateRepository.DeleteByThumbprintAsync(prevCert.Thumbprint);
                    if (deleted)
                        _logger.LogInformation(
                            "Deleted previous certificate. Thumbprint: {Thumbprint}, User: {UserId}",
                            LogSanitizer.Sanitize(prevCert.Thumbprint), LogSanitizer.Sanitize(userId));
                    else
                        _logger.LogWarning(
                            "Failed to delete previous certificate. Thumbprint: {Thumbprint}, User: {UserId}",
                            LogSanitizer.Sanitize(prevCert.Thumbprint), LogSanitizer.Sanitize(userId));
                }
            }
        }
        // Validate PFX certificates if this is the first call (helpful for debugging)
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _certificateGenerator.ValidatePfxCertificates();
        }

        // Generate new certificate
        var cert = _certificateGenerator.GenerateClientCertificate(
            name,
            _tenantContext.TenantId,
            userId);

        // Store certificate metadata
        try
        {
            var newCertificate = new Certificate
            {
                Thumbprint = cert.Thumbprint,
                SubjectName = cert.Subject,
                FriendlyName = string.IsNullOrWhiteSpace(friendlyName) ? null : friendlyName,
                TenantId = _tenantContext.TenantId,
                IssuedTo = userId,
                IssuedAt = DateTime.UtcNow,
                // Explicitly specify UTC DateTimeKind to avoid timezone issues
                ExpiresAt = DateTime.SpecifyKind(cert.NotAfter.ToUniversalTime(), DateTimeKind.Utc),
                IsRevoked = false
            };

            var validatedNewCert = newCertificate.SanitizeAndValidate();
            await _certificateRepository.CreateAsync(validatedNewCert);
        }
        catch (ValidationException ex)
        {
            _logger.LogError(ex, "Failed to generate and store certificate");
            throw new Exception("Failed to generate and store certificate");
        }
        _logger.LogInformation(
            "Generated new certificate. Name: {Name}, Thumbprint: {Thumbprint}, User: {UserId}", 
            LogSanitizer.Sanitize(name), 
            cert.Thumbprint,
            LogSanitizer.Sanitize(userId));

        return cert;
    }

    /// <summary>
    /// Returns all certificates issued to <paramref name="targetUserId"/> within their tenant,
    /// ordered newest first.
    /// </summary>
    public async Task<IEnumerable<Certificate>> ListCertificatesForUserAsync(string targetUserId)
    {
        var tenantId = _tenantContext.TenantId
            ?? throw new InvalidOperationException("Tenant context is required");
        return await _certificateRepository.GetByUserAsync(tenantId, targetUserId);
    }

    /// <summary>
    /// Permanently deletes a certificate, but only if it was issued to <paramref name="targetUserId"/>
    /// within their tenant. Returns false when not found or ownership does not match.
    /// The validation cache is invalidated before deletion so in-flight auth attempts fail immediately.
    /// </summary>
    public async Task<bool> RevokeCertificateAsync(string thumbprint, string reason, string targetUserId)
    {
        var cert = await _certificateRepository.GetByThumbprintAsync(thumbprint);
        if (cert == null
            || !string.Equals(cert.TenantId, _tenantContext.TenantId, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(cert.IssuedTo, targetUserId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var revoked = await _certificateRepository.DeleteByThumbprintAsync(thumbprint);

        if (revoked)
        {
            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.CertificateRevoked,
                new { tenantId = cert.TenantId, thumbprint, issuedTo = cert.IssuedTo, reason },
                cert.TenantId);
        }

        return revoked;
    }

    /// <summary>
    /// Generates a certificate for the calling user (reads <c>LoggedInUser</c> from tenant context).
    /// Used by the WebAPI; admin callers should use the overload that accepts an explicit userId.
    /// </summary>
    public async Task<IResult> GenerateClientCertificateBase64(bool revokePrevious = false)
    {
        var userId = _tenantContext.LoggedInUser
            ?? throw new UnauthorizedAccessException("User not authenticated");
        return await GenerateClientCertificateBase64ForUser(userId, revokePrevious);
    }

    /// <summary>
    /// Generates a certificate issued to <paramref name="targetUserId"/>.
    /// <paramref name="friendlyName"/> is an optional display label stored alongside the certificate
    /// for identification in UIs; it does not affect the X.509 subject.
    /// </summary>
    public async Task<IResult> GenerateClientCertificateBase64ForUser(
        string targetUserId, bool revokePrevious = false, string? friendlyName = null)
    {
        try
        {
            var certName = "XiansAi-Client-Certificate";

            _logger.LogInformation(
                "Generating base64 client certificate for {Name}, Tenant: {TenantId}, User: {UserId}",
                certName,
                LogSanitizer.Sanitize(_tenantContext.TenantId),
                LogSanitizer.Sanitize(targetUserId));

            var cert = await GenerateAndStoreCertificate(certName, targetUserId, revokePrevious, friendlyName);
            var certBytes = cert.Export(X509ContentType.Cert);
            var base64String = Convert.ToBase64String(certBytes);

            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.CertificateCreated,
                new { tenantId = _tenantContext.TenantId, thumbprint = cert.Thumbprint, issuedTo = targetUserId, friendlyName },
                _tenantContext.TenantId);

            return Results.Ok(new { certificate = base64String });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized certificate generation attempt");
            return Results.Unauthorized();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate base64 certificate");
            return Results.Problem(
                "An error occurred while generating the certificate",
                statusCode: StatusCodes.Status500InternalServerError);
        }
    }
}

public class CertRequest
{
    public string Name { get; set; } = "DefaultCertificate";
}