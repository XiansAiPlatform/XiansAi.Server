using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using XiansAi.Server.Tests.TestUtils;
using Shared.Repositories;
using Shared.Services;
using Shared.Data;

namespace XiansAi.Server.Tests.IntegrationTests.WebApi;

public class MessagingEndpointsTests : WebApiIntegrationTestBase, IClassFixture<MongoDbFixture>
{
    private const string TestUserId = "test-user-id";

    public MessagingEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task GetThreads_WithValidAgent_ReturnsThreads()
    {
        // Arrange
        var agent = $"test-agent-{Guid.NewGuid()}";
        await CreateTestThreadAsync(agent: agent);
        await CreateTestThreadAsync(agent: agent);

        // Act
        var response = await GetAsync($"/api/client/messaging/threads?agent={agent}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var threads = await ReadAsJsonAsync<List<ConversationThread>>(response);
        Assert.NotNull(threads);
        Assert.True(threads.Count >= 2);
        Assert.All(threads, t => Assert.Equal(agent, t.Agent));
    }

    [Fact]
    public async Task GetThreads_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var agent = $"test-agent-{Guid.NewGuid()}";
        for (int i = 0; i < 5; i++)
        {
            await CreateTestThreadAsync(agent: agent);
        }

        // Act
        var response = await GetAsync($"/api/client/messaging/threads?agent={agent}&page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var threads = await ReadAsJsonAsync<List<ConversationThread>>(response);
        Assert.NotNull(threads);
        Assert.True(threads.Count <= 2);
    }

    [Fact]
    public async Task GetThreads_WithMissingAgent_ReturnsBadRequest()
    {
        // Act - Missing required agent parameter
        var response = await GetAsync("/api/client/messaging/threads");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetMessages_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        for (int i = 0; i < 5; i++)
        {
            await CreateTestMessageAsync(thread.Id);
        }

        // Act
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/messages?page=1&pageSize=2");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var messages = await ReadAsJsonAsync<List<ConversationMessage>>(response);
        Assert.NotNull(messages);
        Assert.True(messages.Count <= 2);
    }

    [Fact]
    public async Task GetMessages_WithValidThreadId_ReturnsOK()
    {
        // Arrange - Use a valid ObjectId format
        var validThreadId = ObjectId.GenerateNewId().ToString();

        // Act
        var response = await GetAsync($"/api/client/messaging/threads/{validThreadId}/messages");

        // Assert - Should return OK even if no messages exist
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DeleteThread_WithValidThreadId_ReturnsOK()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();

        // Act
        var response = await DeleteAsync($"/api/client/messaging/threads/{thread.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verify thread was deleted
        var deletedThread = await GetThreadByIdAsync(thread.Id);
        Assert.Null(deletedThread);
    }

    [Fact]
    public async Task DeleteThread_WithNonExistentThreadId_ReturnsBadRequest()
    {
        // Arrange
        var nonExistentThreadId = "507f1f77bcf86cd799439011"; // Valid ObjectId format that doesn't exist in database

        // Act
        var response = await DeleteAsync($"/api/client/messaging/threads/{nonExistentThreadId}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode); // API returns BadRequest when thread not found
    }

    [Fact]
    public async Task DeleteThread_WithInvalidThreadId_ReturnsInternalServerError()
    {
        // Arrange
        var invalidThreadId = "invalid-thread-id"; // Invalid ObjectId format

        // Act
        var response = await DeleteAsync($"/api/client/messaging/threads/{invalidThreadId}");

        // Assert
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); // Exception during ObjectId parsing
    }

    // Helper methods
    private async Task<ConversationThread> CreateTestThreadAsync(
        string? workflowId = null,
        string? workflowType = null,
        string? agent = null,
        string? participantId = null)
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var thread = new ConversationThread
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = TestTenantId,
            WorkflowId = workflowId ?? $"test-workflow-{Guid.NewGuid()}",
            WorkflowType = workflowType ?? "TestWorkflowType",
            Agent = agent ?? $"test-agent-{Guid.NewGuid()}",
            ParticipantId = participantId ?? $"test-participant-{Guid.NewGuid()}",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = TestUserId,
            Status = ConversationThreadStatus.Active
        };

        // Insert directly into repository
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationThread>("conversation_thread");
        await collection.InsertOneAsync(thread);

        return thread;
    }

    private async Task<ConversationMessage> CreateTestMessageAsync(
        string threadId,
        string content = "Test message",
        MessageDirection direction = MessageDirection.Incoming)
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var message = new ConversationMessage
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ThreadId = threadId,
            TenantId = TestTenantId,
            ParticipantId = $"test-participant-{Guid.NewGuid()}",
            WorkflowId = $"test-workflow-{Guid.NewGuid()}",
            WorkflowType = "TestWorkflowType",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = TestUserId,
            Direction = direction,
            Text = content,
            Status = MessageStatus.DeliveredToWorkflow,
            Data = new Dictionary<string, object>
            {
                ["testProperty"] = "testValue"
            }
        };

        // Insert directly into repository
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationMessage>("conversation_message");
        await collection.InsertOneAsync(message);

        return message;
    }

    private async Task<ConversationThread?> GetThreadByIdAsync(string threadId)
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationThread>("conversation_thread");
        
        return await collection.Find(t => t.Id == threadId).FirstOrDefaultAsync();
    }
} 