using System.Security.Cryptography.X509Certificates;
using System.Security.Claims;

public class CertificateEndpoint
{
    private readonly ILogger<CertificateEndpoint> _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly CertificateGenerator _certificateGenerator;

    public CertificateEndpoint(
        ILogger<CertificateEndpoint> logger,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        CertificateGenerator certificateGenerator)
    {
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _certificateGenerator = certificateGenerator;
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
                $"{request.Name.Trim('-')}-{_tenantContext.TenantId.Trim('-')}.pfx"
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