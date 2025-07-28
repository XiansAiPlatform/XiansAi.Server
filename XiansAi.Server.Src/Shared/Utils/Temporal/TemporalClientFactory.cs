using Shared.Auth;
using Temporalio.Client;

namespace Shared.Utils.Temporal;

public interface ITemporalClientFactory
{
    ITemporalClient GetClient();
    Task<ITemporalClient> GetClientAsync();
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

    public ITemporalClient GetClient()
    {
        return _temporalClientService.GetClient(_tenantContext.TenantId);
    }

    public Task<ITemporalClient> GetClientAsync()
    {
        return _temporalClientService.GetClientAsync(_tenantContext.TenantId);
    }
} 