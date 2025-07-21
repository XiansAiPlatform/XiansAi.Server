using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using Xunit;
using XiansAi.Server.Tests.TestUtils;
using Features.WebApi.Models;
using Features.WebApi.Repositories;
using Features.WebApi.Services;
using Microsoft.Extensions.Logging;
using Shared.Data;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public class LogsEndpointsTests : WebApiIntegrationTestBase, IClassFixture<MongoDbFixture>
{
    private const string TestUserId = "test-user-id";

    public LogsEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    // Removed tests for /api/client/logs/{id} as the endpoint does not exist

    [Fact]
    public async Task GetByWorkflowRunId_WithValidRequest_ReturnsLogs()
    {
        // Arrange
        var workflowRunId = ObjectId.GenerateNewId().ToString();
        var log1 = await CreateTestLogAsync(workflowRunId: workflowRunId, message: "First log");
        var log2 = await CreateTestLogAsync(workflowRunId: workflowRunId, message: "Second log");
        
        // Create a log with different workflow run ID to ensure filtering
        await CreateTestLogAsync(workflowRunId: ObjectId.GenerateNewId().ToString(), message: "Different workflow");

        // Act
        var response = await GetAsync($"/api/client/logs/workflow?workflowRunId={workflowRunId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Equal(2, logs.Count);
        Assert.All(logs, log => Assert.Equal(workflowRunId, log.WorkflowRunId));
    }

    [Fact]
    public async Task GetByWorkflowRunId_WithPagination_ReturnsCorrectLogs()
    {
        // Arrange
        var workflowRunId = ObjectId.GenerateNewId().ToString();
        
        // Create 5 logs for the same workflow run
        for (int i = 0; i < 5; i++)
        {
            await CreateTestLogAsync(workflowRunId: workflowRunId, message: $"Log {i}");
        }

        // Act - Get first 3 logs
        var response = await GetAsync($"/api/client/logs/workflow?workflowRunId={workflowRunId}&skip=0&limit=3");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Equal(3, logs.Count);
    }

    [Fact]
    public async Task GetByWorkflowRunId_WithLogLevelFilter_ReturnsFilteredLogs()
    {
        // Arrange
        var workflowRunId = ObjectId.GenerateNewId().ToString();
        await CreateTestLogAsync(workflowRunId: workflowRunId, logLevel: LogLevel.Information);
        await CreateTestLogAsync(workflowRunId: workflowRunId, logLevel: LogLevel.Warning);
        await CreateTestLogAsync(workflowRunId: workflowRunId, logLevel: LogLevel.Error);

        // Act - Filter for Warning logs only (LogLevel.Warning = 3)
        var response = await GetAsync($"/api/client/logs/workflow?workflowRunId={workflowRunId}&logLevel=3");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Single(logs);
        Assert.Equal(LogLevel.Warning, logs[0].Level);
    }

    [Fact]
    public async Task GetByWorkflowRunId_WithNonExistentWorkflowRunId_ReturnsEmptyList()
    {
        // Arrange
        var nonExistentWorkflowRunId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/client/logs/workflow?workflowRunId={nonExistentWorkflowRunId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task GetByWorkflowRunId_WithMissingWorkflowRunId_ReturnsBadRequest()
    {
        // Act
        var response = await GetAsync("/api/client/logs/workflow");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetByWorkflowRunId_WithLargeSkipValue_ReturnsEmptyList()
    {
        // Arrange
        var workflowRunId = ObjectId.GenerateNewId().ToString();
        await CreateTestLogAsync(workflowRunId: workflowRunId);

        // Act - Skip more logs than exist
        var response = await GetAsync($"/api/client/logs/workflow?workflowRunId={workflowRunId}&skip=100&limit=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task GetByWorkflowRunId_WithMultipleLogLevels_ReturnsAllWhenNoFilter()
    {
        // Arrange
        var workflowRunId = ObjectId.GenerateNewId().ToString();
        await CreateTestLogAsync(workflowRunId: workflowRunId, logLevel: LogLevel.Information);
        await CreateTestLogAsync(workflowRunId: workflowRunId, logLevel: LogLevel.Warning);
        await CreateTestLogAsync(workflowRunId: workflowRunId, logLevel: LogLevel.Error);

        // Act - No log level filter
        var response = await GetAsync($"/api/client/logs/workflow?workflowRunId={workflowRunId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Equal(3, logs.Count);
    }

    private async Task<Log> CreateTestLogAsync(
        string? workflowRunId = null,
        string message = "Test log message",
        LogLevel logLevel = LogLevel.Information,
        DateTime? createdAt = null)
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var logRepository = new LogRepository(databaseService);

        var log = new Log
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = TestTenantId,
            Message = message,
            Level = logLevel,
            WorkflowId = ObjectId.GenerateNewId().ToString(),
            WorkflowRunId = workflowRunId ?? ObjectId.GenerateNewId().ToString(),
            WorkflowType = "TestWorkflowType",
            Agent = "TestAgent",
            ParticipantId = "test-participant",
            CreatedAt = createdAt ?? DateTime.UtcNow,
            Properties = new Dictionary<string, object>
            {
                ["testProperty"] = "testValue"
            }
        };

        // Insert directly into repository to bypass service validation
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<Log>("logs");
        await collection.InsertOneAsync(log);

        return log;
    }
} 