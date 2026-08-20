using Shared.Auth;
using Temporalio.Client;

namespace Shared.Utils.Temporal;

public interface ITemporalGatewayFactory
{
    IAsyncEnumerable<ITemporalClient> GetClientsAsync(string tenantId);
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

    public async Task<ITemporalClient> GetClientAsync()
    {
        return await _temporalGatewayService.GetClientInternalAsync(_tenantContext.TenantId, null);
    }

    public async Task<ITemporalClient> GetClientAsync(string? agentName)
    {
        return await _temporalGatewayService.GetClientInternalAsync(_tenantContext.TenantId, agentName);
    }
}