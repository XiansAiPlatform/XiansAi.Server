using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shared.Data.Models;
using Shared.Services;
using Shared.Utils;
using Temporalio.Client;
using Temporalio.Client.Schedules;
using Temporalio.Common;
using Temporalio.Worker;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

[Collection(AdminApiTemporalCollection.Name)]
public abstract partial class AdminApiTemporalIntegrationTestBase : AdminApiIntegrationTestBase
{
    protected readonly TemporalFixture Temporal;

    protected AdminApiTemporalIntegrationTestBase(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
        Temporal = temporalFixture;
    }

    protected async Task<(string TenantId, Agent Agent, FlowDefinition Flow)> SeedTenantAgentAndFlowAsync()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid():N}", tenantId);
        var flow = await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        return (tenantId, agent, flow);
    }

    protected async Task<(string TenantId, Agent Agent, FlowDefinition Flow, AgentActivation Activation)> SeedActiveActivationAsync(
        string activationName = "front-desk")
    {
        var (tenantId, agent, flow) = await SeedTenantAgentAndFlowAsync();
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: activationName);
        return (tenantId, agent, flow, activation);
    }

    protected string ChatTaskQueue(string tenantId, string workflowType) => $"{tenantId}:{workflowType}";

    protected Task<TemporalTestWorker> StartWorkerAsync(
        string taskQueue,
        WorkerDeploymentOptions? deploymentOptions = null)
    {
        var pendingRequests = _factory.Services.GetRequiredService<IPendingRequestService>();
        return TemporalTestWorker.StartAsync(
            Temporal.Environment.Client,
            taskQueue,
            pendingRequests,
            deploymentOptions);
    }

    protected async Task<bool> WaitForWorkflowInListAsync(string tenantId, string agentName, string workflowId)
    {
        var uri = $"/api/v1/admin/tenants/{tenantId}/workflows/list?agent={Uri.EscapeDataString(agentName)}";

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await GetAsync(uri);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                foreach (var item in json.RootElement.GetProperty("workflows").EnumerateArray())
                {
                    if (string.Equals(item.GetProperty("workflowId").GetString(), workflowId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            await Task.Delay(250);
        }

        return false;
    }

    protected SearchAttributeCollection SearchAttributes(
        string tenantId,
        string agentName,
        string userId,
        string idPostfix)
    {
        return new SearchAttributeCollection.Builder()
            .Set(SearchAttributeKey.CreateKeyword(Constants.TenantIdKey), tenantId)
            .Set(SearchAttributeKey.CreateKeyword(Constants.AgentKey), agentName)
            .Set(SearchAttributeKey.CreateKeyword(Constants.UserIdKey), userId)
            .Set(SearchAttributeKey.CreateKeyword(Constants.IdPostfixKey), idPostfix)
            .ToSearchAttributeCollection();
    }

    protected async Task CreateAgentScheduleAsync(
        string tenantId,
        string agentName,
        string workflowType,
        string scheduleId,
        string idPostfix)
    {
        var workflowId = $"{scheduleId}:run";
        var action = ScheduleActionStartWorkflow.Create(
            workflowType,
            Array.Empty<object>(),
            new WorkflowOptions(workflowId, ChatTaskQueue(tenantId, workflowType))
            {
                Memo = new Dictionary<string, object>
                {
                    [Constants.TenantIdKey] = tenantId,
                    [Constants.AgentKey] = agentName,
                    [Constants.IdPostfixKey] = idPostfix
                }
            });

        await Temporal.Environment.Client.CreateScheduleAsync(
            scheduleId,
            new Schedule(action, new ScheduleSpec { CronExpressions = ["0 * * * *"] }),
            new ScheduleOptions
            {
                TypedSearchAttributes = SearchAttributes(tenantId, agentName, _adminUserId ?? "test-admin", idPostfix)
            });
    }

    protected async Task<string> StartHitlTaskAsync(
        string tenantId,
        string agentName,
        string activationName,
        string taskQueue)
    {
        var workflowType = $"{agentName}:Task";
        var workflowId = $"{tenantId}:{workflowType}:{activationName}";
        var userId = _adminUserId ?? "test-admin";

        await Temporal.Environment.Client.StartWorkflowAsync(
            workflowType,
            Array.Empty<object>(),
            new WorkflowOptions(workflowId, taskQueue)
            {
                TypedSearchAttributes = SearchAttributes(tenantId, agentName, userId, activationName),
                Memo = new Dictionary<string, object>
                {
                    [Constants.TenantIdKey] = tenantId,
                    [Constants.AgentKey] = agentName,
                    [Constants.UserIdKey] = userId,
                    [Constants.IdPostfixKey] = activationName,
                    [Constants.TaskTitleKey] = "Test HITL task",
                    [Constants.TaskDescriptionKey] = "stub task",
                    [Constants.TaskActionsKey] = "approve,reject"
                }
            });

        return workflowId;
    }
}
