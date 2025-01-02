using System.Security.Cryptography.X509Certificates;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

public class CertificateValidationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly X509Certificate2 _rootCertificate;
    private readonly ILogger<CertificateValidationMiddleware> _logger;
    private readonly IConfiguration _configuration;

    public CertificateValidationMiddleware(RequestDelegate next, 
        CertificateGenerator generator, 
        ILogger<CertificateValidationMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _rootCertificate = generator.CreateOrLoadRootCertificate();
        _logger = logger;
        _configuration = configuration;
    }

    public async Task InvokeAsync(HttpContext context)
    {


        // Only apply to /api/server paths
        if (!context.Request.Path.StartsWithSegments("/api/server"))
        {
            await _next(context);
            return;
        }

        // Get the client certificate from the request headers
        var certHeader = context.Request.Headers["X-Client-Cert"].ToString();
        if (string.IsNullOrEmpty(certHeader))
        {
            _logger.LogInformation("No client certificate found");
            context.Response.StatusCode = 401;
            return;
        }

        try
        {
            var certBytes = Convert.FromBase64String(certHeader);
#pragma warning disable SYSLIB0057 // Type or member is obsolete
            var cert = new X509Certificate2(certBytes);
#pragma warning restore SYSLIB0057 // Type or member is obsolete

            // Validate certificate chain
            var chain = new X509Chain();
            chain.ChainPolicy.ExtraStore.Add(_rootCertificate);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            if (!chain.Build(cert))
            {
                _logger.LogInformation("Certificate chain validation failed");
                context.Response.StatusCode = 401;
                return;
            }

            // Verify that the certificate was issued by our root certificate
            bool validRoot = false;
            for (int i = 0; i < chain.ChainElements.Count; i++)
            {
                if (chain.ChainElements[i].Certificate.Thumbprint == _rootCertificate.Thumbprint)
                {
                    validRoot = true;
                    break;
                }
            }

            if (!validRoot)
            {
                _logger.LogInformation("Certificate was not issued by the expected root certificate");
                context.Response.StatusCode = 401;
                return;
            }


            // Validate tenant name from certificate subject
            var certSubject = cert.Subject;
            var certOrganization = cert.Issuer;
            var distinguishedName = new X500DistinguishedName(certSubject);
            var tenantNameFromCert = distinguishedName.Format(multiLine: false)
                .Split(',')
                .FirstOrDefault(x => x.Trim().StartsWith("O="))
                ?.Split('=')[1];

            
            if (string.IsNullOrEmpty(tenantNameFromCert))
            {
                _logger.LogInformation("Certificate does not contain Organization name");
                context.Response.StatusCode = 401;
                return;
            }

            if (!_configuration.GetSection($"Tenants:{tenantNameFromCert}").Exists())
            {
                _logger.LogInformation($"Organization name from certificate does not exist in configuration - Organization:{tenantNameFromCert}");
                context.Response.StatusCode = 401;
                return;
            }

            _logger.LogInformation("Authorization succeeded. Setting tenant ID: {currentTenantId}", tenantNameFromCert);


            // Get the request services scope
            var scope = context.RequestServices;
            // Register services into the scope
            var tenantContext = scope.GetRequiredService<ITenantContext>();
            tenantContext.TenantId = tenantNameFromCert;

            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogInformation("Certificate validation failed due to exception {Exception}", ex);
            context.Response.StatusCode = 401;
        }
    }
}