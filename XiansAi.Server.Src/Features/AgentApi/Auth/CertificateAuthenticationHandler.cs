// Features/AgentApi/Auth/CertificateAuthenticationHandler.cs
using System.Collections.Generic;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Linq;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Features.AgentApi.Repositories;
using System.Security.Cryptography;
using Shared.Auth;
using Shared.Utils;
using Shared.Repositories;

namespace Features.AgentApi.Auth;

public class CertificateAuthenticationHandler : AuthenticationHandler<CertificateAuthenticationOptions>
{
    private readonly ILogger<CertificateAuthenticationHandler> _logger;
    private readonly CertificateGenerator _certificateGenerator;
    private readonly ICertificateRepository _certificateRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ICertificateValidationCache _certValidationCache;
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    public CertificateAuthenticationHandler(
        IOptionsMonitor<CertificateAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        CertificateGenerator certificateGenerator,
        ICertificateRepository certificateRepository,
        ITenantContext tenantContext,
        ICertificateValidationCache certValidationCache,
        IUserRepository userRepository,
        ITenantRepository tenantRepository) 
        : base(options, logger, encoder)
    {
        _logger = logger.CreateLogger<CertificateAuthenticationHandler>();
        _certificateGenerator = certificateGenerator;
        _certificateRepository = certificateRepository;
        _tenantContext = tenantContext;
        _certValidationCache = certValidationCache;
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        try
        {
            // Only handle authentication for AgentApi endpoints
            var path = Request.Path.Value?.ToLowerInvariant() ?? "";
            if (!path.StartsWith("/api/agent/"))
            {
                _logger.LogDebug("Skipping certificate authentication for non-AgentApi path: {Path}", Request.Path);
                return AuthenticateResult.NoResult(); // Let other handlers process this request
            }

            _logger.LogDebug("Handling certificate authentication for {Path}", Request.Path);

            var certHeader = await ExtractCertificateFromHeader();
            if (certHeader == null)
            {
                return AuthenticateResult.Fail("No valid certificate found in request");
            }

            var certBytes = Convert.FromBase64String(certHeader);
            using var cert = X509CertificateLoader.LoadCertificate(certBytes);

            // Check validation cache (only successful validations are cached)
            var (found, validation) = _certValidationCache.GetValidation(cert.Thumbprint);
            if (found && validation?.IsValid == true)
            {
                return await CreateAuthenticationTicket(cert, validation);
            }

            // Cache miss or invalid - perform full validation
            var validationResult = await ValidateCertificateAsync(cert);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning("Certificate validation failed for tenant ID {TenantId}", cert.Subject);
                // Note: Invalid results are not cached to prevent cache pollution
                return AuthenticateResult.Fail(string.Join(", ", validationResult.Errors));
            }

            // Build cached validation payload (includes user + roles) to avoid DB on future hits
            var cacheBuildResult = await BuildCachedValidationAsync(cert);
            if (!cacheBuildResult.success)
            {
                return AuthenticateResult.Fail(cacheBuildResult.error ?? "Certificate validation cache build failed");
            }

            _certValidationCache.CacheValidation(cert.Thumbprint, cacheBuildResult.validation);
            return await CreateAuthenticationTicket(cert, cacheBuildResult.validation);
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
                _logger.LogWarning("Certificate has been revoked for tenant ID {TenantId}", cert.Subject);
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
                _logger.LogWarning("Certificate validation chain failed for tenant ID {TenantId}", cert.Subject);
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
                _logger.LogWarning("Certificate is not signed by the expected root CA for tenant ID {TenantId}", cert.Subject);
                result.AddError("Certificate is not signed by the expected root CA");
                return result;
            }

            // Validate certificate purpose
            var enhancedKeyUsage = cert.Extensions
                .OfType<X509EnhancedKeyUsageExtension>()
                .FirstOrDefault();
                
            if (enhancedKeyUsage == null || !HasClientAuthenticationPurpose(enhancedKeyUsage))
            {
                _logger.LogWarning("Certificate does not have client authentication purpose for tenant ID {TenantId}", cert.Subject);
                result.AddError("Certificate does not have client authentication purpose");
                return result;
            }

