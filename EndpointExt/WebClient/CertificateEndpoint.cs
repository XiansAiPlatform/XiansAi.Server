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
        _logger.LogInformation($"GetFlowServerSettings for Tenant:{_tenantContext.TenantId} FlowServerUrl:{_tenantContext.GetTemporalConfig().FlowServerUrl} FlowServerNamespace:{_tenantContext.GetTemporalConfig().FlowServerNamespace}");
        return Results.Ok(new {
            _tenantContext.GetTemporalConfig().FlowServerUrl,
            _tenantContext.GetTemporalConfig().FlowServerNamespace
        });
    }

    public async Task<IResult> GetFlowServerCertFile(string fileName) {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        
        if (!string.IsNullOrEmpty(temporalConfig.CertificateBase64))
        {
            // Use base64 config
            var certBytes = Convert.FromBase64String(temporalConfig.CertificateBase64);
            return Results.File(certBytes, "application/x-pkcs12", fileName);
        } else {
            return Results.NotFound($"Temporal certificate configuration not found for setting Tenants:{_tenantContext.TenantId}");
        }
    }

    public async Task<string> GetFlowServerCertBase64() {
        var temporalConfig = _tenantContext.GetTemporalConfig();

        if (!string.IsNullOrEmpty(temporalConfig.CertificateBase64))
        {
            // Use base64 config directly
            return temporalConfig.CertificateBase64;
        } else {
            throw new Exception($"Temporal certificate configuration not found for setting Tenants:{_tenantContext.TenantId}");
        }
    }

    public async Task<string> GetFlowServerPrivateKeyBase64() {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        
        if (!string.IsNullOrEmpty(temporalConfig.PrivateKeyBase64))
        {
            // Use base64 config directly
            return temporalConfig.PrivateKeyBase64;
        }
        else {
            throw new Exception($"Temporal private key configuration not found for setting Tenants:{_tenantContext.TenantId}");
        }
    }
    public async Task<IResult> GetFlowServerPrivateKeyFile(string fileName) {
        var temporalConfig = _tenantContext.GetTemporalConfig();
        
        if (!string.IsNullOrEmpty(temporalConfig.PrivateKeyBase64))
        {
            // Use base64 config
            var keyBytes = Convert.FromBase64String(temporalConfig.PrivateKeyBase64);
            return Results.File(keyBytes, "application/x-pkcs12", fileName);
        }
        else {
            return Results.NotFound($"Temporal private key configuration not found for setting Tenants:{_tenantContext.TenantId}");
        }
    }

    public async Task<IResult> GenerateClientCertificate(CertRequest request)
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            _logger.LogDebug("Generating client certificate for {requestName}, {tenantId}", request.Name, _tenantContext.TenantId);
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";  
            _logger.LogDebug("User Name: {userName}", userId);
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
            return Results.Problem($"Failed to generate certificate. {ex.Message}", statusCode: 500);
        }
    }

    public async Task<IResult> GenerateClientCertificateBase64(CertRequest request)
    {
        try
        {
            var user = _httpContextAccessor.HttpContext?.User;
            _logger.LogDebug("Generating client certificate (PEM) for {requestName}, {tenantId}", request.Name, _tenantContext.TenantId);
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";  
            _logger.LogDebug("User Name: {userName}", userId);
            
            var cert = await _certificateGenerator.GenerateClientCertificate(request.Name, _tenantContext.TenantId, userId);
            
            // Export as PEM format
            var pemBytes = cert.Export(X509ContentType.Cert);
            var base64String = Convert.ToBase64String(pemBytes);
            
            return Results.Ok(new { 
                certificate = base64String
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            _logger.LogError(ex, "Failed to generate PEM certificate");
            return Results.Problem($"Failed to generate PEM certificate. {ex.Message}", statusCode: 500);
        }
    }
}

public class CertRequest
{
    public string Name { get; set; } = "DefaultCertificate";
    public string? Password { get; set; }
}