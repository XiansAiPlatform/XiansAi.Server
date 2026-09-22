using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Client-only SignalWithStartAsync plus typed GetWorkflowHandleAsync / QueryAsync.
/// Start/Execute/Signal stay on the Custom workflows cycle.
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/workflows/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalWorkflowHandleAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalWorkflowHandleAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task WorkflowHandleAgent_SignalWithStart_QueryAndComplete()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"Handle {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var holdId = $"{tenantId}:{agentName}:Hold:{activationName}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterHandleAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, "run:ok:first:second",
            userText: "run");
        Assert.True(
            await WaitForWorkflowInListAsync(tenantId, agentName, holdId),
            $"Hold {holdId} was not started by SignalWithStart.");
        await AssertWorkflowCompletedAsync(tenantId, holdId);

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterHandleAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "SignalWithStart, GetWorkflowHandle, and Query");
        agent.Workflows.DefineCustom<WorkflowHandleHoldWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Hold");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleWorkflowHandleChatAsync);
        return agent;
    }

    private static async Task HandleWorkflowHandleChatAsync(UserMessageContext context)
    {
        try
        {
            if (context.Message.Text?.Trim() != "run")
            {
                await context.ReplyAsync("unknown");
                return;
            }

            await XiansContext.Workflows.SignalWithStartAsync<WorkflowHandleHoldWorkflow>(
                [],
                "PingAsync",
                uniqueKey: null,
                executionTimeout: null,
                activationName: null,
                "first");

            var handle = await XiansContext.Workflows.GetWorkflowHandleAsync<WorkflowHandleHoldWorkflow>(
                XiansContext.SafeIdPostfix);
            var first = await WaitForHoldStatusAsync(handle, "first");

            await XiansContext.Workflows.SignalWithStartAsync<WorkflowHandleHoldWorkflow>(
                [],
                "PingAsync",
                uniqueKey: null,
                executionTimeout: null,
                activationName: null,
                "second");
            var second = await WaitForHoldStatusAsync(handle, "second");

            await handle.SignalAsync(workflow => workflow.CompleteAsync("done"));
            await context.ReplyAsync($"run:ok:{first}:{second}");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private static async Task<string> WaitForHoldStatusAsync(
        Temporalio.Client.WorkflowHandle<WorkflowHandleHoldWorkflow> handle,
        string expected)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            try
            {
                var status = await handle.QueryAsync(workflow => workflow.GetStatus());
                if (status == expected)
                {
                    return status;
                }
            }
            catch (Exception)
            {
                // SignalWithStart is accepted before the worker has processed the first task.
            }

            await Task.Delay(250);
        }

        return $"timeout:{expected}";
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
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                using var json = System.Text.Json.JsonDocument.Parse(lastBody);
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

[Workflow("placeholder:Hold")]
public class WorkflowHandleHoldWorkflow
{
    private string _status = "idle";
    private bool _done;

    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await Workflow.WaitConditionAsync(() => _done);
        return _status;
    }

    [WorkflowQuery]
    public string GetStatus()
    {
        return _status;
    }

    [WorkflowSignal("PingAsync")]
    public Task PingAsync(string note)
    {
        _status = note;
        return Task.CompletedTask;
    }

    [WorkflowSignal("CompleteAsync")]
    public Task CompleteAsync(string note)
    {
        _status = note;
        _done = true;
        return Task.CompletedTask;
    }
}
