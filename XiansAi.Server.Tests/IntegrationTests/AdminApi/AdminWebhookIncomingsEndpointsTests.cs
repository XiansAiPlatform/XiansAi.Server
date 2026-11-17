using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// A POST to a builtin webhook URL is recorded in <c>webhook_incomings</c> and can be listed,
/// read and deleted through the admin API. No agent runs here, so the call itself times out or
/// fails; it must be recorded either way.
/// </summary>
public class AdminWebhookIncomingsEndpointsTests : AdminApiIntegrationTestBase
{
    private const string Payload = "{\"action\":\"labeled\",\"label\":{\"name\":\"ai-dlc/pr/pr-review\"}}";

    public AdminWebhookIncomingsEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task BuiltinWebhookCall_IsRecordedAndCanBeListedReadAndDeleted()
    {
        var (tenantId, webhookId, webhookUrl) = await CreateBuiltinWebhookAsync();
        using var inbound = InboundClient();

        await PostInboundAsync(inbound, webhookUrl, Payload);

        var items = await ListIncomingsAsync(tenantId, webhookId);
        var item = Assert.Single(items);
        Assert.False(item.GetProperty("bodyTruncated").GetBoolean());
        var incomingId = item.GetProperty("id").GetString()!;

        var get = await GetAsync($"/api/v1/admin/tenants/{tenantId}/webhooks/{webhookId}/incomings/{incomingId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var incoming = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        var root = incoming.RootElement;
        Assert.Equal(Payload, root.GetProperty("body").GetString());
        Assert.Equal(webhookId, root.GetProperty("integrationId").GetString());
        Assert.StartsWith("application/json", root.GetProperty("contentType").GetString());
        // Each call runs in its own workflow, keyed by the call's request ID.
        var requestId = root.GetProperty("requestId").GetString();
        Assert.False(string.IsNullOrEmpty(requestId));
        var workflowId = root.GetProperty("workflowId").GetString();
        Assert.NotNull(workflowId);
        Assert.StartsWith($"{tenantId}:", workflowId);
        Assert.EndsWith($":Integrator Workflow:hooks:{requestId}", workflowId);

        var headers = root.GetProperty("headers").EnumerateArray()
            .Select(h => (Name: h.GetProperty("name").GetString()!, Value: h.GetProperty("value").GetString()!))
            .ToList();
        Assert.Contains(("X-GitHub-Event", "issues"), headers);
        Assert.Contains(("X-Hub-Signature-256", "sha256=abc"), headers);
        Assert.Contains(("Cookie", "********"), headers);

        var stored = await _database.GetCollection<BsonDocument>("webhook_incomings")
            .Find(new BsonDocument("tenant_id", tenantId)).SingleAsync();
        Assert.False(stored.Contains("headers"));
        Assert.DoesNotContain("session=secret", stored["headers_encrypted"].AsString);
        Assert.DoesNotContain("X-GitHub-Event", stored["headers_encrypted"].AsString);

        var delete = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/webhooks/{webhookId}/incomings/{incomingId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);
        var getAfterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/webhooks/{webhookId}/incomings/{incomingId}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDelete.StatusCode);
        Assert.Empty(await ListIncomingsAsync(tenantId, webhookId));
    }

    [Fact]
    public async Task BuiltinWebhookCall_PassesActivationValidation_InItsOwnWorkflow()
    {
        var (_, _, webhookUrl) = await CreateBuiltinWebhookAsync();
        using var inbound = InboundClient();

        // The call's workflow ends with its request ID, yet it belongs to the active "hooks" activation:
        // it is accepted and waits for an agent (none runs here) instead of being rejected.
        var status = await PostInboundAsync(inbound, webhookUrl, Payload);

        Assert.Equal(HttpStatusCode.RequestTimeout, status);
    }

    [Theory]
    [InlineData("agentName")]
    [InlineData("workflowName")]
    [InlineData("activationName")]
    public async Task BuiltinWebhookCall_WithAColonInAName_IsRejectedAndNotRecorded(string parameter)
    {
        var (tenantId, webhookId, webhookUrl) = await CreateBuiltinWebhookAsync();
        using var inbound = InboundClient();

        // A ':' in a name would add a segment to the workflow ID and shift the activation after it.
        var url = Regex.Replace(webhookUrl, $"([?&]{parameter}=)([^&]*)", "$1$2%3Aextra");
        Assert.NotEqual(webhookUrl, url);
        var status = await PostInboundAsync(inbound, url, Payload);

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Empty(await ListIncomingsAsync(tenantId, webhookId));
    }

