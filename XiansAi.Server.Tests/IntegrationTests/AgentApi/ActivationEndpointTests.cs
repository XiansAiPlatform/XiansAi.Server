using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Data;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// Integration tests for the AgentApi activation group: existence checks plus
/// list/create/activate/deactivate lifecycle under certificate auth.
/// </summary>
public class ActivationEndpointTests : IntegrationTestBase
{
    private static readonly JsonSerializerOptions JsonReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

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

    [Fact]
    public async Task List_ReturnsTenantActivations_AndFiltersByAgentName()
    {
        var agentA = $"list-agent-a-{Guid.NewGuid()}";
        var agentB = $"list-agent-b-{Guid.NewGuid()}";
        var activationA = $"activation-a-{Guid.NewGuid()}";
        var activationB = $"activation-b-{Guid.NewGuid()}";

        await CreateTestActivationAsync(agentA, activationA, active: true);
        await CreateTestActivationAsync(agentB, activationB, active: false);

        var allResponse = await _client.GetAsync("/api/agent/activation");
        Assert.Equal(HttpStatusCode.OK, allResponse.StatusCode);

        var allActivations = await allResponse.Content.ReadFromJsonAsync<List<AgentActivation>>(JsonReadOptions);
        Assert.NotNull(allActivations);
        Assert.Contains(allActivations!, a => a.Name == activationA && a.AgentName == agentA);
        Assert.Contains(allActivations!, a => a.Name == activationB && a.AgentName == agentB);

        var filteredResponse = await _client.GetAsync($"/api/agent/activation?agentName={agentA}");
        Assert.Equal(HttpStatusCode.OK, filteredResponse.StatusCode);

        var filtered = await filteredResponse.Content.ReadFromJsonAsync<List<AgentActivation>>(JsonReadOptions);
        Assert.NotNull(filtered);
        Assert.All(filtered!, a => Assert.Equal(agentA, a.AgentName));
        Assert.Contains(filtered!, a => a.Name == activationA);
        Assert.DoesNotContain(filtered!, a => a.Name == activationB);
    }

