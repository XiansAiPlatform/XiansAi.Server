using Temporalio.Activities;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Webhook Create/List/Delete from a Temporal activity and from workflow code
/// (Lib stubs HTTP to WebhookActivities). Inbound POST and non-200 Respond stay on
/// the other Webhook cycles. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/webhooks/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalWebhookContextAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalWebhookContextAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public Task WebhookContextAgent_CreateListDelete_FromActivity()
        => RunManageCycleAsync(fromWorkflow: false);

    [Fact]
    public Task WebhookContextAgent_CreateListDelete_FromWorkflow()
        => RunManageCycleAsync(fromWorkflow: true);

    private async Task RunManageCycleAsync(bool fromWorkflow)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"HookCtx {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var expectedRun = fromWorkflow ? "run:ok:workflow" : "run:ok:activity";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterWebhookContextAgent(host, agentName, fromWorkflow);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, expectedRun, userText: "run");

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterWebhookContextAgent(
        LibAgentWorkflowHost host,
        string agentName,
        bool fromWorkflow)
    {
        var agent = host.RegisterTemplate(agentName, "Create/List/Delete webhooks from activity and workflow");
        var manage = agent.Workflows.DefineCustom<WebhookContextManageWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Manage");
        manage.AddActivity(new WebhookContextActivities());

        // Integrator is required so CreateAsync can target a builtin webhook workflow.
        var integrator = agent.Workflows.DefineIntegrator();
        integrator.OnWebhook(context => context.Respond("{\"ok\":true}"));

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => HandleWebhookContextChatAsync(context, fromWorkflow));
        return agent;
    }

    private static async Task HandleWebhookContextChatAsync(UserMessageContext context, bool fromWorkflow)
    {
        try
        {
            var result = await XiansContext.Workflows.ExecuteAsync<WebhookContextManageWorkflow, string>(
                new object[] { context.Message.Text?.Trim() ?? string.Empty, fromWorkflow },
                uniqueKey: Guid.NewGuid().ToString("N"));
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }
}

[Workflow("placeholder:HookCtxManage")]
public class WebhookContextManageWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string command, bool fromWorkflow)
    {
        if (fromWorkflow)
        {
            return WebhookContextDispatch.RunAsync("workflow");
        }

        return Workflow.ExecuteActivityAsync(
            (WebhookContextActivities activities) => activities.DispatchAsync(command),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }
}

public class WebhookContextActivities
{
    [Activity]
    public Task<string> DispatchAsync(string command)
    {
        return WebhookContextDispatch.RunAsync("activity");
    }
}

internal static class WebhookContextDispatch
{
    public static async Task<string> RunAsync(string context)
    {
        var webhooks = XiansContext.CurrentAgent.Webhooks;
        var hookName = $"Ctx-{Guid.NewGuid():N}"[..12];

        var created = await webhooks.CreateAsync(webhookName: hookName);
        if (string.IsNullOrWhiteSpace(created.Id) || string.IsNullOrWhiteSpace(created.WebhookUrl))
        {
            return $"run:create-empty:{context}";
        }

        var listed = await webhooks.ListAsync();
        if (listed.All(item => item.Id != created.Id))
        {
            return $"run:not-listed:{context}";
        }

        if (!await webhooks.DeleteAsync(created.Id))
        {
            return $"run:delete-failed:{context}";
        }

        var remaining = await webhooks.ListAsync();
        if (remaining.Any(item => item.Id == created.Id))
        {
            return $"run:still-listed:{context}";
        }

        return $"run:ok:{context}";
    }
}
