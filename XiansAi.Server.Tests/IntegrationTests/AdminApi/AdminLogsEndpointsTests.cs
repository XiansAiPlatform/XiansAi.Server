using System.Net;
using Microsoft.Extensions.Logging;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminLogsEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminLogsEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task ListStreamsAndLogsThenDeleteByActivation_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, name: "front-desk");
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agent.Name, "Chat", activation.Name);
        await SeedLogAsync(tenantId, agent.Name, activation.Name, workflowId, "desk log");

        var streams = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs/streams?agentName={Uri.EscapeDataString(agent.Name)}");
        Assert.Equal(HttpStatusCode.OK, streams.StatusCode);
        var streamResult = await ReadAsJsonAsync<AdminLogStreamsResponse>(streams);
        Assert.Contains(streamResult!.Streams, stream => stream.WorkflowId == workflowId);

        var logs = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs?agentName={Uri.EscapeDataString(agent.Name)}&workflowId={Uri.EscapeDataString(workflowId)}");
        Assert.Equal(HttpStatusCode.OK, logs.StatusCode);
        var logResult = await ReadAsJsonAsync<AdminLogsResponse>(logs);
        Assert.Contains(logResult!.Logs, log => log.Message.Contains("desk log"));

        var delete = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs/agents/{Uri.EscapeDataString(agent.Name)}/activation/{Uri.EscapeDataString(activation.Name)}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var afterDelete = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs?agentName={Uri.EscapeDataString(agent.Name)}&workflowId={Uri.EscapeDataString(workflowId)}");
        Assert.Equal(HttpStatusCode.OK, afterDelete.StatusCode);
        var empty = await ReadAsJsonAsync<AdminLogsResponse>(afterDelete);
        Assert.DoesNotContain(empty!.Logs, log => log.WorkflowId == workflowId);
    }

    [Fact]
    public async Task ListLogs_WithInvalidPage_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logs?page=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SearchLogs_MatchesMessageAndPropertyValues_CaseInsensitive()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var invoiceStream = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Invoices", activationName);
        var messageStream = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        var otherStream = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Other", activationName);
        await SeedLogAsync(tenantId, agentName, activationName, invoiceStream, "invoice processed",
            properties: new Dictionary<string, object> { ["invoiceNumber"] = "INV-1042" });
        await SeedLogAsync(tenantId, agentName, activationName, messageStream, "Reminder sent for inv-1042");
        await SeedLogAsync(tenantId, agentName, activationName, otherStream, "unrelated log",
            properties: new Dictionary<string, object> { ["invoiceNumber"] = "INV-9999" });

        var streams = await GetStreamsAsync(tenantId, agentName, "inv-1042");
        Assert.Equal(
            new[] { invoiceStream, messageStream }.OrderBy(id => id),
            streams.Streams.Select(s => s.WorkflowId).OrderBy(id => id));

        var logs = await GetLogsAsync(tenantId, agentName, "INV-1042");
        Assert.Equal(2, logs.TotalCount);
        Assert.DoesNotContain(logs.Logs, log => log.WorkflowId == otherStream);
    }

    [Fact]
    public async Task SearchLogs_TreatsRegexCharactersAsPlainText()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "total (USD) 42.50");
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "total USD 42550");

        var logs = await GetLogsAsync(tenantId, agentName, "(USD) 42.5");
        Assert.Single(logs.Logs);

        var wildcard = await GetLogsAsync(tenantId, agentName, ".*");
        Assert.Empty(wildcard.Logs);
    }

    [Fact]
    public async Task SearchLogs_CombinesWithLevelFilter()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "INV-7 failed", level: LogLevel.Error);
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "INV-7 created");

        var logs = await GetLogsAsync(tenantId, agentName, "INV-7", "&logLevel=Error");

        var log = Assert.Single(logs.Logs);
        Assert.Equal("INV-7 failed", log.Message);
    }

    [Fact]
    public async Task SearchLogs_WithTooLongTerm_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var search = new string('a', 101);
        var logs = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logs?search={search}");
        var streams = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logs/streams?search={search}");

        Assert.Equal(HttpStatusCode.BadRequest, logs.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, streams.StatusCode);
    }

    private async Task<(string TenantId, string AgentName, string ActivationName)> SetUpTenantWithActivationAsync()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, name: "front-desk");
        return (tenantId, agent.Name, activation.Name);
    }

    private async Task<AdminLogStreamsResponse> GetStreamsAsync(string tenantId, string agentName, string search)
    {
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs/streams?agentName={Uri.EscapeDataString(agentName)}&search={Uri.EscapeDataString(search)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsJsonAsync<AdminLogStreamsResponse>(response))!;
    }

    private async Task<AdminLogsResponse> GetLogsAsync(string tenantId, string agentName, string search, string extraQuery = "")
    {
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs?agentName={Uri.EscapeDataString(agentName)}&search={Uri.EscapeDataString(search)}{extraQuery}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsJsonAsync<AdminLogsResponse>(response))!;
    }
}
