using System.Net;
using System.Text.Json;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Admin API HTTP tests that start workflows and talk to a stub Temporal worker.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task ActivateAndDeactivateAgent_StartsAndCleansUpWorkflows()
    {
        var (tenantId, agent, _) = await SeedTenantAgentAndFlowAsync();
        // CreateActivation sanitizes a missing participant to "", which WorkflowStarter
        // treats as the Temporal user id and then rejects. Pass a real participant.
        var create = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/agentActivations", new
        {
            name = "front-desk",
            agentName = agent.Name,
            participantId = _adminUserId
        });
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        using var createdJson = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        var activationId = createdJson.RootElement.GetProperty("id").GetString();
        Assert.False(string.IsNullOrWhiteSpace(activationId));

        var activate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/activate",
            new { });
        var activateBody = await activate.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        using var activateJson = JsonDocument.Parse(activateBody);
        Assert.True(
            activateJson.RootElement.GetProperty("workflowCount").GetInt32() >= 1,
            $"Expected at least one Temporal workflow to start. Response: {activateBody}");
        var workflowId = activateJson.RootElement.GetProperty("workflowIds")[0].GetString();
        Assert.False(string.IsNullOrWhiteSpace(workflowId));
        Assert.True(
            await WaitForWorkflowInListAsync(tenantId, agent.Name, workflowId!),
            $"Workflow {workflowId} did not appear in Temporal visibility.");

        var deactivate = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/agentActivations/{activationId}/deactivate",
            new { });
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var getAfter = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows?workflowId={Uri.EscapeDataString(workflowId!)}");
        Assert.Equal(HttpStatusCode.OK, getAfter.StatusCode);
        using var afterJson = JsonDocument.Parse(await getAfter.Content.ReadAsStringAsync());
        var status = afterJson.RootElement.GetProperty("status").GetString();
        Assert.False(
            string.Equals(status, "Running", StringComparison.OrdinalIgnoreCase),
            $"Workflow {workflowId} was still Running after deactivate (status: {status}).");
    }

    [Fact]
    public async Task SendMessage_ToActiveActivation_ReturnsOkAndPersistsHistory()
    {
        var (tenantId, agent, _, activation) = await SeedActiveActivationAsync();
        var participantId = "reader@example.com";

        var send = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
        {
            agentName = agent.Name,
            activationName = activation.Name,
            participantId,
            text = "hello from temporal tests"
        });
        Assert.Equal(HttpStatusCode.OK, send.StatusCode);

        var query =
            $"agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&participantId={Uri.EscapeDataString(participantId)}";
        var history = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains("hello from temporal tests", await history.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Heartbeat_WhenWorkerReplies_ReturnsAvailable()
    {
        var (tenantId, agent, flow, activation) = await SeedActiveActivationAsync();
        await using var worker = await StartWorkerAsync(ChatTaskQueue(tenantId, flow.WorkflowType));

        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/heartbeat?agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&timeoutSeconds=15");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("available").GetBoolean());
    }

    [Fact]
    public async Task GetWorkflowTypes_AfterWorkflowStart_IncludesStartedType()
    {
        var (tenantId, agent, flow) = await SeedTenantAgentAndFlowAsync();
        var activate = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/workflows/activate", new
        {
            workflowType = flow.WorkflowType,
            agentName = agent.Name,
            workflowIdPostfix = $"run-{Guid.NewGuid():N}"
        });
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        for (var attempt = 0; attempt < 20; attempt++)
        {
            var response = await GetAsync(
                $"/api/v1/admin/tenants/{tenantId}/workflows/types?agent={Uri.EscapeDataString(agent.Name)}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadAsStringAsync();
            if (body.Contains(flow.WorkflowType, StringComparison.Ordinal))
            {
                return;
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Workflow type {flow.WorkflowType} did not appear in Temporal visibility.");
    }
}
