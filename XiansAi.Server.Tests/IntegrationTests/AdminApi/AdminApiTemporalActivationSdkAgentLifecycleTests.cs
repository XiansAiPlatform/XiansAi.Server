using System.Net;
using System.Text.Json;
using Temporalio.Activities;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xians.Lib.Temporal.Workflows.Activations;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Manager + Target system agents on one Lib host. Manager chat ExecuteAsync a Lifecycle
/// workflow that calls agent.Tenant.Agent(target) Exists/Create/Activate/status/list/Deactivate
/// from a Temporal activity and, in a second test, from workflow code (Lib stubs HTTP to
/// ActivationActivities). Target Heartbeat is Activable and starts on SDK activate.
/// Agent API has no delete; deactivate is the remove step. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/activations/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalActivationSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalActivationSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public Task ActivationSdkAgent_ManagerProvisionsTarget_CreateActivateListDeactivate()
        => RunProvisionCycleAsync(fromWorkflow: false);

    [Fact]
    public Task ActivationSdkAgent_ManagerProvisionsTarget_FromWorkflow()
        => RunProvisionCycleAsync(fromWorkflow: true);

    private async Task RunProvisionCycleAsync(bool fromWorkflow)
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var managerName = $"Manager {Guid.NewGuid():N}";
        var targetName = $"Target {Guid.NewGuid():N}";
        var ghostName = $"Ghost {Guid.NewGuid():N}";
        const string managerActivation = "front-desk";
        const string demoActivation = "sdk-demo";
        var heartbeatId = $"{ownerTenant}:{targetName}:Heartbeat:{demoActivation}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var manager = RegisterManagerAgent(
            host, managerName, targetName, demoActivation, ghostName, fromWorkflow);
        var target = RegisterTargetAgent(host, targetName);
        await host.StartWorkersAsync(manager, target);
        await WaitForTemplateAsync(managerName);
        await WaitForTemplateAsync(targetName);
        await DeployLibTemplateAsync(ownerTenant, managerName);
        await DeployLibTemplateAsync(ownerTenant, targetName);
        await DeployLibTemplateAsync(otherTenant, managerName);
        await DeployLibTemplateAsync(otherTenant, targetName);

        var ownerManagerId = await ActivateLibAgentAsync(ownerTenant, managerName, managerActivation);
        var otherManagerId = await ActivateLibAgentAsync(otherTenant, managerName, managerActivation);

        await AssertAgentRepliesWithAsync(
            ownerTenant, managerName, managerActivation, "exists:true", userText: "exists");
        await AssertAgentRepliesWithAsync(
            ownerTenant, managerName, managerActivation, "missing:false", userText: "missing");
        await AssertAgentRepliesWithAsync(
            ownerTenant, managerName, managerActivation, "self:true", userText: "self");
        await AssertAgentRepliesWithAsync(
            ownerTenant, managerName, managerActivation, "status:NotFound", userText: "status");

        await AssertAgentRepliesWithAsync(
            ownerTenant, managerName, managerActivation, "provision:ok:", userText: "provision");
        var demoId = await WaitForNamedActivationAsync(ownerTenant, targetName, demoActivation, expectedActive: true);
        var startedIds = await GetActivationWorkflowIdsAsync(ownerTenant, demoId);
        Assert.Contains(heartbeatId, startedIds);
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, targetName, heartbeatId),
            $"Heartbeat {heartbeatId} did not start on SDK activate. Activation workflowIds: [{string.Join(", ", startedIds)}]");
        await AssertWorkflowStatusAsync(ownerTenant, heartbeatId, "Running");

        await AssertAgentRepliesWithAsync(
            otherTenant, managerName, managerActivation, "exists:true", userText: "exists");
        await AssertAgentRepliesWithAsync(
            otherTenant, managerName, managerActivation, "status:NotFound", userText: "status");

        BindTenantContext(otherTenant, _adminUserId!);
        var otherGet = await GetAsync(
            $"/api/v1/admin/tenants/{otherTenant}/agentActivations/{demoId}");
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);

        await AssertAgentRepliesWithAsync(
            ownerTenant, managerName, managerActivation, "deactivate:ok", userText: "deactivate");
        await AssertAgentRepliesWithAsync(
            ownerTenant, managerName, managerActivation, "status:Deactivated", userText: "status");
        await WaitForNamedActivationAsync(ownerTenant, targetName, demoActivation, expectedActive: false);
        await AssertWorkflowStatusAsync(ownerTenant, heartbeatId, "Canceled", "Terminated");

        BindTenantContext(ownerTenant, _adminUserId!);
        await RemoveLibActivationAsync(ownerTenant, demoId);
        await RemoveLibActivationAsync(ownerTenant, ownerManagerId);
        await RemoveLibActivationAsync(otherTenant, otherManagerId);
        await RemoveLibDeploymentAsync(ownerTenant, managerName);
        await RemoveLibDeploymentAsync(ownerTenant, targetName);
        await RemoveLibDeploymentAsync(otherTenant, managerName);
        await RemoveLibDeploymentAsync(otherTenant, targetName);
        await RemoveLibTemplateAsync(managerName);
        await RemoveLibTemplateAsync(targetName);
    }

    private static XiansAgent RegisterManagerAgent(
        LibAgentWorkflowHost host,
        string managerName,
        string targetName,
        string demoActivation,
        string ghostName,
        bool fromWorkflow)
    {
        var agent = host.RegisterTemplate(managerName, "Provisions another agent's activations through the SDK");
        var lifecycle = agent.Workflows.DefineCustom<ActivationSdkWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{managerName}:Lifecycle");
        lifecycle.AddActivity(new ActivationSdkActivities());

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context =>
            HandleManagerChatAsync(context, targetName, demoActivation, ghostName, fromWorkflow));
        return agent;
    }

    private static XiansAgent RegisterTargetAgent(LibAgentWorkflowHost host, string targetName)
    {
        var agent = host.RegisterTemplate(targetName, "Heartbeat starts when an activation is activated");
        agent.Workflows.DefineCustom<ActivationSdkHeartbeatWorkflow>(
            new WorkflowOptions { Activable = true },
            typeName: $"{targetName}:Heartbeat");
        return agent;
    }

    private static async Task HandleManagerChatAsync(
        UserMessageContext context,
        string targetName,
        string demoActivation,
        string ghostName,
        bool fromWorkflow)
    {
        var command = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var target = command == "missing" ? ghostName : targetName;
            var result = await XiansContext.Workflows.ExecuteAsync<ActivationSdkWorkflow, string>(
                new object[] { command, target, demoActivation, context.Message.ParticipantId, fromWorkflow },
                uniqueKey: command);
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task<string> WaitForNamedActivationAsync(
        string tenantId,
        string agentName,
        string activationName,
        bool expectedActive)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri =
            $"/api/v1/admin/tenants/{tenantId}/agentActivations?agentName={Uri.EscapeDataString(agentName)}";
        var lastBody = string.Empty;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await GetAsync(uri);
            lastBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(lastBody);
                foreach (var item in json.RootElement.EnumerateArray())
                {
                    if (!string.Equals(item.GetProperty("name").GetString(), activationName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (ReadActive(item) == expectedActive)
                    {
                        var id = item.GetProperty("id").GetString();
                        Assert.False(string.IsNullOrWhiteSpace(id));
                        return id!;
                    }
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail(
            $"Activation {activationName} for {agentName} did not reach active={expectedActive}. Last: {lastBody}");
        return string.Empty;
    }

    private static bool ReadActive(JsonElement item)
    {
        if (item.TryGetProperty("isActive", out var isActive) &&
            (isActive.ValueKind == JsonValueKind.True || isActive.ValueKind == JsonValueKind.False))
        {
            return isActive.GetBoolean();
        }

        return item.TryGetProperty("active", out var active) &&
               active.ValueKind == JsonValueKind.True;
    }

    private async Task<List<string>> GetActivationWorkflowIdsAsync(string tenantId, string activationId)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        if (!json.RootElement.TryGetProperty("workflowIds", out var ids) ||
            ids.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return ids.EnumerateArray()
            .Select(item => item.GetString() ?? string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToList();
    }

    private async Task AssertWorkflowStatusAsync(string tenantId, string workflowId, params string[] expectedStatuses)
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
                if (expectedStatuses.Any(expected =>
                    string.Equals(status, expected, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail(
            $"Workflow {workflowId} did not reach status {string.Join(" or ", expectedStatuses)}. Last: {lastBody}");
    }
}

[Workflow("placeholder:Lifecycle")]
public class ActivationSdkWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(
        string command,
        string targetAgent,
        string activationName,
        string participantId,
        bool fromWorkflow)
    {
        if (fromWorkflow)
        {
            return ActivationSdkDispatch.RunAsync(command, targetAgent, activationName, participantId);
        }

        return Workflow.ExecuteActivityAsync(
            (ActivationSdkActivities activities) =>
                activities.DispatchAsync(command, targetAgent, activationName, participantId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }
}

public class ActivationSdkActivities
{
    [Activity]
    public Task<string> DispatchAsync(
        string command,
        string targetAgent,
        string activationName,
        string participantId)
    {
        return ActivationSdkDispatch.RunAsync(command, targetAgent, activationName, participantId);
    }
}

internal static class ActivationSdkDispatch
{
    public static async Task<string> RunAsync(
        string command,
        string targetAgent,
        string activationName,
        string participantId)
    {
        var manager = XiansContext.CurrentAgent;
        var target = manager.Tenant.Agent(targetAgent);

        if (command == "exists" || command == "missing")
        {
            var prefix = command == "missing" ? "missing" : "exists";
            return await target.ExistsAsync() ? $"{prefix}:true" : $"{prefix}:false";
        }

        if (command == "self")
        {
            return await manager.ActivationExistsAsync() ? "self:true" : "self:false";
        }

        if (command == "status")
        {
            var status = await target.GetActivationStatusAsync(activationName);
            return $"status:{status}";
        }

        if (command == "provision")
        {
            if (!await target.ExistsAsync())
            {
                return "provision:missing-agent";
            }

            var created = await target.CreateActivationAsync(
                activationName,
                "Created by activation SDK test",
                participantId: participantId);
            var activated = await created.ActivateAsync();
            var listed = await target.ListActivationsAsync();
            var listedOk = listed.Any(item =>
                string.Equals(item.Name, activationName, StringComparison.Ordinal) && item.IsActive);
            var status = await target.GetActivationStatusAsync(activationName);
            var exists = await target.ActivationExistsAsync(activationName);
            if (!activated.IsActive || status != ActivationCheckStatus.Active || !exists || !listedOk)
            {
                return $"provision:check-failed:{status}:{exists}:{listedOk}";
            }

            if (activated.WorkflowIds == null || activated.WorkflowIds.Count == 0)
            {
                return $"provision:no-workflows:{activated.Id}";
            }

            return $"provision:ok:{activated.Id}";
        }

        if (command == "deactivate")
        {
            var listed = await target.ListActivationsAsync();
            var found = listed.FirstOrDefault(item =>
                string.Equals(item.Name, activationName, StringComparison.Ordinal));
            if (found == null)
            {
                return "deactivate:missing";
            }

            var deactivated = await found.DeactivateAsync();
            var status = await target.GetActivationStatusAsync(activationName);
            var exists = await target.ActivationExistsAsync(activationName);
            if (deactivated.IsActive || exists || status != ActivationCheckStatus.Deactivated)
            {
                return $"deactivate:failed:{status}:{exists}";
            }

            return "deactivate:ok";
        }

        return "unknown";
    }
}

[Workflow("placeholder:Heartbeat")]
public class ActivationSdkHeartbeatWorkflow
{
    [WorkflowRun]
    public Task RunAsync()
    {
        return Workflow.DelayAsync(TimeSpan.FromDays(30));
    }
}
