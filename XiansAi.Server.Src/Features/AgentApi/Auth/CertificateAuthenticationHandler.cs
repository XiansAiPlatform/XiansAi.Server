using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Encodings.Web;
using Features.AgentApi.Repositories;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using Shared.Auth;
using Shared.Repositories;
using Shared.Utils;

namespace Features.AgentApi.Auth;

public class CertificateAuthenticationHandler : AuthenticationHandler<CertificateAuthenticationOptions>
{
    private const string AgentApiPathPrefix = "/api/agent/";
    private const string BearerPrefix = "Bearer ";
    private const string TenantIdHeader = "X-Tenant-Id";
    private const string ClientAuthenticationOid = "1.3.6.1.5.5.7.3.2";

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
            if (!IsAgentApiRequest())
            {
                _logger.LogDebug("Skipping certificate authentication for non-AgentApi path: {Path}", LogSanitizer.Sanitize(Request.Path));
                return AuthenticateResult.NoResult();
            }

            _logger.LogDebug("Handling certificate authentication for {Path}", LogSanitizer.Sanitize(Request.Path));

            var certHeader = ExtractCertificateFromHeader();
            if (certHeader == null)
            {
                return AuthenticateResult.Fail("No valid certificate found in request");
            }

            var certBytes = Convert.FromBase64String(certHeader);
            using var cert = X509CertificateLoader.LoadCertificate(certBytes);

            var (found, validation) = _certValidationCache.GetValidation(cert.Thumbprint);
            if (found && validation?.IsValid == true)
            {
                return CreateAuthenticationTicket(validation);
            }

            var validationResult = await ValidateCertificateAsync(cert);
            if (!validationResult.IsValid)
            {
                _logger.LogWarning(
                    "Certificate validation failed for subject {Subject}",
                    LogSanitizer.Sanitize(cert.Subject));
                return AuthenticateResult.Fail(string.Join(", ", validationResult.Errors));
            }

            var cacheBuildResult = await BuildCachedValidationAsync(cert);
            if (!cacheBuildResult.Success)
            {
                return AuthenticateResult.Fail(cacheBuildResult.Error ?? "Certificate validation cache build failed");
            }

