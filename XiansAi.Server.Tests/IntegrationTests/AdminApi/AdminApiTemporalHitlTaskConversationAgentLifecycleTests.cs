using System.Net;
using System.Text.Json;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Tasks;
using Xians.Lib.Agents.Tasks.Models;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// HITL create variants and the conversational SDK: CreateAndWaitAsync, fire-and-forget
/// CreateAsync with SurviveParentClose, and HitlTask.FromWorkflowIdAsync / ApproveAsync.
/// StartTask/GetResult and TaskCollection stay on the other HITL cycles.
/// See https://xiansaiplatform.github.io/XiansAi.Docs/concepts/hitl-tasks/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalHitlTaskConversationAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalHitlTaskConversationAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task HitlTaskConversationAgent_CreateAndWait_HitlTaskApprove_AndFireAndForget()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"HITLConv {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var waitName = $"wait-{Guid.NewGuid():N}"[..12];
        var forgetName = $"fg-{Guid.NewGuid():N}"[..12];
        var waitParentId = $"{tenantId}:{agentName}:Wait:{activationName}:{waitName}";
        var forgetParentId = $"{tenantId}:{agentName}:Forget:{activationName}:{forgetName}";
        var waitTaskId = $"{tenantId}:{agentName}:Task Workflow:{activationName}--{waitName}";
        var forgetTaskId = $"{tenantId}:{agentName}:Task Workflow:{activationName}--{forgetName}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterHitlConversationAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"waiting:{waitName}",
            userText: $"wait {waitName}");
        Assert.True(
            await WaitForWorkflowInListAsync(tenantId, agentName, waitParentId),
            $"Wait parent {waitParentId} did not start.");
        Assert.True(
            await WaitForTaskInListAsync(tenantId, agentName, waitTaskId),
            $"CreateAndWait task {waitTaskId} was not created.");

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"approved:{waitName}",
            userText: $"approve {waitTaskId}");
        await AssertWorkflowStatusAsync(tenantId, waitParentId, "Completed");
        await AssertTaskCompletedAsync(tenantId, waitTaskId, "approve");

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"spawned:{forgetName}",
            userText: $"forget {forgetName}");
        await AssertWorkflowStatusAsync(tenantId, forgetParentId, "Completed");
        Assert.True(
            await WaitForTaskInListAsync(tenantId, agentName, forgetTaskId),
            $"Fire-and-forget task {forgetTaskId} did not survive the parent.");
        await AssertTaskPendingAsync(tenantId, forgetTaskId);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"approved:{forgetName}",
            userText: $"approve {forgetTaskId}");
        await AssertTaskCompletedAsync(tenantId, forgetTaskId, "approve");

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterHitlConversationAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(
            agentName,
            "CreateAndWait, fire-and-forget Create, and HitlTask approve",
            enableTasks: true);
        var notActivable = new WorkflowOptions { Activable = false };
        agent.Workflows.DefineCustom<HitlConversationWaitWorkflow>(notActivable, typeName: $"{agentName}:Wait");
        agent.Workflows.DefineCustom<HitlConversationForgetWorkflow>(notActivable, typeName: $"{agentName}:Forget");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleHitlConversationChatAsync);
        return agent;
    }

    private static async Task HandleHitlConversationChatAsync(UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var space = text.IndexOf(' ');
            var command = space < 0 ? text : text[..space];
            var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

            if (command == "wait")
            {
                await XiansContext.Workflows.StartAsync<HitlConversationWaitWorkflow>(
                    new object[] { argument }, uniqueKey: argument);
                await context.ReplyAsync($"waiting:{argument}");
                return;
            }

            if (command == "forget")
            {
                await XiansContext.Workflows.ExecuteAsync<HitlConversationForgetWorkflow, string>(
                    new object[] { argument }, uniqueKey: argument);
                await context.ReplyAsync($"spawned:{argument}");
                return;
            }

            if (command == "approve")
            {
                var task = await HitlTask.FromWorkflowIdAsync(argument);
                var info = await task.GetInfoAsync();
                await task.ApproveAsync("from-sdk");
                await context.ReplyAsync($"approved:{info.Title}");
                return;
            }

            await context.ReplyAsync("unknown");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task<bool> WaitForTaskInListAsync(string tenantId, string agentName, string taskId)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri = $"/api/v1/admin/tenants/{tenantId}/tasks?agentName={Uri.EscapeDataString(agentName)}";
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await GetAsync(uri);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var item in json.RootElement.GetProperty("tasks").EnumerateArray())
                {
                    if (string.Equals(item.GetProperty("workflowId").GetString(), taskId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            await Task.Delay(250);
        }

        return false;
    }

    private async Task AssertTaskPendingAsync(string tenantId, string taskId)
    {
        await AssertTaskAsync(tenantId, taskId, json =>
            !json.GetProperty("isCompleted").GetBoolean() &&
            !json.GetProperty("timedOut").GetBoolean());
    }

    private async Task AssertTaskCompletedAsync(string tenantId, string taskId, string action)
    {
        await AssertTaskAsync(tenantId, taskId, json =>
            json.GetProperty("isCompleted").GetBoolean() &&
            string.Equals(json.GetProperty("performedAction").GetString(), action, StringComparison.Ordinal));
    }

    private async Task AssertTaskAsync(string tenantId, string taskId, Func<JsonElement, bool> match)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri = $"/api/v1/admin/tenants/{tenantId}/tasks/by-id?taskId={Uri.EscapeDataString(taskId)}";
        var lastBody = string.Empty;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await GetAsync(uri);
            lastBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(lastBody);
                if (match(json.RootElement))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Task {taskId} did not match. Last: {lastBody}");
    }

    private async Task AssertWorkflowStatusAsync(string tenantId, string workflowId, string expectedStatus)
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
                if (string.Equals(status, expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Workflow {workflowId} did not reach status {expectedStatus}. Last: {lastBody}");
    }
}

[Workflow("placeholder:HitlWait")]
public class HitlConversationWaitWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string taskName)
    {
        var result = await XiansContext.CurrentAgent.Tasks.CreateAndWaitAsync(
            new TaskWorkflowRequest
            {
                TaskName = taskName,
                Title = taskName,
                Description = $"Wait {taskName}",
                DraftWork = $"draft:{taskName}",
                Actions = ["approve", "reject"]
            });
        return result.PerformedAction ?? "none";
    }
}

[Workflow("placeholder:HitlForget")]
public class HitlConversationForgetWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string taskName)
    {
        await XiansContext.CurrentAgent.Tasks.CreateAsync(
            new TaskWorkflowRequest
            {
                TaskName = taskName,
                Title = taskName,
                Description = $"Forget {taskName}",
                DraftWork = $"draft:{taskName}",
                Actions = ["approve", "reject"],
                SurviveParentClose = true
            });
        return "spawned";
    }
}
