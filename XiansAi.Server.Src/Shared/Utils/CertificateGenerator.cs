using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Linq;

namespace XiansAi.Server.Auth;

public class CertificateGenerator
{
    private readonly ILogger<CertificateGenerator> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly CertificatesConfig _certificatesConfig;
    public CertificateGenerator(IConfiguration configuration, 
        ILogger<CertificateGenerator> logger, 
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
        _certificatesConfig = _configuration.GetSection("Certificates").Get<CertificatesConfig>() ?? throw new InvalidOperationException("Certificates configuration not found");
    }

    public X509Certificate2 GetRootCertificate()
    {
        string pfxBase64 = _certificatesConfig.AppServerPfxBase64 ?? 
            throw new InvalidOperationException($"PFX data not found in configuration key CertificatesConfig.AppServerPfxBase64.");
        string? password = _certificatesConfig.AppServerCertPassword ?? 
            throw new InvalidOperationException($"Certificate password not found in configuration key CertificatesConfig.AppServerCertPassword.");

        // Convert base64 string to byte array
        byte[] pfxBytes = Convert.FromBase64String(pfxBase64);

        // Create certificate from PFX data with preserved storage provider
        var limits = new Pkcs12LoaderLimits { PreserveStorageProvider = true };
        return X509CertificateLoader.LoadPkcs12(pfxBytes, password,
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable,
            limits);
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

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var rootCertificate = GetRootCertificate();
        
        // Set validity period starting from now
        var now = DateTimeOffset.UtcNow;
        var notBefore = now.AddMinutes(-10); // Buffer for clock skew
        var notAfter = now.AddYears(5);

        // Add key usage extension
        req.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                false));

        var cert = req.Create(rootCertificate, 
            notBefore.DateTime, 
            notAfter.DateTime, 
            Guid.NewGuid().ToByteArray());

        // Create a certificate with private key
        var certWithKey = cert.CopyWithPrivateKey(rsa);
        
        // Export with private key and reimport to ensure proper key storage
        var pfxBytes = certWithKey.Export(X509ContentType.Pfx);
        
        // Configure loader limits to preserve the storage provider
        var limits = new Pkcs12LoaderLimits
        {
            PreserveStorageProvider = true
        };

        // Load using X509CertificateLoader with proper flags and limits
        return X509CertificateLoader.LoadPkcs12(pfxBytes, null, 
            X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.Exportable,
            limits);
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