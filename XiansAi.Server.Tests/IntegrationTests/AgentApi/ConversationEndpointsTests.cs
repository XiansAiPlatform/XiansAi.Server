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
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithHintInHistory_ReturnsLastHint"
    */
    [Fact]
    public async Task GetLastHint_WithHintInHistory_ReturnsLastHint()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages with hints at different times
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Old hint",
            createdAt: DateTime.UtcNow.AddHours(-2));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Middle hint",
            createdAt: DateTime.UtcNow.AddHours(-1));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Latest hint",
            createdAt: DateTime.UtcNow);

        // Act
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hint = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"Latest hint\"", hint); // JSON string is quoted
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithWorkflowId_ReturnsLastHint"
    */
    [Fact]
    public async Task GetLastHint_WithWorkflowId_ReturnsLastHint()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Test hint from workflowId",
            createdAt: DateTime.UtcNow);

        // Act - Use workflowId instead of workflowType
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowId={workflowId}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hint = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"Test hint from workflowId\"", hint);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithScope_ReturnsLastHintForScope"
    */
    [Fact]
    public async Task GetLastHint_WithScope_ReturnsLastHintForScope()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages with hints in different scopes
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Billing hint",
            scope: "billing",
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Support hint",
            scope: "support",
            createdAt: DateTime.UtcNow);

        // Act - Filter by billing scope
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}&participantId={participantId}&scope=billing");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hint = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"Billing hint\"", hint);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithNullScope_ReturnsLastHintForNullScope"
    */
    [Fact]
    public async Task GetLastHint_WithNullScope_ReturnsLastHintForNullScope()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages with hints in different scopes
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Scoped hint",
            scope: "billing",
            createdAt: DateTime.UtcNow.AddMinutes(-5));

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Default hint",
            scope: null,
            createdAt: DateTime.UtcNow);

        // Act - Filter by null scope (empty string)
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}&participantId={participantId}&scope=");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hint = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"Default hint\"", hint);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithMessagesWithoutHint_IgnoresMessagesWithoutHint"
    */
    [Fact]
    public async Task GetLastHint_WithMessagesWithoutHint_IgnoresMessagesWithoutHint()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var workflowId = $"{TestTenantId}:{workflowType}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Create messages: some with hints, some without
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "First hint",
            createdAt: DateTime.UtcNow.AddHours(-2));

        // Message without hint (more recent)
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: null,
            createdAt: DateTime.UtcNow.AddHours(-1));

        // Empty hint (should be ignored)
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "",
            createdAt: DateTime.UtcNow.AddMinutes(-30));

        // Latest message with valid hint
        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participantId,
            hint: "Latest valid hint",
            createdAt: DateTime.UtcNow);

        // Act
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hint = await response.Content.ReadAsStringAsync();
        Assert.Equal("\"Latest valid hint\"", hint);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithNoHintsInHistory_ReturnsNull"
    */
    [Fact]
    public async Task GetLastHint_WithNoHintsInHistory_ReturnsNull()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Act
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}&participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Equal("null", content); // JSON null
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithMissingWorkflowTypeAndId_ReturnsBadRequest"
    */
    [Fact]
    public async Task GetLastHint_WithMissingWorkflowTypeAndId_ReturnsBadRequest()
    {
        // Arrange
        var participantId = $"test-participant-{Guid.NewGuid()}";

        // Act - Missing both workflowType and workflowId
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?participantId={participantId}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("WorkflowType or WorkflowId is required", content);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithMissingParticipantId_ReturnsBadRequest"
    */
    [Fact]
    public async Task GetLastHint_WithMissingParticipantId_ReturnsBadRequest()
    {
        // Arrange
        var workflowType = $"test-workflow-{Guid.NewGuid()}";

        // Act - Missing participantId
        var response = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("ParticipantId is required", content);
    }

    /*
    dotnet test --filter "FullyQualifiedName=Tests.IntegrationTests.AgentApi.ConversationEndpointsTests.GetLastHint_WithDifferentParticipant_ReturnsCorrectHint"
    */
    [Fact]
    public async Task GetLastHint_WithDifferentParticipant_ReturnsCorrectHint()
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
            hint: "Hint for participant 1",
            createdAt: DateTime.UtcNow);

        await CreateTestMessageAsync(
            workflowId: workflowId,
            workflowType: workflowType,
            participantId: participant2,
            hint: "Hint for participant 2",
            createdAt: DateTime.UtcNow);

        // Act - Get hint for participant 1
        var response1 = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}&participantId={participant1}");

        // Assert participant 1
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var hint1 = await response1.Content.ReadAsStringAsync();
        Assert.Equal("\"Hint for participant 1\"", hint1);

        // Act - Get hint for participant 2
        var response2 = await _client.GetAsync(
            $"/api/agent/conversation/last-hint?workflowType={workflowType}&participantId={participant2}");

        // Assert participant 2
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var hint2 = await response2.Content.ReadAsStringAsync();
        Assert.Equal("\"Hint for participant 2\"", hint2);
    }

    // Helper methods
    private async Task<ConversationMessage> CreateTestMessageAsync(
        string workflowId,
        string workflowType,
        string participantId,
        string? hint = null,
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

