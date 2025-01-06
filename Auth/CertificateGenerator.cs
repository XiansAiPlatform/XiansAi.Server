using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace XiansAi.Server.Auth;

public class CertificateGenerator
{

    //private readonly X509Certificate2 _rootCertificate;
    private readonly string _rootCertPath;
    private readonly string _rootCertPassword;
    private readonly ILogger<CertificateGenerator> _logger;
    private readonly IHostEnvironment _environment;

    public CertificateGenerator(IConfiguration configuration, 
        ILogger<CertificateGenerator> logger, 
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
        _rootCertPath = configuration["Certificates:RootCertPath"] ?? throw new InvalidOperationException("Root certificate path not found");
        _rootCertPassword = configuration["Certificates:RootCertPassword"] ?? throw new InvalidOperationException("Root certificate password not found");
    }

    public X509Certificate2 CreateOrLoadRootCertificate()
    {
        if (_environment.IsDevelopment() && !File.Exists(_rootCertPath))
        {
            return CreateNewRootCertificate();
        } 

        // In Production, the root certificate must be present
        if (!File.Exists(_rootCertPath))
        {
            throw new InvalidOperationException("Root certificate not found");
        }
#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return new X509Certificate2(_rootCertPath, _rootCertPassword);
#pragma warning restore SYSLIB0057 // Type or member is obsolete

    }

    private X509Certificate2 CreateNewRootCertificate()
    {
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

        File.WriteAllBytes(_rootCertPath, 
            rootCert.Export(X509ContentType.Pfx, _rootCertPassword));

        _logger.LogInformation("Root certificate created and saved to {RootCertPath}", _rootCertPath);

        return rootCert;
    }

    public X509Certificate2 GenerateClientCertificate(string certName, string tenantName, string userName)
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

        var rootCertificate = CreateOrLoadRootCertificate();
        var cert = req.Create(rootCertificate, 
            DateTimeOffset.Now, 
            DateTimeOffset.Now.AddYears(5), 
            Guid.NewGuid().ToByteArray());

#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057 // Type or member is obsolete
    }
}