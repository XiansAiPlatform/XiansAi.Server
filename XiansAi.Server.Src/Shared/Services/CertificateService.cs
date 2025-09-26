using System.Security.Cryptography.X509Certificates;
using System.ComponentModel.DataAnnotations;
using Shared.Auth;
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

    public CertificateService(
        ILogger<CertificateService> logger,
        ITenantContext tenantContext,
        CertificateGenerator certificateGenerator,
        ICertificateRepository certificateRepository,
        ILlmService llmService)
    {
        _logger = logger;
        _tenantContext = tenantContext;
        _certificateGenerator = certificateGenerator;
        _certificateRepository = certificateRepository;
        _llmService = llmService;
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

    private async Task<X509Certificate2> GenerateAndStoreCertificate(string name, string userId, bool revokePrevious)
    {
        // Revoke previous certificates for this user
        if (revokePrevious)
        {
            var previousCerts = await _certificateRepository.GetByUserAsync(_tenantContext.TenantId, userId);
            foreach (var prevCert in previousCerts)
            {
                if (!prevCert.IsRevoked)
                {
                    prevCert.IsRevoked = true;
                    prevCert.RevokedAt = DateTime.UtcNow;
                    await _certificateRepository.UpdateAsync(prevCert);
                    _logger.LogInformation(
                        "Revoked previous certificate. Thumbprint: {Thumbprint}, User: {UserId}", 
                        prevCert.Thumbprint, 
                        userId);
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
                TenantId = _tenantContext.TenantId,
                IssuedTo = userId,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = cert.NotAfter.ToUniversalTime(),
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
            name, 
            cert.Thumbprint,
            userId);

        return cert;
    }

    public async Task<IResult> GenerateClientCertificateBase64(bool revokePrevious = false)
    {
        try
        {
            var certName = "XiansAi-Client-Certificate";
            var userId = _tenantContext.LoggedInUser
                ?? throw new UnauthorizedAccessException("User not authenticated");

            _logger.LogInformation(
                "Generating base64 client certificate for {Name}, Tenant: {TenantId}, User: {UserId}", 
                certName, 
                _tenantContext.TenantId, 
                userId);

            var cert = await GenerateAndStoreCertificate(certName, userId, revokePrevious);
            var certBytes = cert.Export(X509ContentType.Cert);
            var base64String = Convert.ToBase64String(certBytes);

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