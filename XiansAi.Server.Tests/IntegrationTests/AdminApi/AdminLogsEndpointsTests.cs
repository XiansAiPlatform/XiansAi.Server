using System.Net;
using System.Net.Http.Headers;
using System.Text;
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

    [Fact]
    public async Task ListLogs_FilteredByProperty_ReturnsOnlyMatchingValues()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "order opened",
            properties: new Dictionary<string, object> { ["status"] = "open", ["orderId"] = "A" });
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "order shipped",
            properties: new Dictionary<string, object> { ["status"] = "shipped", ["orderId"] = "A" });
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "plain log");

        var logs = await GetLogsByQueryAsync(tenantId, agentName, "&property=status:open");

        var log = Assert.Single(logs.Logs);
        Assert.Equal("order opened", log.Message);
    }

    [Fact]
    public async Task ListLogs_FilteredByPropertyWithSeveralValues_AcceptsCommaSeparatedAndRepeatedKey()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        foreach (var status in new[] { "open", "pending", "closed" })
        {
            await SeedLogAsync(tenantId, agentName, activationName, workflowId, status,
                properties: new Dictionary<string, object> { ["status"] = status });
        }

        var commaSeparated = await GetLogsByQueryAsync(tenantId, agentName, "&property=status:open,closed");
        var repeated = await GetLogsByQueryAsync(tenantId, agentName, "&property=status:open&property=status:closed");

        var expected = new[] { "closed", "open" };
        Assert.Equal(expected, commaSeparated.Logs.Select(l => l.Message).OrderBy(m => m));
        Assert.Equal(expected, repeated.Logs.Select(l => l.Message).OrderBy(m => m));
    }

    [Fact]
    public async Task ListLogs_FilteredByPropertyIds_ReturnsOnlyMatchingIds()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        foreach (var orderId in new[] { "A", "B", "C" })
        {
            await SeedLogAsync(tenantId, agentName, activationName, workflowId, $"order {orderId}",
                properties: new Dictionary<string, object> { ["orderId"] = orderId });
        }

        var logs = await GetLogsByQueryAsync(tenantId, agentName, "&property=orderId:A,B");

        Assert.Equal(new[] { "order A", "order B" }, logs.Logs.Select(l => l.Message).OrderBy(m => m));
    }

    [Fact]
    public async Task ListLogs_FilteredByTwoPropertyKeys_RequiresBothToMatch()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "A closed",
            properties: new Dictionary<string, object> { ["status"] = "closed", ["orderId"] = "A" });
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "A open",
            properties: new Dictionary<string, object> { ["status"] = "open", ["orderId"] = "A" });
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "B closed",
            properties: new Dictionary<string, object> { ["status"] = "closed", ["orderId"] = "B" });

        var logs = await GetLogsByQueryAsync(tenantId, agentName, "&property=status:closed&property=orderId:A");

        Assert.Equal("A closed", Assert.Single(logs.Logs).Message);
    }

    [Fact]
    public async Task ListLogs_PropertyFilter_CombinesWithExistingFilters()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "A failed", level: LogLevel.Error,
            properties: new Dictionary<string, object> { ["orderId"] = "A" });
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "A created",
            properties: new Dictionary<string, object> { ["orderId"] = "A" });

        var logs = await GetLogsByQueryAsync(tenantId, agentName, "&property=orderId:A&logLevel=Error");

        Assert.Equal("A failed", Assert.Single(logs.Logs).Message);
    }

    [Fact]
    public async Task ListLogs_WithoutPropertyFilter_ReturnsLogsWithAndWithoutProperties()
    {
        var (tenantId, agentName, activationName) = await SetUpTenantWithActivationAsync();
        var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, "Chat", activationName);
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "order log",
            properties: new Dictionary<string, object> { ["status"] = "open" });
        await SeedLogAsync(tenantId, agentName, activationName, workflowId, "plain log");

        var logs = await GetLogsByQueryAsync(tenantId, agentName, "");

        Assert.Equal(2, logs.TotalCount);
    }

    [Theory]
    [InlineData("properties.status:open")]
    [InlineData("status.nested:open")]
    [InlineData("$where:open")]
    [InlineData("status:")]
    [InlineData(":open")]
    [InlineData("status")]
    [InlineData("")]
    public async Task ListLogs_WithInvalidPropertyFilter_ReturnsBadRequest(string property)
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logs?property={Uri.EscapeDataString(property)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListLogs_WithTooLongPropertyKey_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/logs?property={new string('k', 65)}:v");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListLogs_FilteredByProperty_MatchesLogsIngestedThroughAgentApi()
    {
        // Agent logs arrive through POST /api/agent/logs, where properties are deserialized as
        // JsonElement values. Post the same JSON shape Xians.Lib sends instead of seeding Mongo.
        var agentName = $"agent-{Guid.NewGuid()}";
        var workflowId = WorkflowIdentifier.BuildWorkflowId(TestTenantId, agentName, "Chat", "front-desk");
        var payload = $$"""
            [
              { "level": 2, "message": "A opened", "workflowId": "{{workflowId}}", "agent": "{{agentName}}",
                "activation": "front-desk", "properties": { "status": "open", "orderId": "A", "attempt": 1 } },
              { "level": 2, "message": "A shipped", "workflowId": "{{workflowId}}", "agent": "{{agentName}}",
                "activation": "front-desk", "properties": { "status": "shipped", "orderId": "A" } },
              { "level": 2, "message": "B opened", "workflowId": "{{workflowId}}", "agent": "{{agentName}}",
                "activation": "front-desk", "properties": { "status": "open", "orderId": "B" } }
            ]
            """;

        using var agentClient = CreateAgentApiClient();
        var post = await agentClient.PostAsync("/api/agent/logs", new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.True(post.StatusCode == HttpStatusCode.OK, await post.Content.ReadAsStringAsync());

        await ConfigureAdminApiClientAsync(TestTenantId);
        await CreateTestTenantAsync(TestTenantId);
        var logs = await GetLogsByQueryAsync(TestTenantId, agentName, "&property=status:open&property=orderId:A");

        var log = Assert.Single(logs.Logs);
        Assert.Equal("A opened", log.Message);
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

    /// <summary>
    /// Client authenticated as an agent (test certificate) for the Agent API, alongside the
    /// AdminApi client used by the rest of this class.
    /// </summary>
    private HttpClient CreateAgentApiClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", TestApiKey);
        client.DefaultRequestHeaders.Add("X-Test-Certificate", TestCertificateThumbprint);
        client.DefaultRequestHeaders.Add("X-Tenant-Id", TestTenantId);
        return client;
    }

    private async Task<AdminLogsResponse> GetLogsByQueryAsync(string tenantId, string agentName, string query)
    {
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/logs?agentName={Uri.EscapeDataString(agentName)}{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await ReadAsJsonAsync<AdminLogsResponse>(response))!;
    }
}
