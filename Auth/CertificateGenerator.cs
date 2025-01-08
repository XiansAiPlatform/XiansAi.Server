using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;

namespace XiansAi.Server.Auth;

public class CertificateGenerator
{
    private readonly ILogger<CertificateGenerator> _logger;
    private readonly IHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    public CertificateGenerator(IConfiguration configuration, 
        ILogger<CertificateGenerator> logger, 
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
        _configuration = configuration;
    }

    public async Task<X509Certificate2> GetRootCertificate()
    {
        var rootCertPath = _configuration["Certificates:RootCert"];
        if (string.IsNullOrEmpty(rootCertPath))
        {
            return CreateOrLoadRootCertificateFile();
        } else {
            return await LoadFromKeyVault(rootCertPath);
        }
    }

    private async Task<X509Certificate2> LoadFromKeyVault(string certPath)
    {
        var credential = new DefaultAzureCredential();
        var certClient = new CertificateClient(new Uri(certPath), credential);
        var certResponse = await certClient.GetCertificateAsync("Certificates-RootCert");
        var cert = new X509Certificate2(certResponse.Value.Cer);
        return cert;
    }

    public X509Certificate2 CreateOrLoadRootCertificateFile()
    {
        var rootCertPath = _configuration["Certificates:RootCertPath"] ?? throw new InvalidOperationException("Root certificate path not found");
        var rootCertPassword = _configuration["Certificates:RootCertPassword"] ?? throw new InvalidOperationException("Root certificate password not found");
        if (!_environment.IsProduction() && !File.Exists(rootCertPath))
        {
            return CreateNewRootCertificate(rootCertPath);
        } 

        // In Production, the root certificate must be present
        if (!File.Exists(rootCertPath))
        {
            throw new InvalidOperationException("Root certificate not found");
        }
#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return new X509Certificate2(rootCertPath, rootCertPassword);
#pragma warning restore SYSLIB0057 // Type or member is obsolete

    }

    private X509Certificate2 CreateNewRootCertificate(string path)
    {
        _logger.LogWarning("Local certificate File not found, creating new root certificate");
        var rootCertPassword = _configuration["Certificates:RootCertPassword"] ?? throw new InvalidOperationException("Root certificate password not found");

        using var rsa = RSA.Create(4096);
        var rootReq = new CertificateRequest(
            $"CN=Flowmaxer Root-{DateTime.Now.Ticks}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        rootReq.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(true, false, 0, true));

        var rootCert = rootReq.CreateSelfSigned(
            DateTimeOffset.Now,
            DateTimeOffset.Now.AddYears(10));

        File.WriteAllBytes(path, 
            rootCert.Export(X509ContentType.Pfx, rootCertPassword));

        _logger.LogInformation("Root certificate created and saved to {path}", path);

        return rootCert;
    }

    public async Task<X509Certificate2> GenerateClientCertificate(string certName, string tenantName, string userName)
    {
        using var rsa = RSA.Create(2048);
        var distinguishedName = new X500DistinguishedName(
            $"CN={certName}--{tenantName}--{userName}, O={tenantName}");
        
        var req = new CertificateRequest(
            distinguishedName,
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var rootCertificate = await GetRootCertificate();
        var cert = req.Create(rootCertificate, 
            DateTimeOffset.Now, 
            DateTimeOffset.Now.AddYears(5), 
            Guid.NewGuid().ToByteArray());

#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057 // Type or member is obsolete
    }
}