using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;

public interface IKeyVaultService
{
    Task<X509Certificate2> LoadCertificate(string certificateName);
    Task<string> LoadSecret(string secretName);
}

public class KeyVaultService : IKeyVaultService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<KeyVaultService> _logger;
    public KeyVaultService(IConfiguration configuration, ILogger<KeyVaultService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> LoadSecret(string secretName)
    {
        _logger.LogInformation("Loading secret from key vault---" + secretName + "---");
        var keyVaultUrl = new Uri(_configuration["KeyVaultUri"]!);
        var creds = new DefaultAzureCredential();
        var secretClient = new SecretClient(keyVaultUrl, creds);
        var secretResp = await secretClient.GetSecretAsync(secretName);
        return secretResp.Value.Value;
    }

    public async Task<X509Certificate2> LoadCertificate(string certificateName)
    {
        _logger.LogInformation("Loading certificate from key vault---" + certificateName + "---");
        var keyVaultUrl = new Uri(_configuration["KeyVaultUri"]!);
        var creds = new DefaultAzureCredential();
        var certificateClient = new CertificateClient(keyVaultUrl, creds);

        var certResp = await certificateClient.GetCertificateAsync(certificateName);
        var identifer = new KeyVaultSecretIdentifier(certResp.Value.SecretId);

        var secretClient = new SecretClient(keyVaultUrl, creds);
        var secretResp = secretClient.GetSecret(identifer.Name, identifer.Version);

        byte[] privateKeyBytes = Convert.FromBase64String(secretResp.Value.Value);

#pragma warning disable SYSLIB0057 // Type or member is obsolete
        var result = new X509Certificate2(privateKeyBytes, string.Empty, X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet | X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057 // Type or member is obsolete
        return result;
    }

} 