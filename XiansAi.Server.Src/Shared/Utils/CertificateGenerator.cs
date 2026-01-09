using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Shared.Utils;

public class CertificateGenerator
{
    private readonly ILogger<CertificateGenerator> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly CertificatesConfig _certificatesConfig;

    public CertificateGenerator(IConfiguration configuration, ILogger<CertificateGenerator> logger, IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
        _certificatesConfig = _configuration.GetSection("Certificates").Get<CertificatesConfig>() ?? 
            throw new InvalidOperationException("Certificates configuration not found");
    }

    public X509Certificate2 GetRootCertificate()
    {
        string pfxBase64 = _certificatesConfig.AppServerPfxBase64 ?? 
            throw new InvalidOperationException($"PFX data not found in configuration key CertificatesConfig.AppServerPfxBase64.");
        
        string? password = _certificatesConfig.AppServerCertPassword ?? 
            throw new InvalidOperationException($"Certificate password not found in configuration key CertificatesConfig.AppServerCertPassword.");

        try 
        {
            // Convert base64 string to byte array
            byte[] pfxBytes = Convert.FromBase64String(pfxBase64);
            
            // Import the entire certificate collection from PFX using the new API
            var collection = X509CertificateLoader.LoadPkcs12Collection(pfxBytes, password, X509KeyStorageFlags.Exportable);
            
            _logger.LogDebug("Loaded {Count} certificates from PFX", collection.Count);
            
            // Find the CA certificate (the one with BasicConstraints CA=true)
            X509Certificate2? candidateCert = null;
            X509Certificate2? selfSignedWithKey = null;
            
            foreach (X509Certificate2 cert in collection)
            {
                _logger.LogDebug("Examining certificate: Subject={Subject}, Issuer={Issuer}, HasPrivateKey={HasPrivateKey}", 
                    cert.Subject, cert.Issuer, cert.HasPrivateKey);
                
                // Check if this is a self-signed certificate (Subject == Issuer)
                bool isSelfSigned = cert.Subject.Equals(cert.Issuer, StringComparison.OrdinalIgnoreCase);
                if (isSelfSigned && cert.HasPrivateKey)
                {
                    selfSignedWithKey = cert;
                    _logger.LogDebug("Found self-signed certificate with private key: {Subject}", cert.Subject);
                }
                
                var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                if (basicConstraints != null)
                {
                    _logger.LogDebug("Basic Constraints - CA: {IsCA}, Critical: {IsCritical}, PathLength: {PathLength}", 
                        basicConstraints.CertificateAuthority, basicConstraints.Critical, basicConstraints.PathLengthConstraint);
                    
                    if (basicConstraints.CertificateAuthority)
                    {
                        _logger.LogDebug("Found CA certificate: {Subject}", cert.Subject);
                        
                        // Verify it has a private key for signing
                        if (cert.HasPrivateKey)
                        {
                            _logger.LogDebug("CA certificate has private key - ready for signing");
                            return cert;
                        }
                        else
                        {
                            candidateCert = cert;
                            _logger.LogWarning("CA certificate found but has no private key, keeping as candidate");
                        }
                    }
                }
                else
                {
                    _logger.LogDebug("Certificate has no Basic Constraints extension");
                }
            }
            
            // Fallback strategy: if we have a self-signed certificate with private key but no proper CA,
            // use it as a CA (this handles cases where the certificate was generated without proper CA extensions)
            if (candidateCert == null && selfSignedWithKey != null)
            {
                _logger.LogWarning("No proper CA certificate found, but found self-signed certificate with private key. Using as fallback CA: {Subject}", 
                    selfSignedWithKey.Subject);
                return selfSignedWithKey;
            }
            
            // If we get here, no CA certificate was found
            _logger.LogError("No CA certificate found in PFX file. Available certificates:");
            foreach (X509Certificate2 cert in collection)
            {
                var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                var isCA = basicConstraints?.CertificateAuthority ?? false;
                var isSelfSigned = cert.Subject.Equals(cert.Issuer, StringComparison.OrdinalIgnoreCase);
                
                _logger.LogError("  - Subject: {Subject}, HasPrivateKey: {HasPrivateKey}, IsCA: {IsCA}, IsSelfSigned: {IsSelfSigned}", 
                    cert.Subject, cert.HasPrivateKey, isCA, isSelfSigned);
                    
                if (basicConstraints != null)
                {
                    _logger.LogError("    BasicConstraints: CA={CA}, PathLength={PathLength}, Critical={Critical}",
                        basicConstraints.CertificateAuthority, basicConstraints.PathLengthConstraint, basicConstraints.Critical);
                }
                else
                {
                    _logger.LogError("    No BasicConstraints extension found");
                }
            }
            
            throw new InvalidOperationException("No valid CA certificate found in PFX file");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load root certificate from PFX data");
            throw;
        }
    }

