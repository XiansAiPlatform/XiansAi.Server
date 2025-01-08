using System.Security.Cryptography.X509Certificates;
using Azure.Identity;
using Azure.Security.KeyVault.Certificates;
using Azure.Security.KeyVault.Secrets;

public interface IKeyVaultService
{
    Task<X509Certificate2> LoadFromKeyVault(string certificateName);
}

public class KeyVaultService : IKeyVaultService
{
    private readonly IConfiguration _configuration;
    public KeyVaultService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<X509Certificate2> LoadFromKeyVault(string certificateName)
    {
        var keyVaultUrl = new Uri(_configuration["KeyVaultUrl"]!);
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