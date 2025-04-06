using System.Net.Http.Json;
using XiansAi.Server.Tests.TestUtils;
using Features.AgentApi.Services.Lib;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using XiansAi.Server.Utils;
using MongoDB.Driver;
using Shared.Data.Models;

namespace XiansAi.Server.Tests.IntegrationTests.AgentApi;


public class ActivityHistoryTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    
    /*
    dotnet test --filter "FullyQualifiedName~ActivityHistoryTests"
    */
    public ActivityHistoryTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ActivityHistoryTests.CreateActivityHistory_WithValidData_ReturnsOk"
    */
    [Fact]
    public async Task CreateActivityHistory_WithValidData_ReturnsOk()
    {
        // Arrange
        var request = new ActivityHistoryRequest
        {
            ActivityId = "test-activity-id",
            ActivityName = "Test Activity",
            WorkflowId = "test-workflow-id",
            WorkflowRunId = "test-workflow-run-id",
            WorkflowType = "test-workflow-type",
            TaskQueue = "test-task-queue",
            WorkflowNamespace = "test-namespace",
            StartedTime = DateTime.UtcNow,
            EndedTime = DateTime.UtcNow.AddMinutes(5),
            Attempt = 1,
            AgentToolNames = new List<string> { "tool1", "tool2" },
            InstructionIds = new List<string> { "instruction1" },
            Inputs = new Dictionary<string, JsonElement?>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/activity-history", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("Activity history creation queued", responseContent);
        
        // Wait for background tasks to complete before test ends
        // This prevents MongoDB connection errors when the test fixture is disposed
        var backgroundTaskService = _factory.Services.GetRequiredService<IBackgroundTaskService>();
        
        // Wait for background tasks to complete with a reasonable timeout
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
    }
    
    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ActivityHistoryTests.CreateActivityHistory_WithInvalidData_ReturnsBadRequest"
    */
    [Fact]
    public async Task CreateActivityHistory_WithInvalidData_ReturnsBadRequest()
    {
        // Arrange - using anonymous object with incomplete data that will fail model binding
        // because it's missing required fields: ActivityId, WorkflowType, TaskQueue
        var invalidRequest = new
        {
            ActivityName = "Test Activity",
            WorkflowId = "test-workflow-id",
            WorkflowRunId = "test-workflow-run-id",
            WorkflowNamespace = "test-namespace"
            // Missing required fields: ActivityId, WorkflowType, TaskQueue
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/activity-history", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var responseContent = await response.Content.ReadAsStringAsync();
        Assert.Contains("JSON deserialization", responseContent);
    }
    
    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ActivityHistoryTests.CreateActivityHistory_VerifyDataInsertedIntoMongoDB"
    */
    [Fact]
    public async Task CreateActivityHistory_VerifyDataInsertedIntoMongoDB()
    {
        // Arrange
        string uniqueActivityId = $"test-activity-id-{Guid.NewGuid()}";
        var request = new ActivityHistoryRequest
        {
            ActivityId = uniqueActivityId,
            ActivityName = "Test Activity",
            WorkflowId = "test-workflow-id",
            WorkflowRunId = "test-workflow-run-id",
            WorkflowType = "test-workflow-type",
            TaskQueue = "test-task-queue",
            WorkflowNamespace = "test-namespace",
            StartedTime = DateTime.UtcNow,
            EndedTime = DateTime.UtcNow.AddMinutes(5),
            Attempt = 1,
            AgentToolNames = new List<string> { "tool1", "tool2" },
            InstructionIds = new List<string> { "instruction1" },
            Inputs = new Dictionary<string, JsonElement?>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/activity-history", request);
        
        // Assert HTTP response
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Wait for background tasks to complete
        var backgroundTaskService = _factory.Services.GetRequiredService<IBackgroundTaskService>();
        await backgroundTaskService.WaitForCompletionAsync(TimeSpan.FromSeconds(5));
        
        // Get MongoDB collection and verify data was inserted
        var collection = _database.GetCollection<ActivityHistory>("activity_history");
        var filter = Builders<ActivityHistory>.Filter.Eq("activity_id", uniqueActivityId);
        
        // Allow a few retries as there might be a slight delay in the background task processing
        ActivityHistory? result = null;
        for (int i = 0; i < 3; i++)
        {
            result = await collection.Find(filter).FirstOrDefaultAsync();
            if (result != null) break;
            await Task.Delay(500); // Short delay between retries
        }
        
        // Assert data was inserted correctly
        Assert.NotNull(result);
        Assert.Equal(uniqueActivityId, result.ActivityId);
        Assert.Equal("Test Activity", result.ActivityName);
        Assert.Equal("test-workflow-id", result.WorkflowId);
        Assert.Equal("test-workflow-type", result.WorkflowType);
        Assert.Equal("test-task-queue", result.TaskQueue);
        Assert.Equal("test-namespace", result.WorkflowNamespace);
        Assert.Equal(1, result.Attempt);
        Assert.Contains("tool1", result.AgentToolNames!);
        Assert.Contains("tool2", result.AgentToolNames!);
        Assert.Contains("instruction1", result.InstructionIds!);
    }
} 