    [Fact]
    public async Task Create_ForExistingAgent_ReturnsActivation()
    {
        var agentName = $"create-agent-{Guid.NewGuid()}";
        var activationName = $"create-activation-{Guid.NewGuid()}";
        await CreateTestAgentAsync(agentName);

        var request = new CreateActivationRequest
        {
            Name = activationName,
            AgentName = agentName,
            Description = "Created via Agent API"
        };

        var response = await _client.PostAsJsonAsync("/api/agent/activation", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<AgentActivation>(JsonReadOptions);
        Assert.NotNull(created);
        Assert.Equal(activationName, created!.Name);
        Assert.Equal(agentName, created.AgentName);
        Assert.Equal(TestTenantId, created.TenantId);
        Assert.False(created.IsActive);
    }

    [Fact]
    public async Task Create_ForMissingAgent_ReturnsNotFound()
    {
        var request = new CreateActivationRequest
        {
            Name = $"missing-agent-activation-{Guid.NewGuid()}",
            AgentName = $"nonexistent-agent-{Guid.NewGuid()}"
        };

        var response = await _client.PostAsJsonAsync("/api/agent/activation", request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ActivateThenDeactivate_UpdatesExistsStatus()
    {
        var agentName = $"lifecycle-agent-{Guid.NewGuid()}";
        var activationName = $"lifecycle-activation-{Guid.NewGuid()}";

        await CreateTestAgentAsync(agentName);
        await CreateTestFlowDefinitionAsync(agentName);

        var createResponse = await _client.PostAsJsonAsync("/api/agent/activation", new CreateActivationRequest
        {
            Name = activationName,
            AgentName = agentName
        });
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<AgentActivation>(JsonReadOptions);
        Assert.NotNull(created);
        Assert.False(string.IsNullOrWhiteSpace(created!.Id));

        var existsBeforeActivate = await _client.GetAsync(
            $"/api/agent/activation/exists?activationName={activationName}&agentName={agentName}");
        Assert.Equal(HttpStatusCode.Conflict, existsBeforeActivate.StatusCode);

        var activateResponse = await _client.PostAsJsonAsync(
            $"/api/agent/activation/{created.Id}/activate",
            new ActivateAgentRequest());
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);

        var existsAfterActivate = await _client.GetAsync(
            $"/api/agent/activation/exists?activationName={activationName}&agentName={agentName}");
        Assert.Equal(HttpStatusCode.OK, existsAfterActivate.StatusCode);

        var deactivateResponse = await _client.PostAsJsonAsync(
            $"/api/agent/activation/{created.Id}/deactivate",
            new { });
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        var existsAfterDeactivate = await _client.GetAsync(
            $"/api/agent/activation/exists?activationName={activationName}&agentName={agentName}");
        Assert.Equal(HttpStatusCode.Conflict, existsAfterDeactivate.StatusCode);
    }

    [Fact]
    public async Task List_DoesNotReturnActivationsFromOtherTenants()
    {
        const string otherTenantId = "other-tenant-isolation";
        var foreignAgent = $"foreign-agent-{Guid.NewGuid()}";
        var foreignActivationName = $"foreign-activation-{Guid.NewGuid()}";
        var localAgent = $"local-agent-{Guid.NewGuid()}";
        var localActivationName = $"local-activation-{Guid.NewGuid()}";

        await CreateTestActivationAsync(foreignAgent, foreignActivationName, active: true, tenantId: otherTenantId);
        await CreateTestActivationAsync(localAgent, localActivationName, active: true);

        var response = await _client.GetAsync("/api/agent/activation");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var activations = await response.Content.ReadFromJsonAsync<List<AgentActivation>>(JsonReadOptions);
        Assert.NotNull(activations);
        Assert.Contains(activations!, a => a.Name == localActivationName && a.TenantId == TestTenantId);
        Assert.DoesNotContain(activations!, a => a.Name == foreignActivationName || a.TenantId == otherTenantId);
    }

    [Fact]
    public async Task Create_ForAgentInOtherTenant_ReturnsNotFound()
    {
        const string otherTenantId = "other-tenant-isolation";
        var foreignAgent = $"foreign-create-agent-{Guid.NewGuid()}";
        await CreateTestAgentAsync(foreignAgent, tenantId: otherTenantId);

        var response = await _client.PostAsJsonAsync("/api/agent/activation", new CreateActivationRequest
        {
            Name = $"activation-{Guid.NewGuid()}",
            AgentName = foreignAgent
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Activate_ForActivationInOtherTenant_ReturnsNotFound()
    {
        const string otherTenantId = "other-tenant-isolation";
        var foreignActivation = await CreateTestActivationAsync(
            $"foreign-activate-agent-{Guid.NewGuid()}",
            $"foreign-activate-{Guid.NewGuid()}",
            active: false,
            tenantId: otherTenantId);

        var response = await _client.PostAsJsonAsync(
            $"/api/agent/activation/{foreignActivation.Id}/activate",
            new ActivateAgentRequest());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deactivate_ForActivationInOtherTenant_ReturnsNotFound()
    {
        const string otherTenantId = "other-tenant-isolation";
        var foreignActivation = await CreateTestActivationAsync(
            $"foreign-deactivate-agent-{Guid.NewGuid()}",
            $"foreign-deactivate-{Guid.NewGuid()}",
            active: true,
            tenantId: otherTenantId);

        var response = await _client.PostAsJsonAsync(
            $"/api/agent/activation/{foreignActivation.Id}/deactivate",
            new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Exists_ForActivationInOtherTenant_ReturnsNotFound()
    {
        const string otherTenantId = "other-tenant-isolation";
        var agentName = $"foreign-exists-agent-{Guid.NewGuid()}";
        var activationName = $"foreign-exists-activation-{Guid.NewGuid()}";
        await CreateTestActivationAsync(agentName, activationName, active: true, tenantId: otherTenantId);

        var response = await _client.GetAsync(
            $"/api/agent/activation/exists?activationName={activationName}&agentName={agentName}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<AgentActivation> CreateTestActivationAsync(
        string agentName,
        string activationName,
        bool active,
        string? tenantId = null)
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
            TenantId = tenantId ?? TestTenantId,
            Active = active,
            ActivatedAt = active ? DateTime.UtcNow : null,
            DeactivatedAt = active ? null : DateTime.UtcNow
        };

        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<AgentActivation>("activations");
        await collection.InsertOneAsync(activation);

        return activation;
    }

    private async Task<Agent> CreateTestAgentAsync(string agentName, string? tenantId = null)
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
        return agent;
    }

    private async Task<FlowDefinition> CreateTestFlowDefinitionAsync(string agentName)
    {
        using var scope = _factory.Services.CreateScope();
        var flowDefinitionRepository = scope.ServiceProvider.GetRequiredService<IFlowDefinitionRepository>();

        // Activable=false so ActivateAgentAsync skips Temporal workflow start
        // but still marks the activation as active with an empty workflow list.
        var flowDefinition = new FlowDefinition
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Agent = agentName,
            WorkflowType = $"{agentName}:TestWorkflow",
            Hash = Guid.NewGuid().ToString("N"),
            Source = "// Test workflow source code",
            Markdown = "// Test markdown",
            Tenant = TestTenantId,
            Activable = false,
            ActivityDefinitions = new List<ActivityDefinition>
            {
                new ActivityDefinition
                {
                    ActivityName = "TestActivity",
                    AgentToolNames = new List<string> { "TestTool" },
                    KnowledgeIds = new List<string> { "TestKnowledge" },
                    ParameterDefinitions = new List<ParameterDefinition>
                    {
                        new ParameterDefinition { Name = "param1", Type = "string" }
                    }
                }
            },
            ParameterDefinitions = new List<ParameterDefinition>
            {
                new ParameterDefinition { Name = "workflowParam", Type = "string" }
            },
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await flowDefinitionRepository.CreateAsync(flowDefinition);
        return flowDefinition;
    }
}
