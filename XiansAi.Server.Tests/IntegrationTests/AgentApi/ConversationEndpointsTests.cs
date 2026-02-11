using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Tests.TestUtils;
using Shared.Repositories;
using Shared.Data;

namespace Tests.IntegrationTests.AgentApi;

public class ConversationEndpointsTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    /*
    dotnet test --filter "FullyQualifiedName~ConversationEndpointsTests"
    */
    private const string TestUserId = "test-user-id";

    public ConversationEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithTaskIdInHistory_ReturnsLastTaskId"
    */
    [Fact]
    public async Task GetLastTaskId_WithTaskIdInHistory_ReturnsLastTaskId()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages with task ids at different times
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "old-task-id",
            createdAt: DateTime.UtcNow.AddHours(-2));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "middle-task-id",
            createdAt: DateTime.UtcNow.AddHours(-1));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "latest-task-id",
            createdAt: DateTime.UtcNow);

        // Act
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var taskId = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"latest-task-id\"", taskId); // JSON string is quoted
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithWorkflowId_ReturnsLastTaskId"
    */
    [Fact]
    public async Task GetLastTaskId_WithWorkflowId_ReturnsLastTaskId()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "task-id-from-workflow",
            createdAt: DateTime.UtcNow);

        // Act - Use workflowId instead of workflowType
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowId={workflowId}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var taskId = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"task-id-from-workflow\"", taskId);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithScope_ReturnsLastTaskIdForScope"
    */
    [Fact]
    public async Task GetLastTaskId_WithScope_ReturnsLastTaskIdForScope()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages with task ids in different scopes
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "billing-task-id",
            scope: "billing",
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "support-task-id",
            scope: "support",
            createdAt: DateTime.UtcNow);

        // Act - Filter by billing scope
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}&participantId={participantId}&scope=billing");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var taskId = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"billing-task-id\"", taskId);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithNullScope_ReturnsLastTaskIdForNullScope"
    */
    [Fact]
    public async Task GetLastTaskId_WithNullScope_ReturnsLastTaskIdForNullScope()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages with task ids in different scopes
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "scoped-task-id",
            scope: "billing",
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "default-task-id",
            scope: null,
            createdAt: DateTime.UtcNow);

        // Act - Filter by null scope (empty string)
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}&participantId={participantId}&scope=");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var taskId = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"default-task-id\"", taskId);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithMessagesWithoutTaskId_IgnoresMessagesWithoutTaskId"
    */
    [Fact]
    public async Task GetLastTaskId_WithMessagesWithoutTaskId_IgnoresMessagesWithoutTaskId()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages: some with task ids, some without
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "first-task-id",
            createdAt: DateTime.UtcNow.AddHours(-2));

        // Message without task id (more recent)
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: null,
            createdAt: DateTime.UtcNow.AddHours(-1));

        // Empty task id (should be ignored)
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "",
            createdAt: DateTime.UtcNow.AddMinutes(-30));

        // Latest message with valid task id
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            taskId: "latest-valid-task-id",
            createdAt: DateTime.UtcNow);

        // Act
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var taskId = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"latest-valid-task-id\"", taskId);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithNoTaskIdsInHistory_ReturnsNull"
    */
    [Fact]
    public async Task GetLastTaskId_WithNoTaskIdsInHistory_ReturnsNull()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Act
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(content == "null" || content == "", "Expected JSON null or empty string when no task id found");
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithMissingWorkflowTypeAndId_ReturnsBadRequest"
    */
    [Fact]
    public async Task GetLastTaskId_WithMissingWorkflowTypeAndId_ReturnsBadRequest()
    {
        // Arrange
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Act - Missing both workflowType and workflowId
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("WorkflowType or WorkflowId is required", content);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithMissingParticipantId_ReturnsBadRequest"
    */
    [Fact]
    public async Task GetLastTaskId_WithMissingParticipantId_ReturnsBadRequest()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";

        // Act - Missing participantId
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        if (!string.IsNullOrEmpty(content))
        {
            Assert.Contains("ParticipantId is required", content);
        }
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastTaskId_WithDifferentParticipant_ReturnsCorrectTaskId"
    */
    [Fact]
    public async Task GetLastTaskId_WithDifferentParticipant_ReturnsCorrectTaskId()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participant1 = $"test-participant-1-{Guid.NewGuid()}";
        var participant2 = $"test-participant-2-{Guid.NewGuid()}";

        // Create messages for different participants
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participant1,
            taskId: "task-id-participant-1",
            createdAt: DateTime.UtcNow);

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participant2,
            taskId: "task-id-participant-2",
            createdAt: DateTime.UtcNow);

        // Act - Get task id for participant 1
        var response1 = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}&participantId={participant1}");

        // Assert participant 1
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var taskId1 = await response1.Content.ReadAsStringAsync();
        Assert.Equal("\"task-id-participant-1\"", taskId1);

        // Act - Get task id for participant 2
        var response2 = await _client.GetAsync(
            $"/api/agent/conversation/last-task-id?workflowType={workflowType}&participantId={participant2}");

        // Assert participant 2
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var taskId2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal("\"task-id-participant-2\"", taskId2);
    }

    // Helper methods
    private async Task<ConversationMessage> CreateTestMessageAsync(
        string workflowId,
        string workflowType,
        string participantId,
        string? hint = null,
        string? taskId = null,
        string? scope = null,
        DateTime? createdAt = null,
        string content = "Test message")
    {
        using var scope2 = _factory.Services.CreateScope();
        var databaseService = scope2.ServiceProvider.GetRequiredService<IDatabaseService>();

        var message = new ConversationMessage
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ThreadId = ObjectId.GenerateNewId().ToString(),
            TenantId = TestTenantId,
            ParticipantId = participantId,
            WorkflowId = workflowId,
            WorkflowType = workflowType,
            CreatedAt = createdAt ?? DateTime.UtcNow,
            UpdatedAt = createdAt ?? DateTime.UtcNow,
            CreatedBy = TestUserId,
            Direction = MessageDirection.Incoming,
            Text = content,
            Status = MessageStatus.DeliveredToWorkflow,
            Hint = hint,
            TaskId = taskId,
            Scope = scope,
            MessageType = MessageType.Chat
        };

        // Insert directly into the database
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationMessage>("conversation_message");
        await collection.InsertOneAsync(message);

        return message;
    }
}

