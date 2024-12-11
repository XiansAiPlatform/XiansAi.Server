using Temporalio.Client;

public class TemporalClientService
{
    private readonly TemporalConfig _config;
    private ITemporalClient? _client;

    public TemporalClientService(TemporalConfig config)
    {
        _config = config;
    }

    public async Task<ITemporalClient> GetClientAsync()
    {
        if (_client != null) return _client;

        _client = await TemporalClient.ConnectAsync(new TemporalClientConnectOptions
        {
            TargetHost = _config.TemporalServerUrl,
            Namespace = _config.Namespace
        });

        return _client;
    }
}