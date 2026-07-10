using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Data;
using Shared.Data.Models;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// Integration tests for the AgentApi activation group's "/exists" endpoint, covering the
/// active, not-found, and deactivated activation paths through the real database-backed repository.
/// </summary>
public class ActivationEndpointTests : IntegrationTestBase
{
    public ActivationEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task Exists_ForActiveActivation_ReturnsOk()
    {
        var agentName = $"test-agent-{Guid.NewGuid()}";
        var activationName = $"test-activation-{Guid.NewGuid()}";
        await CreateTestActivationAsync(agentName, activationName, active: true);

        var response = await _client.GetAsync(
            $"/api/agent/activation/exists?activationName={activationName}&agentName={agentName}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Exists_ForUnknownActivation_ReturnsNotFound()
    {
        var agentName = $"test-agent-{Guid.NewGuid()}";
        var activationName = $"nonexistent-activation-{Guid.NewGuid()}";

        var response = await _client.GetAsync(
            $"/api/agent/activation/exists?activationName={activationName}&agentName={agentName}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Exists_ForDeactivatedActivation_ReturnsConflict()
    {
        var agentName = $"test-agent-{Guid.NewGuid()}";
        var activationName = $"test-activation-{Guid.NewGuid()}";
        await CreateTestActivationAsync(agentName, activationName, active: false);

        var response = await _client.GetAsync(
            $"/api/agent/activation/exists?activationName={activationName}&agentName={agentName}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Exists_WithoutRequiredQueryParams_ReturnsBadRequest()
    {
        // Both 'activationName' and 'agentName' are required query parameters, so model binding rejects the request.
        var response = await _client.GetAsync("/api/agent/activation/exists");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private async Task<AgentActivation> CreateTestActivationAsync(string agentName, string activationName, bool active)
    {
        using var serviceScope = _factory.Services.CreateScope();
        var databaseService = serviceScope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var activation = new AgentActivation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = activationName,
            AgentName = agentName,
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            TenantId = TestTenantId,
            Active = active,
            ActivatedAt = active ? DateTime.UtcNow : null,
            DeactivatedAt = active ? null : DateTime.UtcNow
        };

        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<AgentActivation>("activations");
        await collection.InsertOneAsync(activation);

        return activation;
    }
}
