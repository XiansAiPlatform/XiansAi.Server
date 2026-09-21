using System.Net;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Knowledge ListAsync is tenant- and agent-scoped. GetAsync fallback stays on the
/// Knowledge cycle. See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/knowledge/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalKnowledgeListAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalKnowledgeListAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task KnowledgeListAgent_ListAsync_AgentScoped()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"KnowList {Guid.NewGuid():N}";
        var otherAgentName = $"KnowList Other {Guid.NewGuid():N}";
        const string activationName = "front-desk";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterListAgent(host, agentName);
        var otherAgent = RegisterListAgent(host, otherAgentName);
        await host.StartWorkersAsync(agent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        await DeployLibTemplateAsync(tenantId, otherAgentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);
        var otherActivationId = await ActivateLibAgentAsync(tenantId, otherAgentName, activationName);

        await CreateTenantKnowledgeAsync(tenantId, agentName, "alpha", "alpha playbook");
        await CreateTenantKnowledgeAsync(tenantId, agentName, "beta", "beta playbook");
        await CreateTenantKnowledgeAsync(tenantId, otherAgentName, "gamma", "gamma playbook");

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "list:alpha,beta", userText: "list");
        await AssertAgentRepliesWithAsync(
            tenantId, otherAgentName, activationName, "list:gamma", userText: "list");

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibActivationAsync(tenantId, otherActivationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibDeploymentAsync(tenantId, otherAgentName);
        await RemoveLibTemplateAsync(agentName);
        await RemoveLibTemplateAsync(otherAgentName);
    }

    private static XiansAgent RegisterListAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Lists tenant knowledge through ListAsync");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleListChatAsync);
        return agent;
    }

    private static async Task HandleListChatAsync(UserMessageContext context)
    {
        try
        {
            if (context.Message.Text?.Trim() != "list")
            {
                await context.ReplyAsync("unknown");
                return;
            }

            var names = (await XiansContext.CurrentAgent.Knowledge.ListAsync())
                .Select(item => item.Name)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
            await context.ReplyAsync(names.Count == 0 ? "list:empty" : "list:" + string.Join(",", names));
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task CreateTenantKnowledgeAsync(
        string tenantId,
        string agentName,
        string name,
        string content)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/knowledge?agentName={Uri.EscapeDataString(agentName)}",
            new { name, content, type = "text" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
