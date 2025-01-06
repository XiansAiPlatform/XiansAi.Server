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

    public CertificateEndpoint(
        ILogger<CertificateEndpoint> logger,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        CertificateGenerator certificateGenerator,
        IConfiguration configuration)
    {
        _configuration = configuration;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _certificateGenerator = certificateGenerator;
    }

    public IResult GetFlowServerSettings() {
        var url = _configuration.GetSection("Tenants").GetSection(_tenantContext.TenantId).GetSection("Temporal").GetValue<string>("FlowServerUrl");
        var ns = _configuration.GetSection("Tenants").GetSection(_tenantContext.TenantId).GetSection("Temporal").GetValue<string>("FlowServerNamespace");

        return Results.Ok(new {
            url,
            ns
        });
    }

    public async Task<IResult> GetFlowServerCertFile(string fileName) {
        return await GetCertificateFile("Temporal", "FlowServerCertPath", fileName);
    }

    public async Task<IResult> GetFlowServerPrivateKeyFile(string fileName) {
        return await GetCertificateFile("Temporal", "FlowServerPrivateKeyPath", fileName);
    }

    private async Task<IResult> GetCertificateFile(string group, string key, string fileName)
    {
        var filename = _configuration.GetSection("Tenants").GetSection(_tenantContext.TenantId).GetSection(group).GetValue<string>(key);

        if (string.IsNullOrEmpty(filename))
        {
            return Results.NotFound($"Certificate configuration not found for setting Tenants:{_tenantContext.TenantId}:{group}:{key}");
        }

        if (!File.Exists(filename))
        {
            return Results.NotFound($"Certificate file not found");
        }

        var extension = Path.GetExtension(filename);

        var certBytes = File.ReadAllBytes(filename);
        return await Task.FromResult(Results.File(
            certBytes,
            $"application/{extension.ToLower()}",
            $"{fileName}-{_tenantContext.TenantId}.{extension}"
        ));
    }

    public async Task<IResult> GenerateClientCertificate(CertRequest request)
    {
        try
        {
            var userId = _httpContextAccessor.HttpContext?.User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "unknown";            
            var cert = _certificateGenerator.GenerateClientCertificate(request.Name, _tenantContext.TenantId, userId);
            var certBytes = cert.Export(X509ContentType.Pfx, request.Password);
            
            return await Task.FromResult(Results.File(
                certBytes,
                "application/x-pkcs12",
                $"{request.Name}-{_tenantContext.TenantId}.pfx"
            ));
        }
        catch (Exception ex)
        {
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