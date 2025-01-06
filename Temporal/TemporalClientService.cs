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
                ClientCert = await File.ReadAllBytesAsync(Config.FlowServerCertPath),
                ClientPrivateKey = await File.ReadAllBytesAsync(Config.FlowServerPrivateKeyPath),
            }
        });

        return _client;
    }
}