            _certValidationCache.CacheValidation(cert.Thumbprint, cacheBuildResult.Validation);
            return CreateAuthenticationTicket(cacheBuildResult.Validation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Certificate authentication failed");
            return AuthenticateResult.Fail("Certificate authentication failed");
        }
    }

    private bool IsAgentApiRequest()
    {
        var path = Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
        return path.StartsWith(AgentApiPathPrefix, StringComparison.Ordinal);
    }

    private string? ExtractCertificateFromHeader()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader) ||
            string.IsNullOrEmpty(authHeader))
        {
            _logger.LogDebug("No authorization header found");
            return null;
        }

        var authHeaderValue = authHeader.ToString();
        if (!authHeaderValue.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("Authorization header is not in Bearer format");
            return null;
        }

        return authHeaderValue[BearerPrefix.Length..].Trim();
    }

    private async Task<CertificateValidationResult> ValidateCertificateAsync(X509Certificate2 cert)
    {
        var result = new CertificateValidationResult();

        try
        {
            if (await _certificateRepository.IsRevokedAsync(cert.Thumbprint))
            {
                _logger.LogWarning(
                    "Certificate has been revoked for subject {Subject}",
                    LogSanitizer.Sanitize(cert.Subject));
                result.AddError("Certificate has been revoked");
                return result;
            }

            var chainResult = ValidateCertificateChain(cert);
            if (!chainResult.IsValid)
            {
                return chainResult;
            }

            if (!HasClientAuthenticationPurpose(cert))
            {
                _logger.LogWarning(
                    "Certificate does not have client authentication purpose for subject {Subject}",
                    LogSanitizer.Sanitize(cert.Subject));
                result.AddError("Certificate does not have client authentication purpose");
                return result;
            }

            var subject = CertificateSubject.Parse(cert.Subject);
            if (string.IsNullOrEmpty(subject.TenantId))
            {
                _logger.LogWarning(
                    "No tenant ID found in certificate subject {Subject}",
                    LogSanitizer.Sanitize(cert.Subject));
                result.AddError("Invalid tenant: No tenant ID found in certificate");
                return result;
            }

            return await ValidateTenantExistsAsync(subject.TenantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating certificate");
            result.Errors.Add($"Certificate validation error: {ex.Message}");
            return result;
        }
    }

    private CertificateValidationResult ValidateCertificateChain(X509Certificate2 cert)
    {
        var result = new CertificateValidationResult();
        var rootCert = _certificateGenerator.GetRootCertificate();

        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
        chain.ChainPolicy.ExtraStore.Add(rootCert);

        if (!chain.Build(cert))
        {
            _logger.LogWarning(
                "Certificate validation chain failed for subject {Subject}",
                LogSanitizer.Sanitize(cert.Subject));
            result.Errors.AddRange(
                chain.ChainStatus.Select(s => $"Chain validation error: {s.StatusInformation}"));
            return result;
        }

        var chainRoot = chain.ChainElements[^1].Certificate;
        if (!chainRoot.Thumbprint.Equals(rootCert.Thumbprint, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Certificate is not signed by the expected root CA for subject {Subject}",
                LogSanitizer.Sanitize(cert.Subject));
            result.AddError("Certificate is not signed by the expected root CA");
            return result;
        }

        result.IsValid = true;
        return result;
    }

    private static bool HasClientAuthenticationPurpose(X509Certificate2 cert)
    {
        var enhancedKeyUsage = cert.Extensions
            .OfType<X509EnhancedKeyUsageExtension>()
            .FirstOrDefault();

        return enhancedKeyUsage != null &&
               enhancedKeyUsage.EnhancedKeyUsages.Cast<Oid>()
                   .Any(oid => oid.Value == ClientAuthenticationOid);
    }

    private async Task<CertificateValidationResult> ValidateTenantExistsAsync(string tenantId)
    {
        var result = new CertificateValidationResult();

        _logger.LogDebug("Validating tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
        var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
        if (tenant == null)
        {
            _logger.LogWarning("Invalid tenant: {TenantId} not found", LogSanitizer.Sanitize(tenantId));
            result.AddError($"Invalid tenant: {tenantId}.");
            return result;
        }

        result.IsValid = true;
        return result;
    }

    private async Task<CachedValidationBuildResult> BuildCachedValidationAsync(X509Certificate2 cert)
    {
        var subject = CertificateSubject.Parse(cert.Subject);
        if (!subject.IsComplete)
        {
            return CachedValidationBuildResult.Failed("Invalid certificate subject format");
        }

        // The certificate's OU may carry either the canonical user id or the user's email,
        // depending on which UI issued the certificate. Resolve by user id first, then
        // fall back to email so certificates issued by either path authenticate correctly.
        var user = await _userRepository.GetByUserIdAsync(subject.UserIdentifier!)
            ?? await _userRepository.GetByUserEmailAsync(subject.UserIdentifier!);
        if (user == null)
        {
            return CachedValidationBuildResult.Failed("Invalid user ID");
        }

        // Always use the canonical user id for the authenticated context, regardless of
        // whether the certificate identified the user by id or by email.
        var userId = string.IsNullOrEmpty(user.UserId) ? subject.UserIdentifier! : user.UserId;

        var roles = user.TenantRoles
            .FirstOrDefault(tr => tr.Tenant == subject.TenantId)?.Roles?.ToList() ?? [];

        if (user.IsSysAdmin && !roles.Contains(SystemRoles.SysAdmin))
        {
            roles.Add(SystemRoles.SysAdmin);
        }

        return CachedValidationBuildResult.Succeeded(new CachedCertificateValidation
        {
            IsValid = true,
            TenantId = subject.TenantId!,
            UserId = userId,
            Roles = roles.ToArray(),
            IsSysAdmin = user.IsSysAdmin
        });
    }

    private AuthenticateResult CreateAuthenticationTicket(CachedCertificateValidation validation)
    {
        var tenantId = validation.TenantId;
        var userId = validation.UserId;
        var roles = validation.Roles?.ToArray() ?? Array.Empty<string>();

        if (Request.Headers.TryGetValue(TenantIdHeader, out var requestedTenantId) &&
            !string.IsNullOrWhiteSpace(requestedTenantId))
        {
            var requestedTenantIdStr = requestedTenantId.ToString();

            if (validation.IsSysAdmin)
            {
                _logger.LogInformation(
                    "Sys admin {UserId} impersonating tenant {ImpersonatedTenantId} (original tenant: {OriginalTenantId})",
                    LogSanitizer.Sanitize(userId),
                    LogSanitizer.Sanitize(requestedTenantIdStr),
                    LogSanitizer.Sanitize(tenantId));
                tenantId = requestedTenantIdStr;
            }
            else if (!tenantId.Equals(requestedTenantIdStr, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Non admin user {UserId} attempted to access tenant {RequestedTenantId} but certificate is for tenant {CertTenantId}",
                    LogSanitizer.Sanitize(userId),
                    LogSanitizer.Sanitize(requestedTenantIdStr),
                    LogSanitizer.Sanitize(tenantId));
                return AuthenticateResult.Fail("X-Tenant-Id header does not match certificate tenant ID");
            }
            else
            {
                _logger.LogDebug(
                    "User {UserId} X-Tenant-Id header matches certificate tenant {TenantId}",
                    LogSanitizer.Sanitize(userId),
                    LogSanitizer.Sanitize(tenantId));
            }
        }

        _tenantContext.TenantId = tenantId;
        _tenantContext.UserType = UserType.AgentApiKey;
        _tenantContext.LoggedInUser = userId;
        _tenantContext.UserRoles = roles;
        _tenantContext.AuthorizedTenantIds = [tenantId];

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, userId),
            new("Tenant", tenantId)
        };
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return AuthenticateResult.Success(ticket);
    }

    private readonly record struct CertificateSubject(string? TenantId, string? UserIdentifier)
    {
        public bool IsComplete =>
            !string.IsNullOrEmpty(TenantId) && !string.IsNullOrEmpty(UserIdentifier);

        public static CertificateSubject Parse(string subject) =>
            new(GetSubjectValue(subject, "O"), GetSubjectValue(subject, "OU"));

        private static string? GetSubjectValue(string subject, string key)
        {
            foreach (var part in subject.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.StartsWith($"{key}=", StringComparison.Ordinal))
                {
                    return trimmed[(key.Length + 1)..];
                }
            }

            return null;
        }
    }

    private readonly record struct CachedValidationBuildResult(
        bool Success,
        string? Error,
        CachedCertificateValidation Validation)
    {
        public static CachedValidationBuildResult Failed(string error) =>
            new(false, error, new CachedCertificateValidation { IsValid = false });

        public static CachedValidationBuildResult Succeeded(CachedCertificateValidation validation) =>
            new(true, null, validation);
    }
}
