using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

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

        // Create certificate from PFX data
#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return new X509Certificate2(pfxBytes, password, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
#pragma warning restore SYSLIB0057 // Type or member is obsolete
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

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var rootCertificate = GetRootCertificate();
        var cert = req.Create(rootCertificate, 
            DateTimeOffset.Now, 
            DateTimeOffset.Now.AddYears(5), 
            Guid.NewGuid().ToByteArray());

        byte[] pfxBytes = cert.Export(X509ContentType.Pfx);
#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return new X509Certificate2(pfxBytes);
#pragma warning restore SYSLIB0057 // Type or member is obsolete
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