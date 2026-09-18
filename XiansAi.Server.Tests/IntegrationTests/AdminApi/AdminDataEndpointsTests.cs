using System.Net;
using System.Text.Json;
using MongoDB.Bson;
using Shared.Auth;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminDataEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminDataEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task CreateData_ThenList_ReturnsCreatedRecord()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);

        var key = $"acme-{Guid.NewGuid()}";
        var createResponse = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", new
        {
            agentName = agent.Name,
            dataType = "Companies",
            key,
            activationName = "email-responder",
            participantId = "user@example.com",
            content = new { status = "Completed" },
            metadata = new Dictionary<string, object> { ["city"] = "Oslo" }
        });

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        var created = await ReadAsJsonAsync<AdminDataItemResponse>(createResponse);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrEmpty(created!.Id));
        Assert.Equal(key, created.Key);
        Assert.Equal("Companies", created.Type);
        Assert.Equal(agent.Name, created.AgentName);
        Assert.Equal("Completed", created.Content.GetProperty("status").GetString());

        var listResponse = await GetAsync(ListUrl(tenantId, agent.Name, "Companies"));
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var list = await ReadAsJsonAsync<AdminDataListResponse>(listResponse);
        Assert.NotNull(list);
        Assert.Contains(list!.Data, item => item.Id == created.Id);
    }

    [Fact]
    public async Task CreateData_WithoutAuth_ReturnsUnauthorized()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", new
        {
            agentName = "any-agent",
            dataType = "Companies",
            key = "k1",
            content = new { status = "Completed" }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateData_AsTenantAdmin_AgainstOtherTenant_ReturnsForbidden()
    {
        var tenantA = $"test-tenant-{Guid.NewGuid()}";
        var tenantB = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantA);
        await CreateTestTenantAsync(tenantB);
        await ConfigureAdminApiClientAsync(tenantA, SystemRoles.TenantAdmin);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantA);

        // Auth resolves tenant from query before route. Using the caller's tenant in the query
        // lets authentication succeed so TenantRouteScopeFilter can reject the mismatched route.
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantB}/data?tenantId={tenantA}", new
        {
            agentName = agent.Name,
            dataType = "Companies",
            key = $"acme-{Guid.NewGuid()}",
            content = new { status = "Completed" }
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateData_DuplicateTypeAndKey_ReturnsConflict()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var key = $"acme-{Guid.NewGuid()}";
        var body = new
        {
            agentName = agent.Name,
            dataType = "Companies",
            key,
            content = new { status = "Completed" }
        };

        var first = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", body);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", body);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task CreateData_MissingRequiredFields_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);

        var missingAgent = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", new
        {
            dataType = "Companies",
            key = "k1",
            content = new { status = "Completed" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingAgent.StatusCode);

        var missingType = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", new
        {
            agentName = agent.Name,
            key = "k1",
            content = new { status = "Completed" }
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingType.StatusCode);

        var missingContent = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", new
        {
            agentName = agent.Name,
            dataType = "Companies",
            key = "k1"
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingContent.StatusCode);
    }

    [Fact]
    public async Task CreateData_UnknownAgent_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", new
        {
            agentName = $"missing-agent-{Guid.NewGuid()}",
            dataType = "Companies",
            key = $"acme-{Guid.NewGuid()}",
            content = new { status = "Completed" }
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateData_UpdatesContentAndMetadata()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var created = await CreateRecordAsync(tenantId, agent.Name);

        var updateResponse = await PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data/{created.Id}", new
        {
            content = new { status = "Updated" },
            metadata = new Dictionary<string, object> { ["city"] = "Bergen" }
        });

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        var updated = await ReadAsJsonAsync<AdminDataItemResponse>(updateResponse);
        Assert.NotNull(updated);
        Assert.Equal("Updated", updated!.Content.GetProperty("status").GetString());
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(agent.Name, updated.AgentName);
        Assert.Equal(created.Id, updated.Id);

        var getResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/data/{created.Id}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await ReadAsJsonAsync<AdminDataItemResponse>(getResponse);
        Assert.Equal("Updated", fetched!.Content.GetProperty("status").GetString());
        Assert.Equal("Bergen", fetched.Metadata!["city"].ToString());
    }

    [Fact]
    public async Task UpdateData_UnknownId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PutAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/data/{ObjectId.GenerateNewId()}",
            new { content = new { status = "Updated" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateData_RecordFromOtherTenant_ReturnsNotFound()
    {
        var tenantA = $"test-tenant-{Guid.NewGuid()}";
        var tenantB = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantA);
        await CreateTestTenantAsync(tenantB);

        await ConfigureAdminApiClientAsync(tenantA);
        var agentA = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantA);
        var created = await CreateRecordAsync(tenantA, agentA.Name);

        await ConfigureAdminApiClientAsync(tenantB);
        var response = await PutAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantB}/data/{created.Id}",
            new { content = new { status = "Updated" } });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateData_IgnoresTenantAndAgentInBody()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"test-agent-{Guid.NewGuid()}", tenantId);
        var created = await CreateRecordAsync(tenantId, agent.Name);

        var response = await PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data/{created.Id}", new
        {
            content = new { status = "Updated" },
            tenantId = "attacker-tenant",
            agentName = "other-agent",
            agentId = "other-agent"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await ReadAsJsonAsync<AdminDataItemResponse>(response);
        Assert.Equal(agent.Name, updated!.AgentName);
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Updated", updated.Content.GetProperty("status").GetString());
    }

    private async Task<AdminDataItemResponse> CreateRecordAsync(string tenantId, string agentName)
    {
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/data", new
        {
            agentName,
            dataType = "Companies",
            key = $"acme-{Guid.NewGuid()}",
            content = new { status = "Completed" },
            metadata = new Dictionary<string, object> { ["city"] = "Oslo" }
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await ReadAsJsonAsync<AdminDataItemResponse>(response);
        Assert.NotNull(created);
        return created!;
    }

    private static string ListUrl(string tenantId, string agentName, string dataType)
    {
        var start = Uri.EscapeDataString(DateTime.UtcNow.AddHours(-1).ToString("O"));
        var end = Uri.EscapeDataString(DateTime.UtcNow.AddHours(1).ToString("O"));
        return $"/api/v1/admin/tenants/{tenantId}/data?startDate={start}&endDate={end}&agentName={Uri.EscapeDataString(agentName)}&dataType={Uri.EscapeDataString(dataType)}";
    }
}
