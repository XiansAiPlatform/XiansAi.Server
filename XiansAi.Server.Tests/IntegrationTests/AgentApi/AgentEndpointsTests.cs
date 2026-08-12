using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// Integration tests for AgentApi agent discovery endpoints under certificate auth.
/// </summary>
public class AgentEndpointsTests : IntegrationTestBase
{
    public AgentEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task Exists_ForKnownAgent_ReturnsOk()
    {
        var agentName = $"exists-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);

        var response = await _client.GetAsync($"/api/agent/agents/exists?agentName={agentName}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Exists_ForUnknownAgent_ReturnsNotFound()
    {
        var agentName = $"missing-agent-{Guid.NewGuid()}";

        var response = await _client.GetAsync($"/api/agent/agents/exists?agentName={agentName}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Exists_WithoutAgentName_ReturnsBadRequest()
    {
        var response = await _client.GetAsync("/api/agent/agents/exists");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exists_ForAgentInOtherTenant_ReturnsNotFound()
    {
        const string otherTenantId = "other-tenant-isolation";
        var agentName = $"foreign-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName, tenantId: otherTenantId);

        var response = await _client.GetAsync($"/api/agent/agents/exists?agentName={agentName}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task CreateTestAgentAsync(string agentName, string? tenantId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = tenantId ?? TestTenantId,
            OwnerAccess = new List<string> { "test-user" },
            ReadAccess = new List<string> { "test-user" },
            WriteAccess = new List<string> { "test-user" },
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            SystemScoped = false
        };

        await agentRepository.CreateAsync(agent);
    }
}