            // Validate tenant
            var tenantId = GetSubjectValue(cert.Subject, "O");
            if (string.IsNullOrEmpty(tenantId))
            {
                _logger.LogWarning("Invalid tenant: No tenant ID found in certificate for tenant ID {TenantId}", cert.Subject);
                result.AddError("Invalid tenant: No tenant ID found in certificate");
                return result;
            }
            _logger.LogInformation("Getting tenant by tenant ID {TenantId}", tenantId);
            var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            //`var tenantResult = await _tenantService.GetTenantByTenantId(tenantId);
            _logger.LogInformation("Tenant result: {TenantResult}", tenant);
            if (tenant == null)
            {
                _logger.LogWarning("Invalid tenant: {TenantId} not found", tenantId);
                result.AddError($"Invalid tenant: {tenantId}.");
                return result;
            }

            result.IsValid = true;
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validatingcertificate");
            result.Errors.Add($"Certificate validation error: {ex.Message}");
            return result;
        }
    }

    private bool HasClientAuthenticationPurpose(X509EnhancedKeyUsageExtension extension)
    {
        return extension.EnhancedKeyUsages.Cast<Oid>()
            .Any(oid => oid.Value == "1.3.6.1.5.5.7.3.2"); // Client Authentication OID
    }

    private async Task<(bool success, string? error, CachedCertificateValidation validation)> BuildCachedValidationAsync(X509Certificate2 cert)
    {
        var tenantId = GetSubjectValue(cert.Subject, "O");
        var userId = GetSubjectValue(cert.Subject, "OU");

        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId))
        {
            return (false, "Invalid certificate subject format", new CachedCertificateValidation { IsValid = false });
        }

        var user = await _userRepository.GetByUserIdAsync(userId);
        if (user == null)
        {
            return (false, "Invalid user ID", new CachedCertificateValidation { IsValid = false });
        }

        var roles = user.TenantRoles
            .FirstOrDefault(tr => tr.Tenant == tenantId)?.Roles?.ToList() ?? new List<string>();

        if (user.IsSysAdmin && !roles.Contains(SystemRoles.SysAdmin))
        {
            roles.Add(SystemRoles.SysAdmin);
        }

        var validation = new CachedCertificateValidation
        {
            IsValid = true,
            TenantId = tenantId,
            UserId = userId,
            Roles = roles.ToArray(), // Use array for immutability (matches default Array.Empty<string>())
            IsSysAdmin = user.IsSysAdmin
        };

        return (true, null, validation);
    }

    private Task<AuthenticateResult> CreateAuthenticationTicket(X509Certificate2 cert, CachedCertificateValidation validation)
    {
        var tenantId = validation.TenantId;
        var userId = validation.UserId;
        var roles = validation.Roles ?? Array.Empty<string>();

        // Note: SysAdmin role is already included in cached validation.Roles if user.IsSysAdmin

        // Handle X-Tenant-Id header
        if (Request.Headers.TryGetValue("X-Tenant-Id", out var requestedTenantId) && 
            !string.IsNullOrWhiteSpace(requestedTenantId))
        {
            var requestedTenantIdStr = requestedTenantId.ToString();
            
            if (validation.IsSysAdmin)
            {
                // Sys admin can impersonate any tenant
                _logger.LogInformation("Sys admin {UserId} impersonating tenant {ImpersonatedTenantId} (original tenant: {OriginalTenantId})", 
                    userId, requestedTenantIdStr, tenantId);
                tenantId = requestedTenantIdStr;
            }
            else
            {
                // Non-admin users must match their certificate's tenant ID
                if (!tenantId.Equals(requestedTenantIdStr, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Non admin User `{UserId}` attempted to access tenant `{RequestedTenantId}` but certificate is for tenant `{CertTenantId}`", 
                        userId, requestedTenantIdStr, tenantId);
                    return Task.FromResult(AuthenticateResult.Fail("X-Tenant-Id header does not match certificate tenant ID"));
                }
                _logger.LogDebug("User {UserId} X-Tenant-Id header matches certificate tenant {TenantId}", userId, tenantId);
            }
        }

        // Set up TenantContext (roles is already an array from cached validation)
        _tenantContext.TenantId = tenantId;
        _tenantContext.UserType = UserType.AgentApiKey;
        _tenantContext.LoggedInUser = userId;
        _tenantContext.UserRoles = roles is string[] rolesArray ? rolesArray : roles.ToArray();
        _tenantContext.AuthorizedTenantIds = new[] { tenantId }; // Agents can only access their own tenant

        // Build claims list with all user roles
        var claimsList = new List<Claim>
        {
            new Claim(ClaimTypes.Name, userId),
            new Claim("Tenant", tenantId)
        };

        // Add all roles as separate role claims for authorization to work
        foreach (var role in roles)
        {
            claimsList.Add(new Claim(ClaimTypes.Role, role));
        }

        var identity = new ClaimsIdentity(claimsList, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}