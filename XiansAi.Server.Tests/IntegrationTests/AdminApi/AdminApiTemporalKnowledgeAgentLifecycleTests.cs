using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Knowledge;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System Knowledge agent authored with Xians.Lib. Uploads system-scoped knowledge,
/// then proves tenant and activation overrides through the running worker:
/// another tenant still sees the original; another agent still sees the original;
/// the overridden agent/activation sees the new content.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalKnowledgeAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalKnowledgeAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task KnowledgeAgent_SystemUpload_TenantAndActivationOverridesIsolate()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Knowledge {Guid.NewGuid():N}";
        var otherAgentName = $"Knowledge Other {Guid.NewGuid():N}";
        const string knowledgeName = "playbook";
        const string systemOriginal = "system original playbook";
        const string tenantOverride = "tenant override playbook";
        const string activationOverride = "activation override playbook";
        const string ownerActivation = "front-desk";
        const string otherActivation = "back-office";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var knowledgeAgent = await RegisterKnowledgeAgentAsync(host, agentName, knowledgeName, systemOriginal);
        var otherAgent = await RegisterKnowledgeAgentAsync(host, otherAgentName, knowledgeName, systemOriginal);
        await host.StartWorkersAsync(knowledgeAgent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);

        await DeployLibTemplateAsync(ownerTenant, agentName);
        await DeployLibTemplateAsync(otherTenant, agentName);
        await DeployLibTemplateAsync(ownerTenant, otherAgentName);

        var ownerActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, ownerActivation);
        var otherTenantActivationId = await ActivateLibAgentAsync(otherTenant, agentName, ownerActivation);
        var otherAgentActivationId = await ActivateLibAgentAsync(ownerTenant, otherAgentName, ownerActivation);

        await AssertAgentRepliesWithAsync(ownerTenant, agentName, ownerActivation, systemOriginal);
        await AssertAgentRepliesWithAsync(otherTenant, agentName, ownerActivation, systemOriginal);
        await AssertAgentRepliesWithAsync(ownerTenant, otherAgentName, ownerActivation, systemOriginal);

        var systemKnowledge = await GetLatestKnowledgeAsync(ownerTenant, agentName, knowledgeName);
        Assert.Equal(systemOriginal, systemKnowledge.Content);
        Assert.True(systemKnowledge.SystemScoped);

        var tenantCopy = await OverrideKnowledgeAsync(ownerTenant, systemKnowledge.Id, "tenant");
        await PatchKnowledgeContentAsync(ownerTenant, tenantCopy.Id, tenantOverride);

        await AssertAgentRepliesWithAsync(ownerTenant, agentName, ownerActivation, tenantOverride);
        await AssertAgentRepliesWithAsync(otherTenant, agentName, ownerActivation, systemOriginal);
        await AssertAgentRepliesWithAsync(ownerTenant, otherAgentName, ownerActivation, systemOriginal);

        var tenantLatest = await GetLatestKnowledgeAsync(ownerTenant, agentName, knowledgeName);
        Assert.Equal(tenantOverride, tenantLatest.Content);
        Assert.False(tenantLatest.SystemScoped);

        var otherTenantLatest = await GetLatestKnowledgeAsync(otherTenant, agentName, knowledgeName);
        Assert.Equal(systemOriginal, otherTenantLatest.Content);
        Assert.True(otherTenantLatest.SystemScoped);

        var activationCopy = await OverrideKnowledgeAsync(
            ownerTenant, tenantLatest.Id, "activation", ownerActivation);
        await PatchKnowledgeContentAsync(ownerTenant, activationCopy.Id, activationOverride);

        await AssertAgentRepliesWithAsync(ownerTenant, agentName, ownerActivation, activationOverride);

        var otherActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, otherActivation);
        await AssertAgentRepliesWithAsync(ownerTenant, agentName, otherActivation, tenantOverride);
        await AssertAgentRepliesWithAsync(ownerTenant, otherAgentName, ownerActivation, systemOriginal);

        var frontDeskLatest = await GetLatestKnowledgeAsync(
            ownerTenant, agentName, knowledgeName, ownerActivation);
        Assert.Equal(activationOverride, frontDeskLatest.Content);
        Assert.Equal(ownerActivation, frontDeskLatest.ActivationName);

        var backOfficeLatest = await GetLatestKnowledgeAsync(
            ownerTenant, agentName, knowledgeName, otherActivation);
        Assert.Equal(tenantOverride, backOfficeLatest.Content);
        Assert.True(string.IsNullOrEmpty(backOfficeLatest.ActivationName));

        await RemoveLibActivationAsync(ownerTenant, ownerActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherAgentActivationId);
        await RemoveLibActivationAsync(otherTenant, otherTenantActivationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibDeploymentAsync(otherTenant, agentName);
        await RemoveLibDeploymentAsync(ownerTenant, otherAgentName);
        await RemoveLibTemplateAsync(agentName);
        await RemoveLibTemplateAsync(otherAgentName);
    }

    private static async Task<XiansAgent> RegisterKnowledgeAgentAsync(
        LibAgentWorkflowHost host,
        string agentName,
        string knowledgeName,
        string content)
    {
        var agent = host.RegisterTemplate(agentName, "Replies with the resolved playbook knowledge");
        var uploaded = await agent.Knowledge.UploadTextResourceAsync(knowledgeName, content, "text");
        Assert.True(uploaded, $"Failed to upload system knowledge '{knowledgeName}' for {agentName}.");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            var knowledge = await XiansContext.CurrentAgent.Knowledge.GetAsync(knowledgeName);
            await context.ReplyAsync(knowledge?.Content ?? "no knowledge found");
        });

        return agent;
    }
}
