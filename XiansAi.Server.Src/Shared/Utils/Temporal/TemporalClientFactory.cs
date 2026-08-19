using Shared.Auth;
using Temporalio.Client;

namespace Shared.Utils.Temporal;

public interface ITemporalClientFactory
{
    ITemporalClient GetClient(string? agentName);
    Task<ITemporalClient> GetClientAsync(string? agentName);
}

public class TemporalClientFactory : ITemporalClientFactory
{
    private readonly ITemporalClientService _temporalClientService;
    private readonly ITenantContext _tenantContext;

    public TemporalClientFactory(
        ITemporalClientService temporalClientService,
        ITenantContext tenantContext)
    {
        _temporalClientService = temporalClientService ?? throw new ArgumentNullException(nameof(temporalClientService));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    public ITemporalClient GetClient(string? agentName)
    {
        return _temporalClientService.GetClient(_tenantContext.TenantId, agentName);
    }

    public Task<ITemporalClient> GetClientAsync(string? agentName)
    {
        return _temporalClientService.GetClientAsync(_tenantContext.TenantId, agentName);
    }
} 