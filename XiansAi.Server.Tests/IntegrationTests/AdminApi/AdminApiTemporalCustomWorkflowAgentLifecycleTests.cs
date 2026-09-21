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
/// System custom-workflow agent authored with Xians.Lib. DefineCustom registers Temporal
/// classes under a runtime typeName. An Activable Onboarding workflow starts when the
/// agent is activated. Chat drives ExecuteAsync, StartAsync (uniqueKey), and SignalAsync.
/// Admin list/get/types/cancel round-trip those runs. Workflow IDs follow
/// {tenant}:{agent}:{name}:{activation}[:{uniqueKey}]. Client StartAsync uses UseExisting,
/// so a second hold of a running Approval does not create another execution.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalCustomWorkflowAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalCustomWorkflowAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task CustomWorkflowAgent_DefineCustom_StartExecuteSignalAndAdminOps()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Custom {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var sku = $"sku-{Guid.NewGuid():N}"[..12];
        var orderId = $"order-{Guid.NewGuid():N}"[..12];
        var inventoryType = $"{agentName}:Inventory Check";
        var paymentType = $"{agentName}:Payment";
        var approvalType = $"{agentName}:Approval";
        var onboardingType = $"{agentName}:Onboarding";
        var paymentId = $"{ownerTenant}:{agentName}:Payment:{activationName}:{orderId}";
        var approvalId = $"{ownerTenant}:{agentName}:Approval:{activationName}";
        var onboardingId = $"{ownerTenant}:{agentName}:Onboarding:{activationName}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var agent = RegisterCustomWorkflowAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(ownerTenant, agentName);
        var (activationId, startedIds) = await ActivateLibAgentWithWorkflowsAsync(
            ownerTenant, agentName, activationName);
        var startedOnboarding = Assert.Single(startedIds.Distinct(StringComparer.Ordinal));
        Assert.Equal(onboardingId, startedOnboarding);
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, agentName, onboardingId),
            $"Onboarding {onboardingId} did not start on activate.");
        await AssertWorkflowStatusAsync(ownerTenant, onboardingId, "Running", minHistoryLength: 2);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, $"in-stock:{sku}",
            userText: $"check {sku}");

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, $"started:{orderId}",
            userText: $"pay {orderId}");
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, agentName, paymentId),
            $"Payment {paymentId} did not appear in Temporal visibility.");
        await AssertWorkflowStatusAsync(ownerTenant, paymentId, "Completed");

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, "holding",
            userText: "hold");
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, agentName, approvalId),
            $"Approval {approvalId} did not appear in Temporal visibility.");
        await AssertWorkflowStatusAsync(ownerTenant, approvalId, "Running");

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, "holding",
            userText: "hold");
        Assert.Equal(1, await CountListedByIdAsync(ownerTenant, agentName, approvalId, "running"));

        await WaitForWorkflowTypesAsync(
            ownerTenant, agentName, inventoryType, paymentType, approvalType, onboardingType);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, "signaled:granted",
            userText: "approve granted");
        await AssertWorkflowStatusAsync(ownerTenant, approvalId, "Completed");

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, "holding",
            userText: "hold");
        await AssertWorkflowStatusAsync(ownerTenant, approvalId, "Running");

        BindTenantContext(ownerTenant, _adminUserId!);
        var cancel = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{ownerTenant}/workflows/cancel?workflowId={Uri.EscapeDataString(approvalId)}&force=true",
            new { });
        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        await AssertWorkflowStatusAsync(ownerTenant, approvalId, "Terminated");

        BindTenantContext(otherTenant, _adminUserId!);
        var otherGet = await GetAsync(
            $"/api/v1/admin/tenants/{otherTenant}/workflows?workflowId={Uri.EscapeDataString(paymentId)}");
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);

        BindTenantContext(ownerTenant, _adminUserId!);
        await RemoveLibActivationAsync(ownerTenant, activationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterCustomWorkflowAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Starts custom Temporal workflows from chat");
        var notActivable = new WorkflowOptions { Activable = false };
        agent.Workflows.DefineCustom<CustomOnboardingWorkflow>(
            new WorkflowOptions { Activable = true },
            typeName: $"{agentName}:Onboarding");
        agent.Workflows.DefineCustom<CustomInventoryCheckWorkflow>(notActivable, typeName: $"{agentName}:Inventory Check");
        agent.Workflows.DefineCustom<CustomPaymentWorkflow>(notActivable, typeName: $"{agentName}:Payment");
        agent.Workflows.DefineCustom<CustomApprovalWorkflow>(notActivable, typeName: $"{agentName}:Approval");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleCustomWorkflowChatAsync);
        return agent;
    }

    private static async Task HandleCustomWorkflowChatAsync(UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var space = text.IndexOf(' ');
            var command = space < 0 ? text : text[..space];
            var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

            if (command == "check")
            {
                var stock = await XiansContext.Workflows.ExecuteAsync<CustomInventoryCheckWorkflow, string>(
                    new object[] { argument });
                await context.ReplyAsync(stock);
                return;
            }

            if (command == "pay")
            {
                await XiansContext.Workflows.StartAsync<CustomPaymentWorkflow>(
                    new object[] { argument }, uniqueKey: argument);
                await context.ReplyAsync($"started:{argument}");
                return;
            }

            if (command == "hold")
            {
                await XiansContext.Workflows.StartAsync<CustomApprovalWorkflow>(Array.Empty<object>());
                await context.ReplyAsync("holding");
                return;
            }

            if (command == "approve")
            {
                await XiansContext.Workflows.SignalAsync<CustomApprovalWorkflow>("ApproveAsync", argument);
                await context.ReplyAsync($"signaled:{argument}");
                return;
            }

            await context.ReplyAsync("unknown");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task AssertWorkflowStatusAsync(
        string tenantId,
        string workflowId,
        string expectedStatus,
        int minHistoryLength = 0)
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
                var historyLength = json.RootElement.TryGetProperty("historyLength", out var history)
                    ? history.GetInt32()
                    : 0;
                if (string.Equals(status, expectedStatus, StringComparison.OrdinalIgnoreCase) &&
                    historyLength >= minHistoryLength)
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Workflow {workflowId} did not reach status {expectedStatus}. Last: {lastBody}");
    }

    private async Task WaitForWorkflowTypesAsync(string tenantId, string agentName, params string[] requiredTypes)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri = $"/api/v1/admin/tenants/{tenantId}/workflows/types?agent={Uri.EscapeDataString(agentName)}";
        var lastBody = string.Empty;

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await GetAsync(uri);
            lastBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(lastBody);
                var types = json.RootElement.EnumerateArray()
                    .Select(item => item.GetString() ?? string.Empty)
                    .ToHashSet(StringComparer.Ordinal);
                if (requiredTypes.All(types.Contains))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Workflow types for {agentName} did not include {string.Join(", ", requiredTypes)}. Body: {lastBody}");
    }

    private async Task<int> CountListedByIdAsync(
        string tenantId,
        string agentName,
        string workflowId,
        string status)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri =
            $"/api/v1/admin/tenants/{tenantId}/workflows/list?agent={Uri.EscapeDataString(agentName)}" +
            $"&status={Uri.EscapeDataString(status)}";
        var response = await GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var count = 0;
        foreach (var item in json.RootElement.GetProperty("workflows").EnumerateArray())
        {
            if (string.Equals(item.GetProperty("workflowId").GetString(), workflowId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}

[Workflow("placeholder:Onboarding")]
public class CustomOnboardingWorkflow
{
    [WorkflowRun]
    public Task RunAsync()
    {
        return Workflow.DelayAsync(TimeSpan.FromDays(30));
    }
}

[Workflow("placeholder:Inventory Check")]
public class CustomInventoryCheckWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string sku)
    {
        return Task.FromResult($"in-stock:{sku}");
    }
}

[Workflow("placeholder:Payment")]
public class CustomPaymentWorkflow
{
    [WorkflowRun]
    public Task RunAsync(string orderId)
    {
        _ = orderId;
        return Task.CompletedTask;
    }
}

[Workflow("placeholder:Approval")]
public class CustomApprovalWorkflow
{
    private bool _approved;
    private string _decision = string.Empty;

    [WorkflowRun]
    public async Task<string> RunAsync()
    {
        await Workflow.WaitConditionAsync(() => _approved);
        return _decision;
    }

    [WorkflowSignal("ApproveAsync")]
    public Task ApproveAsync(string decision)
    {
        _decision = decision;
        _approved = true;
        return Task.CompletedTask;
    }
}
