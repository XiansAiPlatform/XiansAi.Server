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

    [Fact]
    public async Task GetById_WithValidId_ReturnsLog()
    {
        // Arrange
        var log = await CreateTestLogAsync();

        // Act
        var response = await GetAsync($"/api/client/logs/{log.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var returnedLog = await ReadAsJsonAsync<Log>(response);
        Assert.NotNull(returnedLog);
        Assert.Equal(log.Id, returnedLog.Id);
        Assert.Equal(log.Message, returnedLog.Message);
        Assert.Equal(log.Level, returnedLog.Level);
        Assert.Equal(log.WorkflowId, returnedLog.WorkflowId);
        Assert.Equal(log.WorkflowRunId, returnedLog.WorkflowRunId);
        Assert.Equal(log.TenantId, returnedLog.TenantId);
    }

    [Fact]
    public async Task GetById_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/client/logs/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

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
    public async Task GetByDateRange_WithValidRange_ReturnsLogs()
    {
        // Arrange - Use a specific date range that won't conflict with other tests
        var baseDate = new DateTime(2023, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var startDate = baseDate.AddDays(-1);
        var endDate = baseDate.AddDays(1);
        
        var log1 = await CreateTestLogAsync(message: "Log within range", createdAt: baseDate);
        
        // Create a log outside the range
        await CreateTestLogAsync(message: "Log outside range", createdAt: baseDate.AddDays(-2));

        // Act
        var response = await GetAsync($"/api/client/logs/date-range?startDate={startDate:yyyy-MM-ddTHH:mm:ss.fffZ}&endDate={endDate:yyyy-MM-ddTHH:mm:ss.fffZ}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Single(logs);
        Assert.Equal("Log within range", logs[0].Message);
    }

    [Fact]
    public async Task GetByDateRange_WithNoLogsInRange_ReturnsEmptyList()
    {
        // Arrange - Use a specific date range that won't conflict with other tests
        var baseDate = new DateTime(2023, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var startDate = baseDate.AddDays(1);
        var endDate = baseDate.AddDays(2);
        
        // Create a log outside the range
        await CreateTestLogAsync(message: "Log outside range", createdAt: baseDate);

        // Act
        var response = await GetAsync($"/api/client/logs/date-range?startDate={startDate:yyyy-MM-ddTHH:mm:ss.fffZ}&endDate={endDate:yyyy-MM-ddTHH:mm:ss.fffZ}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Empty(logs);
    }

    [Fact]
    public async Task DeleteById_WithValidId_DeletesLog()
    {
        // Arrange
        var log = await CreateTestLogAsync();

        // Act
        var response = await DeleteAsync($"/api/client/logs/{log.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        
        // Verify the log is deleted
        var getResponse = await GetAsync($"/api/client/logs/{log.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteById_WithInvalidId_ReturnsNotFound()
    {
        // Arrange
        var invalidId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await DeleteAsync($"/api/client/logs/{invalidId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
    public async Task GetByDateRange_WithInvalidDateFormat_ReturnsBadRequest()
    {
        // Act
        var response = await GetAsync("/api/client/logs/date-range?startDate=invalid-date&endDate=2024-01-01");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetByDateRange_WithStartDateAfterEndDate_ReturnsEmptyList()
    {
        // Arrange - Use a specific date range
        var baseDate = new DateTime(2023, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var startDate = baseDate.AddDays(1);
        var endDate = baseDate.AddDays(-1);

        // Act
        var response = await GetAsync($"/api/client/logs/date-range?startDate={startDate:yyyy-MM-ddTHH:mm:ss.fffZ}&endDate={endDate:yyyy-MM-ddTHH:mm:ss.fffZ}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Empty(logs);
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

    [Fact]
    public async Task GetByDateRange_WithExactDateMatch_ReturnsLog()
    {
        // Arrange - Use a specific date
        var exactDate = new DateTime(2023, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var log = await CreateTestLogAsync(createdAt: exactDate);

        // Act - Use the exact date as both start and end
        var response = await GetAsync($"/api/client/logs/date-range?startDate={exactDate:yyyy-MM-ddTHH:mm:ss.fffZ}&endDate={exactDate:yyyy-MM-ddTHH:mm:ss.fffZ}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var logs = await ReadAsJsonAsync<List<Log>>(response);
        Assert.NotNull(logs);
        Assert.Single(logs);
        Assert.Equal(log.Id, logs[0].Id);
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
        var database = await databaseService.GetDatabase();
        var collection = database.GetCollection<Log>("logs");
        await collection.InsertOneAsync(log);

        return log;
    }
} 