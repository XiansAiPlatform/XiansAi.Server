using System.Net;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Tenant-scoped Lib agent (<c>IsTemplate = false</c>). There is no system template or deploy:
/// the worker listens on <c>{tenantId}:{workflowType}</c> and Admin send uses that queue.
/// Template/system-queue chat stays on Echo. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/multitenancy/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalTenantScopedAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalTenantScopedAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task TenantScopedAgent_RegisterActivateMessageAndRemove()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"Tenant Echo {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var userText = "hello from tenant queue";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = host.RegisterTenant(agentName, "Tenant-scoped echo; listens on the tenant queue");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            var message = context.Message.Text ?? string.Empty;
            await context.ReplyAsync($"TenantEcho: {message}");
        });

        await host.StartWorkersAsync(agent);
        await WaitForTenantAgentAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        var expected = $"TenantEcho: {userText}";
        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, expected,
            userText: userText);

        BindTenantContext(tenantId, _adminUserId!);
        var template = await GetAsync(
            $"/api/v1/admin/agentTemplates/by-name/{Uri.EscapeDataString(agentName)}");
        Assert.Equal(HttpStatusCode.NotFound, template.StatusCode);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
    }
}
