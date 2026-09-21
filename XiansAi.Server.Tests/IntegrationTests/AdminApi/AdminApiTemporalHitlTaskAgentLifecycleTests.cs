using System.Net;
using System.Text.Json;
using Features.WebApi.Models;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Tasks.Models;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System HITL agent authored with Xians.Lib. EnableTasks registers {agent}:Task Workflow
/// on the hitl_task: queue. Chat starts a Review workflow that StartTaskAsync + GetResultAsync.
/// Admin list/get/draft/metadata/action round-trip the waiting task; a second Review times out.
/// Task IDs follow {tenant}:{agent}:Task Workflow:{activation}--{taskName}.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalHitlTaskAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalHitlTaskAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task HitlTaskAgent_StartTaskWait_AdminProgressAndTimeout()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"HITL {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var reviewName = $"rev-{Guid.NewGuid():N}"[..12];
        var expireName = $"exp-{Guid.NewGuid():N}"[..12];
        var reviewParentId = $"{ownerTenant}:{agentName}:Review:{activationName}:{reviewName}";
        var expireParentId = $"{ownerTenant}:{agentName}:Review:{activationName}:{expireName}";
        var reviewTaskId = $"{ownerTenant}:{agentName}:Task Workflow:{activationName}--{reviewName}";
        var expireTaskId = $"{ownerTenant}:{agentName}:Task Workflow:{activationName}--{expireName}";
        var tasksPath = $"/api/v1/admin/tenants/{ownerTenant}/tasks";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var agent = RegisterHitlAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(ownerTenant, agentName);
        var activationId = await ActivateLibAgentAsync(ownerTenant, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, $"waiting:{reviewName}",
            userText: $"review {reviewName}");
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, agentName, reviewParentId),
            $"Review {reviewParentId} did not start.");
        await AssertWorkflowStatusAsync(ownerTenant, reviewParentId, "Running");
        Assert.True(
            await WaitForTaskInListAsync(ownerTenant, agentName, reviewTaskId),
            $"Task {reviewTaskId} was not created by Review.");

        var listed = await GetTaskByIdAsync(ownerTenant, reviewTaskId);
        Assert.Equal("Approve order", listed.GetProperty("title").GetString());
        Assert.Equal($"draft:{reviewName}", listed.GetProperty("finalWork").GetString());
        Assert.False(listed.GetProperty("isCompleted").GetBoolean());
        Assert.False(listed.GetProperty("timedOut").GetBoolean());
        Assert.Contains(listed.GetProperty("availableActions").EnumerateArray(), item =>
            string.Equals(item.GetString(), "approve", StringComparison.Ordinal));

        var draft = await PutAsJsonAsync(
            $"{tasksPath}/draft?taskId={Uri.EscapeDataString(reviewTaskId)}",
            new UpdateDraftRequest { UpdatedDraft = $"revised:{reviewName}" });
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
        await AssertTaskAsync(ownerTenant, reviewTaskId, json =>
            string.Equals(json.GetProperty("finalWork").GetString(), $"revised:{reviewName}", StringComparison.Ordinal) &&
            string.Equals(json.GetProperty("initialWork").GetString(), $"draft:{reviewName}", StringComparison.Ordinal));

        var metadata = await PutAsJsonAsync(
            $"{tasksPath}/metadata?taskId={Uri.EscapeDataString(reviewTaskId)}",
            new UpdateMetadataRequest { Metadata = new Dictionary<string, object> { ["source"] = "admin-hitl" } });
        Assert.Equal(HttpStatusCode.OK, metadata.StatusCode);
        await AssertTaskAsync(ownerTenant, reviewTaskId, json =>
            json.TryGetProperty("metadata", out var meta) &&
            meta.ValueKind == JsonValueKind.Object &&
            meta.TryGetProperty("source", out _));

        BindTenantContext(otherTenant, _adminUserId!);
        var otherGet = await GetAsync(
            $"/api/v1/admin/tenants/{otherTenant}/tasks/by-id?taskId={Uri.EscapeDataString(reviewTaskId)}");
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);
        var otherList = await GetAsync($"/api/v1/admin/tenants/{otherTenant}/tasks");
        Assert.Equal(HttpStatusCode.OK, otherList.StatusCode);
        using (var otherJson = JsonDocument.Parse(await otherList.Content.ReadAsStringAsync()))
        {
            Assert.DoesNotContain(otherJson.RootElement.GetProperty("tasks").EnumerateArray(), item =>
                string.Equals(item.GetProperty("workflowId").GetString(), reviewTaskId, StringComparison.Ordinal));
        }

        BindTenantContext(ownerTenant, _adminUserId!);
        var action = await PostAsJsonAsync(
            $"{tasksPath}/actions?taskId={Uri.EscapeDataString(reviewTaskId)}",
            new PerformActionRequest { Action = "approve", Comment = "looks good" });
        Assert.Equal(HttpStatusCode.OK, action.StatusCode);
        await AssertTaskAsync(ownerTenant, reviewTaskId, json =>
            json.GetProperty("isCompleted").GetBoolean() &&
            string.Equals(json.GetProperty("performedAction").GetString(), "approve", StringComparison.Ordinal) &&
            string.Equals(json.GetProperty("comment").GetString(), "looks good", StringComparison.Ordinal) &&
            string.Equals(json.GetProperty("status").GetString(), "Completed", StringComparison.OrdinalIgnoreCase));
        await AssertWorkflowStatusAsync(ownerTenant, reviewParentId, "Completed");

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, $"waiting:{expireName}",
            userText: $"expire {expireName}");
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, agentName, expireParentId),
            $"Review {expireParentId} did not start.");
        Assert.True(
            await WaitForTaskInListAsync(ownerTenant, agentName, expireTaskId),
            $"Task {expireTaskId} was not created by Review.");
        await AssertTaskAsync(ownerTenant, expireTaskId, json =>
            json.GetProperty("timedOut").GetBoolean() &&
            !json.GetProperty("isCompleted").GetBoolean() &&
            json.GetProperty("performedAction").ValueKind is JsonValueKind.Null or JsonValueKind.Undefined &&
            string.Equals(json.GetProperty("status").GetString(), "Completed", StringComparison.OrdinalIgnoreCase));
        await AssertWorkflowStatusAsync(ownerTenant, expireParentId, "Completed");

        await RemoveLibActivationAsync(ownerTenant, activationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterHitlAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(
            agentName,
            "Creates a HITL task from Review and waits for Admin action or timeout",
            enableTasks: true);
        agent.Workflows.DefineCustom<HitlReviewWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Review");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleHitlChatAsync);
        return agent;
    }

    private static async Task HandleHitlChatAsync(UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var space = text.IndexOf(' ');
            var command = space < 0 ? text : text[..space];
            var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

            if (command == "review")
            {
                await XiansContext.Workflows.StartAsync<HitlReviewWorkflow>(
                    new object[] { argument, 0 }, uniqueKey: argument);
                await context.ReplyAsync($"waiting:{argument}");
                return;
            }

            if (command == "expire")
            {
                await XiansContext.Workflows.StartAsync<HitlReviewWorkflow>(
                    new object[] { argument, 2 }, uniqueKey: argument);
                await context.ReplyAsync($"waiting:{argument}");
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
        var uri =
            $"/api/v1/admin/tenants/{tenantId}/tasks?agentName={Uri.EscapeDataString(agentName)}";
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

    private async Task<JsonElement> GetTaskByIdAsync(string tenantId, string taskId)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/tasks/by-id?taskId={Uri.EscapeDataString(taskId)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task AssertTaskAsync(string tenantId, string taskId, Func<JsonElement, bool> matches)
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
                if (matches(json.RootElement))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Task {taskId} did not match expected state. Last: {lastBody}");
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

[Workflow("placeholder:Review")]
public class HitlReviewWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string taskName, int timeoutSeconds)
    {
        var handle = await XiansContext.CurrentAgent.Tasks.StartTaskAsync(
            new TaskWorkflowRequest
            {
                TaskName = taskName,
                Title = timeoutSeconds > 0 ? "Expire review" : "Approve order",
                Description = $"Review {taskName}",
                DraftWork = $"draft:{taskName}",
                Actions = ["approve", "reject", "hold"],
                Timeout = timeoutSeconds > 0 ? TimeSpan.FromSeconds(timeoutSeconds) : null
            });

        var result = await XiansContext.CurrentAgent.Tasks.GetResultAsync(handle);
        if (result.TimedOut)
        {
            return "timed-out";
        }

        return $"{result.PerformedAction}:{result.Comment}";
    }
}