    [Fact]
    public async Task DeleteAllIncomings_RemovesEveryRecordedCall()
    {
        var (tenantId, webhookId, webhookUrl) = await CreateBuiltinWebhookAsync();
        using var inbound = InboundClient();

        await PostInboundAsync(inbound, webhookUrl, Payload);
        await PostInboundAsync(inbound, webhookUrl, Payload);
        Assert.Equal(2, (await ListIncomingsAsync(tenantId, webhookId)).Count);

        var deleteAll = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/webhooks/{webhookId}/incomings");
        Assert.Equal(HttpStatusCode.OK, deleteAll.StatusCode);
        using var body = JsonDocument.Parse(await deleteAll.Content.ReadAsStringAsync());
        Assert.Equal(2, body.RootElement.GetProperty("deletedCount").GetInt64());
        Assert.Empty(await ListIncomingsAsync(tenantId, webhookId));
    }

    [Fact]
    public async Task DeletingTheWebhook_DeletesItsRecordedCalls()
    {
        var (tenantId, webhookId, webhookUrl) = await CreateBuiltinWebhookAsync();
        using var inbound = InboundClient();
        await PostInboundAsync(inbound, webhookUrl, Payload);

        var delete = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/webhooks/{webhookId}");
        Assert.Equal(HttpStatusCode.OK, delete.StatusCode);

        var rows = await _database.GetCollection<BsonDocument>("webhook_incomings")
            .CountDocumentsAsync(new BsonDocument { { "tenant_id", tenantId }, { "integration_id", webhookId } });
        Assert.Equal(0, rows);
        var list = await GetAsync($"/api/v1/admin/tenants/{tenantId}/webhooks/{webhookId}/incomings");
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
    }

    [Fact]
    public async Task Incomings_OfAnotherTenantsWebhook_AreNotFound()
    {
        var (_, webhookId, webhookUrl) = await CreateBuiltinWebhookAsync();
        using var inbound = InboundClient();
        await PostInboundAsync(inbound, webhookUrl, Payload);

        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(otherTenant);
        await CreateTestTenantAsync(otherTenant);

        var list = await GetAsync($"/api/v1/admin/tenants/{otherTenant}/webhooks/{webhookId}/incomings");
        Assert.Equal(HttpStatusCode.NotFound, list.StatusCode);
        var deleteAll = await DeleteAsync($"/api/v1/admin/tenants/{otherTenant}/webhooks/{webhookId}/incomings");
        Assert.Equal(HttpStatusCode.NotFound, deleteAll.StatusCode);
    }

    /// <summary>Tenant, agent, active activation "hooks" and its Integrator Workflow, plus a builtin webhook on them.</summary>
    private async Task<(string TenantId, string WebhookId, string WebhookUrl)> CreateBuiltinWebhookAsync()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid():N}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId, "Integrator Workflow");
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "hooks");

        var create = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/webhooks", new
        {
            agentName = agent.Name,
            activationName = activation.Name,
            webhookName = "GitHub",
            timeoutInSeconds = 2
        });
        Assert.True(create.IsSuccessStatusCode, $"Create webhook failed: {create.StatusCode} {await create.Content.ReadAsStringAsync()}");
        using var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        return (
            tenantId,
            created.RootElement.GetProperty("id").GetString()!,
            created.RootElement.GetProperty("webhookUrl").GetString()!);
    }

    private HttpClient InboundClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Clear();
        return client;
    }

    /// <summary>Sends a GitHub-style call. Nothing answers it, so only the recording is checked.</summary>
    private static async Task<HttpStatusCode> PostInboundAsync(HttpClient inbound, string webhookUrl, string payload)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, webhookUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("X-GitHub-Event", "issues");
        request.Headers.TryAddWithoutValidation("X-Hub-Signature-256", "sha256=abc");
        request.Headers.TryAddWithoutValidation("Cookie", "session=secret");
        using var response = await inbound.SendAsync(request);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.NotFound, response.StatusCode);
        return response.StatusCode;
    }

    private async Task<List<JsonElement>> ListIncomingsAsync(string tenantId, string webhookId)
    {
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/webhooks/{webhookId}/incomings");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("items").EnumerateArray().Select(item => item.Clone()).ToList();
    }
}