    /// <summary>
    /// Validates and provides diagnostic information about certificates in the PFX file
    /// </summary>
    public void ValidatePfxCertificates()
    {
        try
        {
            string pfxBase64 = _certificatesConfig.AppServerPfxBase64 ?? 
                throw new InvalidOperationException($"PFX data not found in configuration key CertificatesConfig.AppServerPfxBase64.");
            
            string? password = _certificatesConfig.AppServerCertPassword ?? 
                throw new InvalidOperationException($"Certificate password not found in configuration key CertificatesConfig.AppServerCertPassword.");

            byte[] pfxBytes = Convert.FromBase64String(pfxBase64);
            var collection = X509CertificateLoader.LoadPkcs12Collection(pfxBytes, password, X509KeyStorageFlags.Exportable);
            
            _logger.LogInformation("=== PFX Certificate Validation Report ===");
            _logger.LogInformation("Total certificates in PFX: {Count}", collection.Count);
            
            foreach (X509Certificate2 cert in collection)
            {
                _logger.LogInformation("Certificate: {Subject}", cert.Subject);
                _logger.LogInformation("  Issuer: {Issuer}", cert.Issuer);
                _logger.LogInformation("  Serial: {Serial}", cert.SerialNumber);
                _logger.LogInformation("  Valid From: {NotBefore} UTC", cert.NotBefore.ToUniversalTime());
                _logger.LogInformation("  Valid To: {NotAfter} UTC", cert.NotAfter.ToUniversalTime());
                _logger.LogInformation("  Has Private Key: {HasPrivateKey}", cert.HasPrivateKey);
                _logger.LogInformation("  Self-Signed: {IsSelfSigned}", cert.Subject.Equals(cert.Issuer, StringComparison.OrdinalIgnoreCase));
                
                var basicConstraints = cert.Extensions.OfType<X509BasicConstraintsExtension>().FirstOrDefault();
                if (basicConstraints != null)
                {
                    _logger.LogInformation("  BasicConstraints: CA={CA}, PathLength={PathLength}, Critical={Critical}",
                        basicConstraints.CertificateAuthority, basicConstraints.PathLengthConstraint, basicConstraints.Critical);
                }
                else
                {
                    _logger.LogInformation("  BasicConstraints: Not present");
                }
                
                var keyUsage = cert.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
                if (keyUsage != null)
                {
                    _logger.LogInformation("  Key Usage: {KeyUsage}", keyUsage.KeyUsages);
                }
                
                _logger.LogInformation("  ---");
            }
            _logger.LogInformation("=== End of Report ===");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate PFX certificates");
        }
    }

    public X509Certificate2 GenerateClientCertificate(string certName, string tenantName, string userName)
    {
        _logger.LogDebug("Generating client certificate for {certName}, {tenantName}, {userName}", certName, tenantName, userName);

        var subject = $"CN=XiansAi, OU={userName}, O={tenantName}";
        _logger.LogDebug("Subject: {subject}", subject);

        using var rsa = RSA.Create(2048);
        var distinguishedName = new X500DistinguishedName(subject);
        var req = new CertificateRequest(
            distinguishedName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        // Add enhanced key usage extension for client authentication
        req.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.2") }, // Client Authentication
                false));

        // Add basic constraints (not a CA)
        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        // Add key usage extension
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));

        var rootCertificate = GetRootCertificate();
        
        _logger.LogDebug("Using root certificate: Subject={Subject}, Issuer={Issuer}", 
            rootCertificate.Subject, rootCertificate.Issuer);

        // Set validity period starting from now
        var now = DateTimeOffset.UtcNow;
        var proposedNotBefore = now.AddMinutes(-10); // Buffer for clock skew
        
        // Ensure client certificate's notBefore is not earlier than issuer certificate's NotBefore
        // Explicitly specify UTC DateTimeKind to avoid timezone conversion issues
        var issuerNotBeforeUtc = DateTime.SpecifyKind(rootCertificate.NotBefore.ToUniversalTime(), DateTimeKind.Utc);
        var issuerNotBefore = new DateTimeOffset(issuerNotBeforeUtc, TimeSpan.Zero);
        var notBefore = proposedNotBefore > issuerNotBefore ? proposedNotBefore : issuerNotBefore;

        if (proposedNotBefore <= issuerNotBefore)
        {
            _logger.LogWarning(
                "Adjusted client certificate notBefore time from {ProposedNotBefore} to {ActualNotBefore} " +
                "to ensure it's not earlier than issuer certificate's NotBefore ({IssuerNotBefore})",
                proposedNotBefore, notBefore, issuerNotBefore);
        }

        var notAfter = now.AddYears(5);

        try
        {
            _logger.LogDebug("Creating certificate with notBefore={NotBefore}, notAfter={NotAfter}", 
                notBefore, notAfter);
            
            // Explicitly specify UTC DateTimeKind to avoid timezone conversion issues
            var notBeforeUtc = DateTime.SpecifyKind(notBefore.UtcDateTime, DateTimeKind.Utc);
            var notAfterUtc = DateTime.SpecifyKind(notAfter.UtcDateTime, DateTimeKind.Utc);
            
            var cert = req.Create(rootCertificate, notBeforeUtc, notAfterUtc, Guid.NewGuid().ToByteArray());

            // Create a certificate with private key
            var certWithKey = cert.CopyWithPrivateKey(rsa);

            // Export with private key and reimport to ensure proper key storage
            var pfxBytes = certWithKey.Export(X509ContentType.Pfx);
            
            var finalCert = X509CertificateLoader.LoadPkcs12(pfxBytes, null, X509KeyStorageFlags.Exportable);
            
            _logger.LogDebug("Successfully generated client certificate: Subject={Subject}", finalCert.Subject);
            return finalCert;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create client certificate using root CA");
            throw;
        }
    }
}

// Extension method for cleaner code
public static class CertificateExtensions
{
    public static T Let<T>(this T obj, Func<T, T> func)
    {
        return func(obj);
    }
}