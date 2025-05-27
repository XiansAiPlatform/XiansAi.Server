// Features/AgentApi/Auth/CertificateAuthenticationHandler.cs
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Features.AgentApi.Repositories;
using System.Security.Cryptography;
using Shared.Auth;
using Shared.Utils;

namespace Features.AgentApi.Auth;

public class CertificateAuthenticationHandler : AuthenticationHandler<CertificateAuthenticationOptions>
{
    private readonly ILogger<CertificateAuthenticationHandler> _logger;
    private readonly IConfiguration _configuration;
    private readonly CertificateGenerator _certificateGenerator;
    private readonly ICertificateRepository _certificateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICertificateValidationCache _certValidationCache;

    public CertificateAuthenticationHandler(
        IOptionsMonitor<CertificateAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CertificateGenerator certificateGenerator,
        ICertificateRepository certificateRepository,
        IConfiguration configuration,
        ITenantContext tenantContext,
        ICertificateValidationCache certValidationCache) 
        : base(options, logger, encoder)
    {
        _logger = logger.CreateLogger<CertificateAuthenticationHandler>();
        _configuration = configuration;
        _certificateGenerator = certificateGenerator;
        _certificateRepository = certificateRepository;
        _tenantContext = tenantContext;
        _certValidationCache = certValidationCache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            _logger.LogDebug("Handling certificate authentication for {Path}", Request.Path);

            var certHeader = await ExtractCertificateFromHeader();
            if (certHeader == null)
            {
                return AuthenticateResult.Fail("No valid certificate found in request");
            }

            var certBytes = Convert.FromBase64String(certHeader);
            using var cert = X509CertificateLoader.LoadCertificate(certBytes);

            // Check validation cache
            var (found, isValid) = _certValidationCache.GetValidation(cert.Thumbprint);
            if (found)
            {
                if (!isValid)
                {
                    return AuthenticateResult.Fail("Certificate validation failed (cached result)");
                }
                return await CreateAuthenticationTicket(cert);
            }

            var validationResult = await ValidateCertificateAsync(cert);
            if (!validationResult.IsValid)
            {
                _certValidationCache.CacheValidation(cert.Thumbprint, false);
                return AuthenticateResult.Fail(string.Join(", ", validationResult.Errors));
            }

            _certValidationCache.CacheValidation(cert.Thumbprint, true);
            return await CreateAuthenticationTicket(cert);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Certificate authentication failed");
            return AuthenticateResult.Fail("Certificate authentication failed");
        }
    }

    private Task<string?> ExtractCertificateFromHeader()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) || 
            string.IsNullOrEmpty(authHeader))
        {
            _logger.LogDebug("No authorization header found");
            return Task.FromResult<string?>(null);
        }

        var authHeaderValue = authHeader.ToString();
        if (!authHeaderValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Authorization header is not in Bearer format");
            return Task.FromResult<string?>(null);
        }

        return Task.FromResult<string?>(authHeaderValue["Bearer ".Length..].Trim());
    }

    private string? GetSubjectValue(string subject, string key)
    {
        return subject.Split(',')
            .FirstOrDefault(x => x.Trim().StartsWith($"{key}="))
            ?.Split('=')[1];
    }

    private async Task<CertificateValidationResult> ValidateCertificateAsync(X509Certificate2 cert)
    {
        var result = new CertificateValidationResult();

        try
        {
            // Check if certificate is revoked
            if (await _certificateRepository.IsRevokedAsync(cert.Thumbprint))
            {
                result.AddError("Certificate has been revoked");
                return result;
            }

            // Get root certificate
            var rootCert = _certificateGenerator.GetRootCertificate();
            
            // Create chain
            using var chain = new X509Chain();
            chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck; // Disable CRL checking for now
            chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
            chain.ChainPolicy.ExtraStore.Add(rootCert);
            
            var isValid = chain.Build(cert);
            
            if (!isValid)
            {
                var errors = chain.ChainStatus
                    .Select(s => $"Chain validation error: {s.StatusInformation}")
                    .ToList();
                result.Errors.AddRange(errors);
                return result;
            }

            // Verify the chain terminates with our root certificate
            var chainRoot = chain.ChainElements[chain.ChainElements.Count - 1].Certificate;
            if (!chainRoot.Thumbprint.Equals(rootCert.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                result.AddError("Certificate is not signed by the expected root CA");
                return result;
            }

            // Validate certificate purpose
            var enhancedKeyUsage = cert.Extensions
                .OfType<X509EnhancedKeyUsageExtension>()
                .FirstOrDefault();
                
            if (enhancedKeyUsage == null || !HasClientAuthenticationPurpose(enhancedKeyUsage))
            {
                result.AddError("Certificate does not have client authentication purpose");
                return result;
            }

            // Validate tenant
            var tenantId = GetSubjectValue(cert.Subject, "O");

            if (string.IsNullOrEmpty(tenantId) || !_configuration.GetSection($"Tenants:{tenantId}").Exists())
            {
                result.AddError($"Invalid tenant: {tenantId}");
                return result;
            }

            result.IsValid = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating certificate");
            result.Errors.Add($"Certificate validation error: {ex.Message}");
            return result;
        }
    }

    private bool HasClientAuthenticationPurpose(X509EnhancedKeyUsageExtension extension)
    {
        return extension.EnhancedKeyUsages.Cast<Oid>()
            .Any(oid => oid.Value == "1.3.6.1.5.5.7.3.2"); // Client Authentication OID
    }

    private Task<AuthenticateResult> CreateAuthenticationTicket(X509Certificate2 cert)
    {
        var tenantId = GetSubjectValue(cert.Subject, "O");
        var userId = GetSubjectValue(cert.Subject, "OU");

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid certificate subject format"));
        }

        // Set up TenantContext
        _tenantContext.TenantId = tenantId;
        _tenantContext.LoggedInUser = userId;
        _tenantContext.UserRoles = new[] { "Agent" }; // Agents get the "Agent" role
        _tenantContext.AuthorizedTenantIds = new[] { tenantId }; // Agents can only access their own tenant

        var claims = new[]
        {
            new Claim(ClaimTypes.Name, userId),
            new Claim(ClaimTypes.Role, "Agent"),
            new Claim("Tenant", tenantId)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}