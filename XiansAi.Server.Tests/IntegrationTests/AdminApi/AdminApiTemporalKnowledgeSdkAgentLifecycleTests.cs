using System.Net;
using Temporalio.Activities;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Knowledge GetAsync / ListAsync from a Temporal activity and from workflow code
/// (Lib stubs HTTP to KnowledgeActivities). Get fallback and List isolation stay on
/// the other Knowledge cycles. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/knowledge/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalKnowledgeSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalKnowledgeSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public Task KnowledgeSdkAgent_GetAndList_FromActivity()
        => RunGetListCycleAsync(fromWorkflow: false);

    [Fact]
    public Task KnowledgeSdkAgent_GetAndList_FromWorkflow()
        => RunGetListCycleAsync(fromWorkflow: true);

    private async Task RunGetListCycleAsync(bool fromWorkflow)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"KnowSdk {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var expectedRun = fromWorkflow ? "run:ok:workflow:playbook" : "run:ok:activity:playbook";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterKnowledgeSdkAgent(host, agentName, fromWorkflow);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await CreateTenantKnowledgeAsync(tenantId, agentName, "playbook", "playbook content");
        await CreateTenantKnowledgeAsync(tenantId, agentName, "glossary", "glossary content");

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, expectedRun, userText: "run");

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterKnowledgeSdkAgent(
        LibAgentWorkflowHost host,
        string agentName,
        bool fromWorkflow)
    {
        var agent = host.RegisterTemplate(agentName, "Get/List knowledge from activity and workflow");
        var manage = agent.Workflows.DefineCustom<KnowledgeSdkManageWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Manage");
        manage.AddActivity(new KnowledgeSdkActivities());

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => HandleKnowledgeSdkChatAsync(context, fromWorkflow));
        return agent;
    }

    private static async Task HandleKnowledgeSdkChatAsync(UserMessageContext context, bool fromWorkflow)
    {
        try
        {
            var result = await XiansContext.Workflows.ExecuteAsync<KnowledgeSdkManageWorkflow, string>(
                new object[] { context.Message.Text?.Trim() ?? string.Empty, fromWorkflow },
                uniqueKey: Guid.NewGuid().ToString("N"));
            await context.ReplyAsync(result);
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

[Workflow("placeholder:KnowSdkManage")]
public class KnowledgeSdkManageWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string command, bool fromWorkflow)
    {
        if (fromWorkflow)
        {
            return KnowledgeSdkDispatch.RunAsync("workflow");
        }

        return Workflow.ExecuteActivityAsync(
            (KnowledgeSdkActivities activities) => activities.DispatchAsync(command),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }
}

public class KnowledgeSdkActivities
{
    [Activity]
    public Task<string> DispatchAsync(string command)
    {
        return KnowledgeSdkDispatch.RunAsync("activity");
    }
}

internal static class KnowledgeSdkDispatch
{
    public static async Task<string> RunAsync(string context)
    {
        var knowledge = XiansContext.CurrentAgent.Knowledge;
        var playbook = await knowledge.GetAsync("playbook");
        if (playbook?.Content != "playbook content")
        {
            return $"run:get-mismatch:{context}:{playbook?.Content ?? "missing"}";
        }

        var names = (await knowledge.ListAsync())
            .Select(item => item.Name)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        if (names.Count != 2 || names[0] != "glossary" || names[1] != "playbook")
        {
            return $"run:list-mismatch:{context}:{string.Join(",", names)}";
        }

        return $"run:ok:{context}:playbook";
    }
}
