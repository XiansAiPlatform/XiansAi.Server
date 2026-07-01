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
    public required string ApiKey { get; set; }
    public required string? ProviderName { get; set; }
    public required string ModelName { get; set; }
    public Dictionary<string, string>? AdditionalConfig { get; set; }
    public required string? BaseUrl { get; set; }
}

public class CertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly CertificateGenerator _certificateGenerator;
    private readonly ICertificateRepository _certificateRepository;
    private readonly ILlmService _llmService;
    private readonly IWebhookEventPublisher _webhookEventPublisher;

    public CertificateService(
        ILogger<CertificateService> logger,
        ITenantContext tenantContext,
        CertificateGenerator certificateGenerator,
        ICertificateRepository certificateRepository,
        ILlmService llmService,
        IWebhookEventPublisher webhookEventPublisher)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _certificateGenerator = certificateGenerator;
        _certificateRepository = certificateRepository;
        _llmService = llmService;
        _webhookEventPublisher = webhookEventPublisher;
    }

    public FlowServerSettings GetFlowServerSettings()
    {
        _logger.LogInformation($"GetFlowServerSettings for Tenant:{_tenantContext.TenantId} FlowServerUrl:{_tenantContext.GetTemporalConfig().FlowServerUrl} FlowServerNamespace:{_tenantContext.GetTemporalConfig().FlowServerNamespace}");
        return new FlowServerSettings
        {
            FlowServerUrl = _tenantContext.GetTemporalConfig().FlowServerUrlExternal ?? _tenantContext.GetTemporalConfig().FlowServerUrl ?? throw new Exception($"FlowServerUrl not found for Tenant:{_tenantContext.TenantId}"),
            FlowServerNamespace = _tenantContext.GetTemporalConfig().FlowServerNamespace ?? throw new Exception($"FlowServerNamespace not found for Tenant:{_tenantContext.TenantId}"),
            FlowServerCertBase64 = GetFlowServerCertBase64(),
            FlowServerPrivateKeyBase64 = GetFlowServerPrivateKeyBase64(),
            ApiKey = _llmService.GetApiKey(),
            ProviderName = _llmService.GetLlmProvider(),
            ModelName = _llmService.GetModel(),
            AdditionalConfig = _llmService.GetAdditionalConfig(),
            BaseUrl = _llmService.GetBaseUrl()
        };
    }


    public string? GetFlowServerCertBase64()
    {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        if (temporalConfig.CertificateBase64 == null)
        {
            return null;
        }
        return temporalConfig.CertificateBase64;
    }

    public string? GetFlowServerPrivateKeyBase64()
    {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        if (temporalConfig.PrivateKeyBase64 == null)
        {
            return null;
        }
        return temporalConfig.PrivateKeyBase64;
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