using Shared.Data.Models;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

[Collection(AdminApiTemporalCollection.Name)]
public abstract class AdminApiTemporalIntegrationTestBase : AdminApiIntegrationTestBase
{
    protected readonly TemporalFixture Temporal;

    protected AdminApiTemporalIntegrationTestBase(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
        Temporal = temporalFixture;
    }

    protected async Task<(string TenantId, Agent Agent, FlowDefinition Flow)> SeedTenantAgentAndFlowAsync()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid():N}", tenantId);
        var flow = await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        return (tenantId, agent, flow);
    }
}
