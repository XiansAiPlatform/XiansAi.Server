using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using Shared.Auth;
using Features.AgentApi.Repositories;
using Features.AgentApi.Models;
using Shared.Utils.GenAi;
using Shared.Utils;

namespace Shared.Services;

public class FlowServerSettings
{
    public required string FlowServerUrl { get; set; }
    public required string FlowServerNamespace { get; set; }
    public required string FlowServerCertBase64 { get; set; }
    public required string FlowServerPrivateKeyBase64 { get; set; }
    public required string OpenAIApiKey { get; set; }
}

public class CertificateService
{
    private readonly ILogger<CertificateService> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly CertificateGenerator _certificateGenerator;
    private readonly ICertificateRepository _certificateRepository;
    private readonly ILlmService _llmService;
    
    public CertificateService(
        ILogger<CertificateService> logger,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        CertificateGenerator certificateGenerator,
        ICertificateRepository certificateRepository,
        ILlmService llmService)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _certificateGenerator = certificateGenerator;
        _certificateRepository = certificateRepository;
        _llmService = llmService;
    }

    public FlowServerSettings GetFlowServerSettings() {
        _logger.LogInformation($"GetFlowServerSettings for Tenant:{_tenantContext.TenantId} FlowServerUrl:{_tenantContext.GetTemporalConfig().FlowServerUrl} FlowServerNamespace:{_tenantContext.GetTemporalConfig().FlowServerNamespace}");
        return new FlowServerSettings {
            FlowServerUrl = _tenantContext.GetTemporalConfig().FlowServerUrl ?? throw new Exception($"FlowServerUrl not found for Tenant:{_tenantContext.TenantId}"),
            FlowServerNamespace = _tenantContext.GetTemporalConfig().FlowServerNamespace ?? throw new Exception($"FlowServerNamespace not found for Tenant:{_tenantContext.TenantId}"),
            FlowServerCertBase64 = GetFlowServerCertBase64(),
            FlowServerPrivateKeyBase64 = GetFlowServerPrivateKeyBase64(),
            OpenAIApiKey = _llmService.GetApiKey()
        };
    }


    public string GetFlowServerCertBase64() {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        return temporalConfig.CertificateBase64 ?? throw new Exception($"Temporal certificate configuration not found for setting Tenants:{_tenantContext.TenantId}");
    }

    public string GetFlowServerPrivateKeyBase64() {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        return temporalConfig.PrivateKeyBase64 ?? throw new Exception($"Temporal private key configuration not found for setting Tenants:{_tenantContext.TenantId}");
    }

    private async Task<X509Certificate2> GenerateAndStoreCertificate(string name, string userId)
    {
        // Generate new certificate
        var cert = _certificateGenerator.GenerateClientCertificate(
            name, 
            _tenantContext.TenantId, 
            userId);

        var previousCerts = await _certificateRepository.GetByUserAsync(_tenantContext.TenantId, userId);
        // Store certificate metadata
        await _certificateRepository.CreateAsync(new Certificate
        {
            Thumbprint = cert.Thumbprint,
            SubjectName = cert.Subject,
            TenantId = _tenantContext.TenantId,
            IssuedTo = userId,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = cert.NotAfter.ToUniversalTime(),
            IsRevoked = false
        });
        // Revoke previous certificates for this user
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
        _logger.LogInformation(
            "Generated new certificate. Name: {Name}, Thumbprint: {Thumbprint}, User: {UserId}", 
            name, 
            cert.Thumbprint,
            userId);

        return cert;
    }

    public async Task<IResult> GenerateClientCertificateBase64()
    {
        try
        {
            var certName = "XiansAi-Client-Certificate";
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value 
                ?? throw new UnauthorizedAccessException("User not authenticated");

            _logger.LogInformation(
                "Generating base64 client certificate for {Name}, Tenant: {TenantId}, User: {UserId}", 
                certName, 
                _tenantContext.TenantId, 
                userId);

            var cert = await GenerateAndStoreCertificate(certName, userId);
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