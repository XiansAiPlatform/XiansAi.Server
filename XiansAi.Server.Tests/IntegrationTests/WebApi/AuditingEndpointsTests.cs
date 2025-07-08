using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Xunit;
using Features.WebApi.Models;
using Features.WebApi.Repositories;
using Shared.Repositories;
using Shared.Data.Models;
using Shared.Data;
using Microsoft.Extensions.Logging;
using XiansAi.Server.Tests.TestUtils;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public class AuditingEndpointsTests : WebApiIntegrationTestBase
{
    private const string TestUserId = "test-user";
    private const string TestAgent = "test-agent";

    public AuditingEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task GetParticipantsForAgent_WithValidAgent_ReturnsParticipants()
    {
        // Arrange
        const string agentName = "test-agent";
        await CreateTestLogAsync(agentName, "participant-1");
        await CreateTestLogAsync(agentName, "participant-2");

        // Act
        var response = await GetAsync($"/api/client/auditing/agents/{agentName}/participants");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        
        Assert.True(jsonDoc.RootElement.TryGetProperty("totalCount", out var totalCountElement));
        Assert.True(jsonDoc.RootElement.TryGetProperty("participants", out var participantsElement));
        
        var totalCount = totalCountElement.GetInt64();
        var participants = participantsElement.EnumerateArray().Select(p => p.GetString()).ToList();
        
        Assert.True(totalCount >= 2);
        Assert.Contains("participant-1", participants);
        Assert.Contains("participant-2", participants);
    }

    [Fact]
    public async Task GetParticipantsForAgent_WithEmptyAgent_ReturnsNotFound()
    {
        // Act
        var response = await GetAsync("/api/client/auditing/agents//participants");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetParticipantsForAgent_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        const string agentName = "pagination-agent";
        for (int i = 1; i <= 25; i++)
        {
            await CreateTestLogAsync(agentName, $"participant-{i:D2}");
        }

        // Act
        var response = await GetAsync($"/api/client/auditing/agents/{agentName}/participants?page=2&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        
        Assert.True(jsonDoc.RootElement.TryGetProperty("page", out var pageElement));
        Assert.True(jsonDoc.RootElement.TryGetProperty("pageSize", out var pageSizeElement));
        Assert.True(jsonDoc.RootElement.TryGetProperty("totalPages", out var totalPagesElement));
        
        Assert.Equal(2, pageElement.GetInt32());
        Assert.Equal(10, pageSizeElement.GetInt32());
        Assert.True(totalPagesElement.GetInt32() >= 3);
    }

    [Fact]
    public async Task GetWorkflowTypes_WithValidAgent_ReturnsWorkflowTypes()
    {
        // Arrange
        const string agentName = "workflow-agent";
        await CreateTestLogAsync(agentName, "participant-1", "WorkflowType1");
        await CreateTestLogAsync(agentName, "participant-1", "WorkflowType2");

        // Act
        var response = await GetAsync($"/api/client/auditing/agents/{agentName}/workflow-types");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var workflowTypes = JsonSerializer.Deserialize<string[]>(content);
        
        Assert.NotNull(workflowTypes);
        Assert.Contains("WorkflowType1", workflowTypes);
        Assert.Contains("WorkflowType2", workflowTypes);
    }

    [Fact]
    public async Task GetWorkflowTypes_WithParticipantFilter_ReturnsFilteredWorkflowTypes()
    {
        // Arrange
        const string agentName = "filter-agent";
        await CreateTestLogAsync(agentName, "participant-1", "WorkflowType1");
        await CreateTestLogAsync(agentName, "participant-2", "WorkflowType2");

        // Act
        var response = await GetAsync($"/api/client/auditing/agents/{agentName}/workflow-types?participantId=participant-1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var workflowTypes = JsonSerializer.Deserialize<string[]>(content);
        
        Assert.NotNull(workflowTypes);
        Assert.Contains("WorkflowType1", workflowTypes);
        Assert.DoesNotContain("WorkflowType2", workflowTypes);
    }

    [Fact]
    public async Task GetWorkflowIdsForWorkflowType_WithValidParameters_ReturnsWorkflowIds()
    {
        // Arrange
        const string agentName = "workflow-ids-agent";
        const string workflowType = "TestWorkflowType";
        await CreateTestLogAsync(agentName, "participant-1", workflowType, "workflow-1");
        await CreateTestLogAsync(agentName, "participant-1", workflowType, "workflow-2");

        // Act
        var response = await GetAsync($"/api/client/auditing/agents/{agentName}/workflow-types/{workflowType}/workflow-ids");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var workflowIds = JsonSerializer.Deserialize<string[]>(content);
        
        Assert.NotNull(workflowIds);
        Assert.Contains("workflow-1", workflowIds);
        Assert.Contains("workflow-2", workflowIds);
    }

    [Fact]
    public async Task GetLogs_WithRequiredAgent_ReturnsLogs()
    {
        // Arrange
        const string agentName = "logs-agent";
        await CreateTestLogAsync(agentName, "participant-1");
        await CreateTestLogAsync(agentName, "participant-2");

        // Act
        var response = await GetAsync($"/api/client/auditing/logs?agent={agentName}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        
        Assert.True(jsonDoc.RootElement.TryGetProperty("totalCount", out var totalCountElement));
        Assert.True(jsonDoc.RootElement.TryGetProperty("logs", out var logsElement));
        
        var totalCount = totalCountElement.GetInt64();
        var logs = logsElement.EnumerateArray().ToList();
        
        Assert.True(totalCount >= 2);
        Assert.True(logs.Count >= 2);
    }

    [Fact]
    public async Task GetLogs_WithParticipantFilter_ReturnsFilteredLogs()
    {
        // Arrange
        const string agentName = "filtered-logs-agent";
        await CreateTestLogAsync(agentName, "participant-1");
        await CreateTestLogAsync(agentName, "participant-2");

        // Act
        var response = await GetAsync($"/api/client/auditing/logs?agent={agentName}&participantId=participant-1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        
        Assert.True(jsonDoc.RootElement.TryGetProperty("logs", out var logsElement));
        
        var logs = logsElement.EnumerateArray().ToList();
        
        foreach (var log in logs)
        {
            if (log.TryGetProperty("participantId", out var participantIdElement))
            {
                Assert.Equal("participant-1", participantIdElement.GetString());
            }
        }
    }

    [Fact]
    public async Task GetLogs_WithLogLevelFilter_ReturnsFilteredLogs()
    {
        // Arrange
        const string agentName = "level-filtered-agent";
        await CreateTestLogAsync(agentName, "participant-1", logLevel: LogLevel.Information);
        await CreateTestLogAsync(agentName, "participant-1", logLevel: LogLevel.Warning);

        // Act
        var response = await GetAsync($"/api/client/auditing/logs?agent={agentName}&logLevel=1");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        
        Assert.True(jsonDoc.RootElement.TryGetProperty("logs", out var logsElement));
        
        var logs = logsElement.EnumerateArray().ToList();
        
        foreach (var log in logs)
        {
            if (log.TryGetProperty("level", out var levelElement))
            {
                Assert.Equal("Information", levelElement.GetString());
            }
        }
    }

    [Fact]
    public async Task GetLogs_WithTimeRangeFilter_ReturnsFilteredLogs()
    {
        // Arrange
        const string agentName = "time-filtered-agent";
        var startTime = DateTime.UtcNow.AddHours(-1);
        var endTime = DateTime.UtcNow.AddMinutes(-30);
        
        await CreateTestLogAsync(agentName, "participant-1");

        // Act
        var response = await GetAsync($"/api/client/auditing/logs?agent={agentName}&startTime={startTime:yyyy-MM-ddTHH:mm:ssZ}&endTime={endTime:yyyy-MM-ddTHH:mm:ssZ}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        
        Assert.True(jsonDoc.RootElement.TryGetProperty("totalCount", out var totalCountElement));
        
        // Should return 0 logs since our test log was created after the endTime
        Assert.Equal(0, totalCountElement.GetInt64());
    }

    [Fact]
    public async Task GetLogs_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        const string agentName = "paginated-logs-agent";
        for (int i = 1; i <= 25; i++)
        {
            await CreateTestLogAsync(agentName, $"participant-{i}");
        }

        // Act
        var response = await GetAsync($"/api/client/auditing/logs?agent={agentName}&page=2&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var jsonDoc = JsonDocument.Parse(content);
        
        Assert.True(jsonDoc.RootElement.TryGetProperty("page", out var pageElement));
        Assert.True(jsonDoc.RootElement.TryGetProperty("pageSize", out var pageSizeElement));
        Assert.True(jsonDoc.RootElement.TryGetProperty("totalPages", out var totalPagesElement));
        Assert.True(jsonDoc.RootElement.TryGetProperty("logs", out var logsElement));
        
        Assert.Equal(2, pageElement.GetInt32());
        Assert.Equal(10, pageSizeElement.GetInt32());
        Assert.True(totalPagesElement.GetInt32() >= 3);
        Assert.True(logsElement.GetArrayLength() <= 10);
    }

    [Fact]
    public async Task GetLogs_WithMissingAgent_ReturnsBadRequest()
    {
        // Act
        var response = await GetAsync("/api/client/auditing/logs");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetLogs_WithEmptyAgent_ReturnsBadRequest()
    {
        // Act
        var response = await GetAsync("/api/client/auditing/logs?agent=");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetCriticalLogs_WithValidRequest_ReturnsCriticalLogs()
    {
        // Arrange
        await CreateTestAgentAsync("critical-agent-1");
        await CreateTestAgentAsync("critical-agent-2");
        
        await CreateTestLogAsync("critical-agent-1", "participant-1", logLevel: LogLevel.Critical);
        await CreateTestLogAsync("critical-agent-2", "participant-2", logLevel: LogLevel.Critical);

        // Act
        var response = await GetAsync("/api/client/auditing/critical-logs");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var content = await response.Content.ReadAsStringAsync();
        var criticalGroups = JsonSerializer.Deserialize<AgentCriticalGroup[]>(content);
        
        Assert.NotNull(criticalGroups);
        Assert.True(criticalGroups.Length >= 2);
    }

    private async Task<Agent> CreateTestAgentAsync(string agentName = "test-agent")
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        
        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            Permissions = new Permission
            {
                OwnerAccess = [TestUserId],
                ReadAccess = [TestUserId],
                WriteAccess = [TestUserId]
            },
            CreatedBy = TestUserId,
            CreatedAt = DateTime.UtcNow
        };

        await agentRepository.CreateAsync(agent);
        return agent;
    }

    private async Task<Agent> CreateTestAgentWithDifferentOwnerAsync(string agentName, string ownerId)
    {
        using var scope = _factory.Services.CreateScope();
        var agentRepository = scope.ServiceProvider.GetRequiredService<IAgentRepository>();
        
        var agent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = TestTenantId,
            Permissions = new Permission
            {
                OwnerAccess = [ownerId],
                ReadAccess = [ownerId],
                WriteAccess = [ownerId]
            },
            CreatedBy = ownerId,
            CreatedAt = DateTime.UtcNow
        };

        await agentRepository.CreateAsync(agent);
        return agent;
    }

    private async Task CreateTestLogAsync(
        string agentName, 
        string participantId = "test-participant", 
        string workflowType = "TestWorkflow",
        string workflowId = "test-workflow-id",
        LogLevel logLevel = LogLevel.Information)
    {
        using var scope = _factory.Services.CreateScope();
        var logRepository = scope.ServiceProvider.GetRequiredService<ILogRepository>();
        
        var log = new Log
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = TestTenantId,
            CreatedAt = DateTime.UtcNow,
            Level = logLevel,
            Message = $"Test log message for {agentName}",
            WorkflowId = workflowId,
            WorkflowRunId = ObjectId.GenerateNewId().ToString(),
            WorkflowType = workflowType,
            Agent = agentName,
            ParticipantId = participantId
        };

        // Insert directly into MongoDB collection for testing
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var mongoDatabase = await databaseService.GetDatabaseAsync();
        var collection = mongoDatabase.GetCollection<Log>("logs");
        await collection.InsertOneAsync(log);
    }
} 