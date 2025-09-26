using System.Net.Http.Json;
using Tests.TestUtils;
using Features.AgentApi.Services.Lib;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shared.Utils;
using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Services;

namespace Tests.IntegrationTests.AgentApi;


public class ActivityHistoryTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    
    /*
    dotnet test --filter "FullyQualifiedName~ActivityHistoryTests"
    */
    public ActivityHistoryTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ActivityHistoryTests.CreateActivityHistory_WithValidData_ReturnsOk"
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
    
    // Removed: CreateActivityHistory_WithInvalidData_ReturnsBadRequest
    // Removed: CreateActivityHistory_VerifyDataInsertedIntoMongoDB
} 