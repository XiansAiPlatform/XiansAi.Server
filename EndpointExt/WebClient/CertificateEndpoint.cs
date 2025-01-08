using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;
using XiansAi.Server.Auth;

namespace XiansAi.Server.EndpointExt.WebClient;

public class CertificateEndpoint
{
    private readonly ILogger<CertificateEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly CertificateGenerator _certificateGenerator;
    private readonly IConfiguration _configuration;
    private readonly IKeyVaultService _keyVaultService;
    public CertificateEndpoint(
        ILogger<CertificateEndpoint> logger,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        CertificateGenerator certificateGenerator,
        IConfiguration configuration,
        IKeyVaultService keyVaultService)
    {
        _configuration = configuration;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _certificateGenerator = certificateGenerator;
        _keyVaultService = keyVaultService;
    }

    public IResult GetFlowServerSettings() {
        return Results.Ok(new {
            _tenantContext.GetTemporalConfig().FlowServerUrl,
            _tenantContext.GetTemporalConfig().FlowServerNamespace
        });
    }

    public async Task<IResult> GetFlowServerCertFile(string fileName) {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        if (temporalConfig.CertificateFilePath != null) {
            // local file
            var certBytes = await File.ReadAllBytesAsync(temporalConfig.CertificateFilePath);
            return Results.File(certBytes, "application/x-pkcs12", fileName);
        } else if (temporalConfig.Certificate != null) {
            // key vault
            var cert = temporalConfig.Certificate;
            var certBytes = Convert.FromBase64String(cert);
            return  Results.File(certBytes, "application/x-pkcs12", fileName);
        } else {
            return Results.NotFound($"Temporal certificate configuration not found for setting Tenants:{_tenantContext.TenantId}");
        }
    }

    public async Task<IResult> GetFlowServerPrivateKeyFile(string fileName) {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        if (temporalConfig.PrivateKeyFilePath != null) {
            // local file
            var certBytes = await File.ReadAllBytesAsync(temporalConfig.PrivateKeyFilePath);
            return Results.File(certBytes, "application/x-pkcs12", fileName);
        } else if (temporalConfig.PrivateKey != null) {
            // key vault
            var cert = temporalConfig.PrivateKey;
            var certBytes = Convert.FromBase64String(cert);
            return Results.File(certBytes, "application/x-pkcs12", fileName);
        } else {
            return Results.NotFound($"Temporal private key configuration not found for setting Tenants:{_tenantContext.TenantId}");
        }
    }

    public async Task<IResult> GenerateClientCertificate(CertRequest request)
    {
        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";            
            var cert = await _certificateGenerator.GenerateClientCertificate(request.Name, _tenantContext.TenantId, userId);
            var certBytes = cert.Export(X509ContentType.Pfx, request.Password);
            
            return await Task.FromResult(Results.File(
                certBytes,
                "application/x-pkcs12",
                $"{request.Name}-{_tenantContext.TenantId}.pfx"
            ));
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            _logger.LogError(ex, "Failed to generate certificate");
            return Results.Problem("Failed to generate certificate", statusCode: 500);
        }
    }
}

public class CertRequest
{
    public string Name { get; set; } = "DefaultCertificate";
    public string? Password { get; set; }
}