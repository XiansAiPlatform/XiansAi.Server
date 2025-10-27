using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;
using Tests.TestUtils;
using Shared.Repositories;
using Shared.Services;
using Shared.Data;

namespace Tests.IntegrationTests.WebApi;

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
            // Small delay to ensure different UpdatedAt timestamps for proper sorting
            await Task.Delay(10);
        }

        // Act - Get page 1
        var response1 = await GetAsync($"/api/client/messaging/threads?agent={agent}&page=1&pageSize=2");

        // Assert page 1
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var page1Threads = await ReadAsJsonAsync<List<ConversationThread>>(response1);
        Assert.NotNull(page1Threads);
        Assert.Equal(2, page1Threads.Count);

        // Act - Get page 2
        var response2 = await GetAsync($"/api/client/messaging/threads?agent={agent}&page=2&pageSize=2");

        // Assert page 2
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var page2Threads = await ReadAsJsonAsync<List<ConversationThread>>(response2);
        Assert.NotNull(page2Threads);
        Assert.Equal(2, page2Threads.Count);

        // Verify that page 1 and page 2 have different threads (no overlap)
        var page1Ids = page1Threads.Select(t => t.Id).ToList();
        var page2Ids = page2Threads.Select(t => t.Id).ToList();
        Assert.Empty(page1Ids.Intersect(page2Ids));

        // Act - Get page 3
        var response3 = await GetAsync($"/api/client/messaging/threads?agent={agent}&page=3&pageSize=2");

        // Assert page 3 (should have 1 thread since we only created 5)
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var page3Threads = await ReadAsJsonAsync<List<ConversationThread>>(response3);
        Assert.NotNull(page3Threads);
        Assert.Single(page3Threads);
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
    public async Task GetThreads_WithInvalidPageNumber_ReturnsBadRequest()
    {
        // Arrange
        var agent = $"test-agent-{Guid.NewGuid()}";

        // Act - page=0 is invalid (1-based pagination)
        var response = await GetAsync($"/api/client/messaging/threads?agent={agent}&page=0&pageSize=20");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page number must be greater than 0", content);
        Assert.Contains("1-based", content);
    }

    [Fact]
    public async Task GetThreads_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        var agent = $"test-agent-{Guid.NewGuid()}";

        // Act - pageSize=0 is invalid
        var response = await GetAsync($"/api/client/messaging/threads?agent={agent}&page=1&pageSize=0");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page size must be greater than 0", content);
    }

    [Fact]
    public async Task GetThreads_WithNegativePageNumber_ReturnsBadRequest()
    {
        // Arrange
        var agent = $"test-agent-{Guid.NewGuid()}";

        // Act - page=-1 is invalid
        var response = await GetAsync($"/api/client/messaging/threads?agent={agent}&page=-1&pageSize=20");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page number must be greater than 0", content);
    }

    [Fact]
    public async Task GetMessages_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        for (int i = 0; i < 5; i++)
        {
            await CreateTestMessageAsync(thread.Id, content: $"Message {i + 1}");
            // Small delay to ensure different CreatedAt timestamps for proper sorting
            await Task.Delay(10);
        }

        // Act - Get page 1
        var response1 = await GetAsync($"/api/client/messaging/threads/{thread.Id}/messages?page=1&pageSize=2");

        // Assert page 1
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var page1Messages = await ReadAsJsonAsync<List<ConversationMessage>>(response1);
        Assert.NotNull(page1Messages);
        Assert.Equal(2, page1Messages.Count);

        // Act - Get page 2
        var response2 = await GetAsync($"/api/client/messaging/threads/{thread.Id}/messages?page=2&pageSize=2");

        // Assert page 2
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var page2Messages = await ReadAsJsonAsync<List<ConversationMessage>>(response2);
        Assert.NotNull(page2Messages);
        Assert.Equal(2, page2Messages.Count);

        // Verify that page 1 and page 2 have different messages (no overlap)
        var page1Ids = page1Messages.Select(m => m.Id).ToList();
        var page2Ids = page2Messages.Select(m => m.Id).ToList();
        Assert.Empty(page1Ids.Intersect(page2Ids));
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
    public async Task GetMessages_WithInvalidPageNumber_ReturnsBadRequest()
    {
        // Arrange
        var validThreadId = ObjectId.GenerateNewId().ToString();

        // Act - page=0 is invalid
        var response = await GetAsync($"/api/client/messaging/threads/{validThreadId}/messages?page=0&pageSize=20");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page number must be greater than 0", content);
    }

    [Fact]
    public async Task GetMessages_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        var validThreadId = ObjectId.GenerateNewId().ToString();

        // Act - pageSize=-5 is invalid
        var response = await GetAsync($"/api/client/messaging/threads/{validThreadId}/messages?page=1&pageSize=-5");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page size must be greater than 0", content);
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
    public async Task DeleteThread_WithNonExistentThreadId_ReturnsNotFound()
    {
        // Arrange
        var nonExistentThreadId = "507f1f77bcf86cd799439011"; // Valid ObjectId format that doesn't exist in database

        // Act
        var response = await DeleteAsync($"/api/client/messaging/threads/{nonExistentThreadId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode); // API returns NotFound when thread not found
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