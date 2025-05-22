using System.Net;
using System.Net.Http.Json;
using MongoDB.Bson;
using Microsoft.Extensions.Logging;
using XiansAi.Server.Tests.TestUtils;
using XiansAi.Server.Features.AgentApi.Models;
using Features.AgentApi.Services.Lib;

namespace XiansAi.Server.Tests.IntegrationTests.AgentApi;

public class LogsEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    //dotnet test --filter "FullyQualifiedName~LogsEndpointTests"
    public LogsEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task CreateSingleLog_ReturnsCreatedLog()
    {
        // Arrange
        var logRequest = new LogRequest
        {
            Message = "Test log message",
            Level = LogLevel.Information,
            WorkflowId = ObjectId.GenerateNewId().ToString(),
            WorkflowRunId = ObjectId.GenerateNewId().ToString(),
            WorkflowType = "TestWorkflow",
            Agent = "TestAgent"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/logs/single", logRequest);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await response.Content.ReadFromJsonAsync<Log>();
        Assert.NotNull(result);
        Assert.Equal(logRequest.Message, result.Message);
        Assert.Equal(logRequest.Level, result.Level);
        Assert.Equal(logRequest.WorkflowId, result.WorkflowId);
        Assert.Equal(logRequest.WorkflowRunId, result.WorkflowRunId);
    }

    [Fact]
    public async Task CreateMultipleLogs_ReturnsCreatedLogs()
    {
        // Arrange
        var logRequests = new[]
        {
            new LogRequest
            {
                Message = "First log message",
                Level = Microsoft.Extensions.Logging.LogLevel.Information,
                WorkflowId = ObjectId.GenerateNewId().ToString(),
                WorkflowRunId = ObjectId.GenerateNewId().ToString(),
                WorkflowType = "TestWorkflow",
                Agent = "TestAgent"
            },
            new LogRequest
            {
                Message = "Second log message",
                Level = Microsoft.Extensions.Logging.LogLevel.Warning,
                WorkflowId = ObjectId.GenerateNewId().ToString(),
                WorkflowRunId = ObjectId.GenerateNewId().ToString(),
                WorkflowType = "TestWorkflow",
                Agent = "TestAgent"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/logs", logRequests);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var results = await response.Content.ReadFromJsonAsync<Log[]>();
        Assert.NotNull(results);
        Assert.Equal(2, results.Length);
        
        Assert.Equal(logRequests[0].Message, results[0].Message);
        Assert.Equal(logRequests[0].Level, results[0].Level);
        Assert.Equal(logRequests[0].WorkflowId, results[0].WorkflowId);
        Assert.Equal(logRequests[0].WorkflowRunId, results[0].WorkflowRunId);
        
        Assert.Equal(logRequests[1].Message, results[1].Message);
        Assert.Equal(logRequests[1].Level, results[1].Level);
        Assert.Equal(logRequests[1].WorkflowId, results[1].WorkflowId);
        Assert.Equal(logRequests[1].WorkflowRunId, results[1].WorkflowRunId);
    }

    [Fact]
    public async Task CreateLog_WithInvalidWorkflowId_ReturnsBadRequest()
    {
        // Arrange
        var logRequest = new LogRequest
        {
            Message = "Test log message",
            Level = Microsoft.Extensions.Logging.LogLevel.Information,
            WorkflowId = null!, // This should trigger validation
            WorkflowRunId = ObjectId.GenerateNewId().ToString(),
            WorkflowType = "TestWorkflow",
            Agent = "TestAgent"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/logs/single", logRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
} 