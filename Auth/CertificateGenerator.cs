using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

public class CertificateGenerator
{

    private readonly X509Certificate2 _rootCertificate;
    private string? _rootCertPath;
    private string? _rootCertPassword;
    private readonly ILogger<CertificateGenerator> _logger;
    private readonly IHostEnvironment _environment;

    public CertificateGenerator(IConfiguration configuration, 
        ILogger<CertificateGenerator> logger, 
        IHostEnvironment environment)
    {
        _logger = logger;
        _environment = environment;
        _rootCertPath = configuration["RootCertPath"];
        _rootCertPassword = configuration["RootCertPassword"];

        // Create or load root certificate
        _rootCertificate = CreateOrLoadRootCertificate();

    }

    public X509Certificate2 CreateOrLoadRootCertificate()
    {
        // In development, use fixed paths and passwords
        if (string.IsNullOrEmpty(_rootCertPath) || string.IsNullOrEmpty(_rootCertPassword))
        {
            if (_environment.IsDevelopment())
            {
                _rootCertPath = "rootCert.pfx";
                _rootCertPassword = "-pwd-";
            } else {
                throw new InvalidOperationException("Root certificate path and password must be configured in production");
            }
        }

        if (File.Exists(_rootCertPath))
        {
#pragma warning disable SYSLIB0057 // Type or member is obsolete
            return new X509Certificate2(_rootCertPath, _rootCertPassword);
#pragma warning restore SYSLIB0057 // Type or member is obsolete
        }

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
        var req = new CertificateRequest(
            $"CN={certName.Trim('-')}-{tenantName.Trim('-')}-{userName.Trim('-')}",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        req.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(false, false, 0, false));

        var cert = req.Create(_rootCertificate, 
            DateTimeOffset.Now, 
            DateTimeOffset.Now.AddYears(5), 
            Guid.NewGuid().ToByteArray());

#pragma warning disable SYSLIB0057 // Type or member is obsolete
        return new X509Certificate2(cert.Export(X509ContentType.Pfx));
#pragma warning restore SYSLIB0057 // Type or member is obsolete
    }
}