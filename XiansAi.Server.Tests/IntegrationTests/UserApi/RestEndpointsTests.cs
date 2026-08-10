using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Shared.Data.Models;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.IntegrationTests.UserApi;

/// <summary>
/// Smoke tests for the UserApi REST group. These verify API-key authentication is enforced and
/// that request validation runs before any workflow processing (which needs Temporal).
/// </summary>
public class RestEndpointsTests : IntegrationTestBase
{
    public RestEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task Send_WithoutApiKey_ReturnsUnauthorized()
    {
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var url = "/api/user/rest/send?workflow=test-workflow&type=Chat&participantId=user-1";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Send_WithInvalidMessageType_ReturnsBadRequest()
    {
        var apiKey = await CreateTestApiKeyAsync();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var url = $"/api/user/rest/send?workflow=test-workflow&type=NotAType&participantId=user-1&apikey={apiKey}";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Send_WithUnregisteredWorkflowType_ReturnsBadRequest()
    {
        var apiKey = await CreateTestApiKeyAsync();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var workflow = Uri.EscapeDataString("Unknown Agent:Supervisor Workflow");
        var url = $"/api/user/rest/send?workflow={workflow}&type=Chat&participantId=user-1&apikey={apiKey}&text=hello";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Unknown Agent", body);
    }

    [Fact]
    public async Task Send_WithUnknownActivation_ReturnsNotFound()
    {
        const string agentName = "RestTest Agent";
        await CreateTestFlowDefinitionAsync(agentName, "Supervisor Workflow");

        var apiKey = await CreateTestApiKeyAsync();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        // Full workflow id with an activation postfix that does not exist.
        var workflow = Uri.EscapeDataString($"{TestTenantId}:{agentName}:Supervisor Workflow:missing-activation");
        var url = $"/api/user/rest/send?workflow={workflow}&type=Chat&participantId=user-1&apikey={apiKey}&text=hello";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("missing-activation", body);
    }

    [Fact]
    public async Task Send_WithDeactivatedActivation_ReturnsConflict()
    {
        const string agentName = "RestTest Agent Deactivated";
        const string activationName = "inactive-one";
        await CreateTestFlowDefinitionAsync(agentName, "Supervisor Workflow");
        await CreateTestActivationAsync(agentName, activationName, active: false);

        var apiKey = await CreateTestApiKeyAsync();
        using var content = new StringContent("{}", Encoding.UTF8, "application/json");
        var workflow = Uri.EscapeDataString($"{TestTenantId}:{agentName}:Supervisor Workflow:{activationName}");
        var url = $"/api/user/rest/send?workflow={workflow}&type=Chat&participantId=user-1&apikey={apiKey}&text=hello";

        var client = _factory.CreateClient();
        var response = await client.PostAsync(url, content);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("deactivated", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<string> CreateTestApiKeyAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var apiKeyRepository = scope.ServiceProvider.GetRequiredService<IApiKeyRepository>();
        var (apiKey, _) = await apiKeyRepository.CreateAsync(TestTenantId, "test-rest-key-" + Guid.NewGuid(), "test-user");
        return apiKey;
    }

    private async Task CreateTestFlowDefinitionAsync(string agentName, string flowName)
    {
        using var scope = _factory.Services.CreateScope();
        var flowDefinitionRepository = scope.ServiceProvider.GetRequiredService<IFlowDefinitionRepository>();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();

        await agentRepository.CreateAsync(new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            OwnerAccess = new List<string> { "test-user" },
            ReadAccess = new List<string> { "test-user" },
            WriteAccess = new List<string> { "test-user" },
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            SystemScoped = false
        });

        await flowDefinitionRepository.CreateAsync(new FlowDefinition
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Agent = agentName,
            WorkflowType = $"{agentName}:{flowName}",
            Hash = Guid.NewGuid().ToString("N"),
            Source = "// test",
            Tenant = TestTenantId,
            Activable = false,
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            ActivityDefinitions = new List<ActivityDefinition>(),
            ParameterDefinitions = new List<ParameterDefinition>()
        });
    }

    private async Task CreateTestActivationAsync(string agentName, string activationName, bool active)
    {
        using var scope = _factory.Services.CreateScope();
        var activationRepository = scope.ServiceProvider.GetRequiredService<IActivationRepository>();

        await activationRepository.CreateAsync(new AgentActivation
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = activationName,
            AgentName = agentName,
            TenantId = TestTenantId,
            CreatedBy = "test-user",
            CreatedAt = DateTime.UtcNow,
            Active = active,
            ActivatedAt = active ? DateTime.UtcNow : null,
            DeactivatedAt = active ? null : DateTime.UtcNow
        });
    }
}
