using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Shared.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using XiansAi.Server.Auth;

namespace Features.AgentApi.Auth;

public class CertificateAuthenticationOptions : AuthenticationSchemeOptions
{
    public const string DefaultScheme = "Certificate";
    public string AuthenticationType => DefaultScheme;
}

public class CertificateAuthenticationHandler : AuthenticationHandler<CertificateAuthenticationOptions>
{
    private readonly ILogger<CertificateAuthenticationHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly CertificateGenerator _certificateGenerator;

    public CertificateAuthenticationHandler(
        IOptionsMonitor<CertificateAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CertificateGenerator certificateGenerator,
        IConfiguration configuration) : base(options, logger, encoder)
    {
        _logger = logger.CreateLogger<CertificateAuthenticationHandler>();
        _configuration = configuration;
        _certificateGenerator = certificateGenerator;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        _logger.LogInformation("Handling certificate authentication for {Path}", Request.Path);

        // Get the client certificate from the Authorization header
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) || string.IsNullOrEmpty(authHeader))
        {
            _logger.LogInformation("No authorization header found");
            return Task.FromResult(AuthenticateResult.Fail("No authorization header found"));
        }

        var authHeaderValue = authHeader.ToString();
        if (!authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Authorization header is not in Bearer format");
            return Task.FromResult(AuthenticateResult.Fail("Authorization header is not in Bearer format"));
        }

        var certHeader = authHeaderValue.Substring("Bearer ".Length).Trim();
        if (string.IsNullOrEmpty(certHeader))
        {
            _logger.LogInformation("No client certificate found in Bearer token");
            return Task.FromResult(AuthenticateResult.Fail("No client certificate found in Bearer token"));
        }

        try
        {
            var rootCertificate = _certificateGenerator.GetRootCertificate();

            var certBytes = Convert.FromBase64String(certHeader);
#pragma warning disable SYSLIB0057 // Type or member is obsolete
            var cert = new X509Certificate2(certBytes);
#pragma warning restore SYSLIB0057 // Type or member is obsolete

            // Validate certificate chain
            var chain = new X509Chain();
            chain.ChainPolicy.ExtraStore.Add(rootCertificate);
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;

            if (!chain.Build(cert))
            {
                _logger.LogInformation("Certificate chain validation failed");
                return Task.FromResult(AuthenticateResult.Fail("Certificate chain validation failed"));
            }

            // Verify that the certificate was issued by our root certificate
            bool validRoot = false;
            for (int i = 0; i < chain.ChainElements.Count; i++)
            {
                if (chain.ChainElements[i].Certificate.Thumbprint == rootCertificate.Thumbprint)
                {
                    validRoot = true;
                    break;
                }
            }

            if (!validRoot)
            {
                _logger.LogInformation("Certificate was not issued by the expected root certificate");
                return Task.FromResult(AuthenticateResult.Fail("Certificate was not issued by the expected root certificate"));
            }

            // Validate tenant name from certificate subject
            var certSubject = cert.Subject;
            var distinguishedName = new X500DistinguishedName(certSubject);
            var tenantNameFromCert = distinguishedName.Format(multiLine: false)
                .Split(',')
                .FirstOrDefault(x => x.Trim().StartsWith("O="))
                ?.Split('=')[1];

            var userNameFromCert = distinguishedName.Format(multiLine: false)
                .Split(',')
                .FirstOrDefault(x => x.Trim().StartsWith("OU="))
                ?.Split('=')[1];
            
            if (string.IsNullOrEmpty(tenantNameFromCert))
            {
                _logger.LogInformation("Certificate does not contain Tenant name");
                return Task.FromResult(AuthenticateResult.Fail("Certificate does not contain Tenant name"));
            }

            if (string.IsNullOrEmpty(userNameFromCert))
            {
                _logger.LogInformation("Certificate does not contain User name");
                return Task.FromResult(AuthenticateResult.Fail("Certificate does not contain User name"));
            }

            if (!_configuration.GetSection($"Tenants:{tenantNameFromCert}").Exists())
            {
                _logger.LogInformation($"Tenant name from certificate is not a valid tenant - Tenant:{tenantNameFromCert}");
                return Task.FromResult(AuthenticateResult.Fail($"Tenant name from certificate is not a valid tenant - Tenant:{tenantNameFromCert}"));
            }

            // Set up tenant context - this will be handled via DI and request scoping
            var httpContext = Context;
            var scope = httpContext.RequestServices;
            var tenantContext = scope.GetRequiredService<ITenantContext>();
            tenantContext.TenantId = tenantNameFromCert;
            tenantContext.LoggedInUser = userNameFromCert;

            // Create the claims for authentication
            var claims = new[]
            {
                new Claim(ClaimTypes.Name, userNameFromCert),
                new Claim("Tenant", tenantNameFromCert)
            };

            var identity = new ClaimsIdentity(claims, Scheme.Name);
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, Scheme.Name);

            _logger.LogInformation("Certificate validation successful for tenant {Tenant} and user {User}", tenantNameFromCert, userNameFromCert);
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
        catch (Exception ex)
        {
            _logger.LogError("Certificate validation failed due to exception {Exception}", ex);
            return Task.FromResult(AuthenticateResult.Fail($"Certificate validation failed: {ex.Message}"));
        }
    }
} 