using System.Text.Json;
using Temporalio.Client;
using System.Collections.Concurrent;
using XiansAi.Server.Auth;

namespace XiansAi.Server.Temporal;

public interface ITemporalClientService
{
    ITemporalClient GetClient();

    TemporalConfig Config { get; }
}

public class TemporalClientService : ITemporalClientService
{
    private static readonly ConcurrentDictionary<string, ITemporalClient> _clients = new();
    public TemporalConfig Config { get; set; }
    private readonly IKeyVaultService _keyVaultService;
    private readonly ILogger<TemporalClientService> _logger;
    private readonly ITenantContext _tenantContext;

    public TemporalClientService(TemporalConfig config, 
    IKeyVaultService keyVaultService,
    ILogger<TemporalClientService> logger,
    ITenantContext tenantContext)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
        _keyVaultService = keyVaultService ?? throw new ArgumentNullException(nameof(keyVaultService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logger.LogInformation("TemporalClientService constructor initiated with config: {0}", JsonSerializer.Serialize(config));
    }

    public ITemporalClient GetClient()
    {
        var tenantId = _tenantContext.TenantId;
        return _clients.GetOrAdd(tenantId, _ => 
        {
            return TemporalClient.ConnectAsync(new(Config.FlowServerUrl)
            {
                Namespace = Config.FlowServerNamespace,
                Tls = new TlsOptions()
                {
                    ClientCert = GetCertificate().GetAwaiter().GetResult(),
                    ClientPrivateKey = GetPrivateKey().GetAwaiter().GetResult(),
                }
            }).GetAwaiter().GetResult();
        });
    }

    private async Task<byte[]> GetCertificate()
    {
        if (Config.CertificateFilePath != null)
        {
            _logger.LogInformation("Loading certificate from file---" + Config.CertificateFilePath + "---");
            return await File.ReadAllBytesAsync(Config.CertificateFilePath);
        }
        else if (Config.CertificateKeyVaultName != null)
        {
            var certificateString = await _keyVaultService.LoadSecret(Config.CertificateKeyVaultName);
            _logger.LogInformation("Loading certificate from key vault---" + certificateString + "---");
            // Clean up the private key string by removing whitespace, newlines, and BEGIN/END markers
            // certificateString = certificateString
            //     .Replace("-----BEGIN CERTIFICATE-----", "")
            //     .Replace("-----END CERTIFICATE-----", "")
            //     .Replace("\n", "")
            //     .Replace("\r", "")
            //     .Trim();
            return Convert.FromBase64String(certificateString);
        } else {
            throw new Exception("Certificate is not set in the configuration");
        }
    }

    private async Task<byte[]> GetPrivateKey()
    {
        if (Config.PrivateKeyFilePath != null)
            return await File.ReadAllBytesAsync(Config.PrivateKeyFilePath);
        else if (Config.PrivateKeyKeyVaultName != null)
        {
            var privateKeyString = await _keyVaultService.LoadSecret(Config.PrivateKeyKeyVaultName);
            _logger.LogInformation("Loading private key from key vault---" + privateKeyString + "---");
            
            // Clean up the private key string by removing whitespace, newlines, and BEGIN/END markers
            // privateKeyString = privateKeyString
            //     .Replace("-----BEGIN PRIVATE KEY-----", "")
            //     .Replace("-----END PRIVATE KEY-----", "")
            //     .Replace("\r", "")
            //     .Trim();

            try
            {
                return Convert.FromBase64String(privateKeyString);
            }
            catch (FormatException ex)
            {
                _logger.LogError($"Failed to decode private key: {ex.Message}");
                throw new FormatException("The private key retrieved from key vault is not in valid Base64 format", ex);
            }
        } else {
            throw new Exception("Private key is not set in the configuration");
        }
    }
}