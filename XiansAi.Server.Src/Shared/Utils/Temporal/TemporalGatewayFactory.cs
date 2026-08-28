using Shared.Auth;
using Temporalio.Client;

namespace Shared.Utils.Temporal;

public interface ITemporalGatewayFactory
{
    IAsyncEnumerable<ITemporalClient> GetClientsAsync(string tenantId);
    IAsyncEnumerable<ITemporalClient> GetClientsForAgentAsync(string? agentName);
    Task<ITemporalClient> GetClientAsync();
    Task<ITemporalClient> GetClientAsync(string? agentName);
}

public class TemporalGatewayFactory : ITemporalGatewayFactory
{
    private readonly ITemporalGatewayService _temporalGatewayService;
    private readonly ITenantContext _tenantContext;
    public TemporalGatewayFactory(
        ITemporalGatewayService temporalGatewayService,
        ITenantContext tenantContext)
    {
        _temporalGatewayService = temporalGatewayService ?? throw new ArgumentNullException(nameof(temporalGatewayService));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public IAsyncEnumerable<ITemporalClient> GetClientsAsync(string tenantId)
    {
        return _temporalGatewayService.GetClientsAsync(tenantId);
    }

    public async IAsyncEnumerable<ITemporalClient> GetClientsForAgentAsync(string? agentName)
    {
        if (!string.IsNullOrEmpty(agentName))
        {
            yield return await _temporalGatewayService.GetClientAsync(_tenantContext.TenantId, agentName);
            yield break;
        }

        await foreach (var client in _temporalGatewayService.GetClientsAsync(_tenantContext.TenantId))
        {
            yield return client;
        }
    }

    public async Task<ITemporalClient> GetClientAsync()
    {
        return await _temporalGatewayService.GetClientAsync(_tenantContext.TenantId);
    }

    public async Task<ITemporalClient> GetClientAsync(string? agentName)
    {
        return await _temporalGatewayService.GetClientAsync(_tenantContext.TenantId, agentName);
    }
}
