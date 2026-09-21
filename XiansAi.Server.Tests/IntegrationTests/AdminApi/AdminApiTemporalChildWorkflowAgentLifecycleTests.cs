using System.Net;
using System.Text.Json;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Parent [WorkflowRun] StartAsync / ExecuteAsync / SignalAsync uses Temporal child
/// workflows. Custom workflows cycle covers the same APIs from a chat activity (client path).
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/workflows/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalChildWorkflowAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalChildWorkflowAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task ChildWorkflowAgent_ParentRun_ExecuteStartAndSignal()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"Child {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var sku = $"sku-{Guid.NewGuid():N}"[..12];
        // Hold has no uniqueKey: SignalAsync builds the id from activation postfix only.
        var holdId = $"{tenantId}:{agentName}:Hold:{activationName}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterChildWorkflowAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"run:ok:in-stock:{sku}:granted",
            userText: $"run {sku}");
        Assert.True(
            await WaitForWorkflowInListAsync(tenantId, agentName, holdId),
            $"Child Hold {holdId} was not started.");
        await AssertWorkflowCompletedAsync(tenantId, holdId);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterChildWorkflowAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Parent workflow Start/Execute/Signal children");
        var notActivable = new WorkflowOptions { Activable = false };
        agent.Workflows.DefineCustom<ChildParentWorkflow>(
            notActivable, typeName: $"{agentName}:Parent");
        agent.Workflows.DefineCustom<ChildInventoryWorkflow>(
            notActivable, typeName: $"{agentName}:Inventory");
        agent.Workflows.DefineCustom<ChildHoldWorkflow>(
            notActivable, typeName: $"{agentName}:Hold");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleChildWorkflowChatAsync);
        return agent;
    }

    private static async Task HandleChildWorkflowChatAsync(UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2 || parts[0] != "run")
            {
                await context.ReplyAsync("unknown");
                return;
            }

            var result = await XiansContext.Workflows.ExecuteAsync<ChildParentWorkflow, string>(
                new object[] { parts[1] },
                uniqueKey: Guid.NewGuid().ToString("N"));
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task AssertWorkflowCompletedAsync(string tenantId, string workflowId)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri = $"/api/v1/admin/tenants/{tenantId}/workflows?workflowId={Uri.EscapeDataString(workflowId)}";
        var lastBody = string.Empty;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await GetAsync(uri);
            lastBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(lastBody);
                var status = json.RootElement.GetProperty("status").GetString();
                if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Workflow {workflowId} did not complete. Last: {lastBody}");
    }
}

[Workflow("placeholder:ChildParent")]
public class ChildParentWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string sku)
    {
        // These Start/Execute/Signal calls run inside a workflow → Temporal child path.
        var stock = await XiansContext.Workflows.ExecuteAsync<ChildInventoryWorkflow, string>(
            new object[] { sku });

        await XiansContext.Workflows.StartAsync<ChildHoldWorkflow>(Array.Empty<object>());
        await XiansContext.Workflows.SignalAsync<ChildHoldWorkflow>("CompleteAsync", "granted");

        return $"run:ok:{stock}:granted";
    }
}

[Workflow("placeholder:ChildInventory")]
public class ChildInventoryWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string sku)
    {
        return Task.FromResult($"in-stock:{sku}");
    }
}

[Workflow("placeholder:ChildHold")]
public class ChildHoldWorkflow
{
    private bool _done;
    private string _status = "idle";

    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await Workflow.WaitConditionAsync(() => _done);
        return _status;
    }

    [WorkflowSignal("CompleteAsync")]
    public Task CompleteAsync(string note)
    {
        _status = note;
        _done = true;
        return Task.CompletedTask;
    }
}
