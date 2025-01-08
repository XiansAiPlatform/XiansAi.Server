using Temporalio.Client;

namespace XiansAi.Server.Temporal;

public interface ITemporalClientService
{
    Task<ITemporalClient> GetClientAsync();

    TemporalConfig Config { get; }
}

public class TemporalClientService : ITemporalClientService
{
    private ITemporalClient? _client;

    public TemporalConfig Config { get; set; }

    public TemporalClientService(TemporalConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }


    public async Task<ITemporalClient> GetClientAsync()
    {
        if (_client != null) return _client;

        _client = await TemporalClient.ConnectAsync(new(Config.FlowServerUrl)
        {
            Namespace = Config.FlowServerNamespace,
            Tls = new TlsOptions()
            {
                ClientCert = await GetCertificate(),
                ClientPrivateKey = await GetPrivateKey(),
            }
        });

        return _client;
    }

    private async Task<byte[]> GetCertificate()
    {
        if (Config.CertificateFilePath != null)
            return await File.ReadAllBytesAsync(Config.CertificateFilePath);
        else
            return Convert.FromBase64String(Config.Certificate!);
    }

    private async Task<byte[]> GetPrivateKey()
    {
        if (Config.PrivateKeyFilePath != null)
            return await File.ReadAllBytesAsync(Config.PrivateKeyFilePath);
        else
            return Convert.FromBase64String(Config.PrivateKey!);
    }
}
