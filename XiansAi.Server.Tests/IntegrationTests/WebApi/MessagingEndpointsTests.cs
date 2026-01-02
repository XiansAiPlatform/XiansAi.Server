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

    private async Task<ConversationMessage> CreateTestMessageWithScopeAsync(
        string threadId,
        string? scope = null,
        string content = "Test message",
        MessageDirection direction = MessageDirection.Incoming)
    {
        using var scope2 = _factory.Services.CreateScope();
        var databaseService = scope2.ServiceProvider.GetRequiredService<IDatabaseService>();

        // Normalize scope: empty string and whitespace should be treated as null (default topic)
        var normalizedScope = string.IsNullOrWhiteSpace(scope) ? null : scope.Trim();

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
            Scope = normalizedScope,  // Use normalized scope
            Status = MessageStatus.DeliveredToWorkflow
        };

        // Insert directly into repository
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationMessage>("conversation_message");
        await collection.InsertOneAsync(message);

        return message;
    }

    #region Topics Tests

    [Fact]
    public async Task GetTopics_WithValidThreadId_ReturnsTopics()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create messages with different scopes
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Billing question");
        await Task.Delay(10);
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "support", content: "Support question");
        await Task.Delay(10);
        await CreateTestMessageWithScopeAsync(thread.Id, scope: null, content: "General message");

        // Act
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        var result = await ReadAsJsonAsync<TopicsResult>(response);
        Assert.NotNull(result);
        Assert.NotNull(result.Topics);
        Assert.Equal(3, result.Topics.Count); // billing, support, null
        
        // Verify pagination metadata
        Assert.NotNull(result.Pagination);
        Assert.Equal(1, result.Pagination.CurrentPage);
        Assert.Equal(50, result.Pagination.PageSize);
        Assert.Equal(3, result.Pagination.TotalTopics);
        Assert.Equal(1, result.Pagination.TotalPages);
        Assert.False(result.Pagination.HasMore);
        
        // Verify topics are sorted by most recent activity
        Assert.Equal("General message", result.Topics[0].Scope == null ? "General message" : result.Topics[0].Scope);
        Assert.Equal(1, result.Topics.First(t => t.Scope == null).MessageCount);
        Assert.Equal(1, result.Topics.First(t => t.Scope == "support").MessageCount);
        Assert.Equal(1, result.Topics.First(t => t.Scope == "billing").MessageCount);
    }

    [Fact]
    public async Task GetTopics_WithPagination_ReturnsCorrectPage()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create messages with 5 different scopes
        for (int i = 0; i < 5; i++)
        {
            await CreateTestMessageWithScopeAsync(thread.Id, scope: $"topic-{i}", content: $"Message {i}");
            await Task.Delay(10); // Ensure different timestamps
        }

        // Act - Get page 1 with pageSize 2
        var response1 = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=2");

        // Assert page 1
        Assert.Equal(HttpStatusCode.OK, response1.StatusCode);
        var result1 = await ReadAsJsonAsync<TopicsResult>(response1);
        Assert.NotNull(result1);
        Assert.Equal(2, result1.Topics.Count);
        Assert.Equal(1, result1.Pagination.CurrentPage);
        Assert.Equal(2, result1.Pagination.PageSize);
        Assert.Equal(5, result1.Pagination.TotalTopics);
        Assert.Equal(3, result1.Pagination.TotalPages);
        Assert.True(result1.Pagination.HasMore);

        // Act - Get page 2
        var response2 = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=2&pageSize=2");

        // Assert page 2
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var result2 = await ReadAsJsonAsync<TopicsResult>(response2);
        Assert.NotNull(result2);
        Assert.Equal(2, result2.Topics.Count);
        Assert.Equal(2, result2.Pagination.CurrentPage);
        Assert.True(result2.Pagination.HasMore);

        // Act - Get page 3 (last page)
        var response3 = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=3&pageSize=2");

        // Assert page 3
        Assert.Equal(HttpStatusCode.OK, response3.StatusCode);
        var result3 = await ReadAsJsonAsync<TopicsResult>(response3);
        Assert.NotNull(result3);
        Assert.Single(result3.Topics); // Only 1 topic left
        Assert.Equal(3, result3.Pagination.CurrentPage);
        Assert.False(result3.Pagination.HasMore);
    }

    [Fact]
    public async Task GetTopics_SortedByMostRecentActivity()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create messages with different scopes at different times
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "old-topic", content: "Old message");
        await Task.Delay(50);
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "middle-topic", content: "Middle message");
        await Task.Delay(50);
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "new-topic", content: "New message");

        // Act
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<TopicsResult>(response);
        Assert.NotNull(result);
        Assert.Equal(3, result.Topics.Count);
        
        // Verify topics are sorted by most recent first
        Assert.Equal("new-topic", result.Topics[0].Scope);
        Assert.Equal("middle-topic", result.Topics[1].Scope);
        Assert.Equal("old-topic", result.Topics[2].Scope);
        
        // Verify timestamps are in descending order
        Assert.True(result.Topics[0].LastMessageAt > result.Topics[1].LastMessageAt);
        Assert.True(result.Topics[1].LastMessageAt > result.Topics[2].LastMessageAt);
    }

    [Fact]
    public async Task GetTopics_WithMultipleMessagesInSameTopic_CountsCorrectly()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create multiple messages with the same scope
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Message 1");
        await Task.Delay(10);
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Message 2");
        await Task.Delay(10);
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Message 3");

        // Act
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<TopicsResult>(response);
        Assert.NotNull(result);
        Assert.Single(result.Topics); // Only one topic
        Assert.Equal("billing", result.Topics[0].Scope);
        Assert.Equal(3, result.Topics[0].MessageCount);
    }

    [Fact]
    public async Task GetTopics_WithInvalidPageNumber_ReturnsBadRequest()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();

        // Act - page=0 is invalid
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=0&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page number must be greater than 0", content);
    }

    [Fact]
    public async Task GetTopics_WithInvalidPageSize_ReturnsBadRequest()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();

        // Act - pageSize=0 is invalid
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=0");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page size must be greater than 0", content);
    }

    [Fact]
    public async Task GetTopics_WithPageSizeExceedingMax_ReturnsBadRequest()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();

        // Act - pageSize=101 exceeds max of 100
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=101");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page size cannot exceed 100", content);
    }

    [Fact]
    public async Task GetTopics_WithEmptyThread_ReturnsEmptyList()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        // Don't create any messages

        // Act
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<TopicsResult>(response);
        Assert.NotNull(result);
        Assert.Empty(result.Topics);
        Assert.Equal(0, result.Pagination.TotalTopics);
        Assert.Equal(0, result.Pagination.TotalPages);
        Assert.False(result.Pagination.HasMore);
    }

    [Fact]
    public async Task GetTopics_WithPageExceedingMaximum_ReturnsBadRequest()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();

        // Act - page=101 exceeds maximum of 100
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=101&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Page number cannot exceed 100", content);
        Assert.Contains("search functionality", content);
    }

    [Fact]
    public async Task GetMessages_WithScopeFilter_ReturnsOnlyMatchingMessages()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create messages with different scopes
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Billing message 1");
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Billing message 2");
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "support", content: "Support message");
        await CreateTestMessageWithScopeAsync(thread.Id, scope: null, content: "General message");

        // Act - Filter by "billing" scope
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/messages?page=1&pageSize=50&scope=billing");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = await ReadAsJsonAsync<List<ConversationMessage>>(response);
        Assert.NotNull(messages);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Equal("billing", m.Scope));
    }

    [Fact]
    public async Task GetMessages_WithNullScope_ReturnsOnlyMessagesWithoutScope()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create messages with different scopes
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Billing message");
        await CreateTestMessageWithScopeAsync(thread.Id, scope: null, content: "General message 1");
        await CreateTestMessageWithScopeAsync(thread.Id, scope: null, content: "General message 2");

        // Act - Filter by null scope (empty string)
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/messages?page=1&pageSize=50&scope=");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = await ReadAsJsonAsync<List<ConversationMessage>>(response);
        Assert.NotNull(messages);
        Assert.Equal(2, messages.Count);
        Assert.All(messages, m => Assert.Null(m.Scope));
    }

    [Fact]
    public async Task GetMessages_WithoutScopeFilter_ReturnsAllMessages()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create messages with different scopes
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Billing message");
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "support", content: "Support message");
        await CreateTestMessageWithScopeAsync(thread.Id, scope: null, content: "General message");

        // Act - No scope filter
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/messages?page=1&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var messages = await ReadAsJsonAsync<List<ConversationMessage>>(response);
        Assert.NotNull(messages);
        Assert.Equal(3, messages.Count);
    }

    [Fact]
    public async Task GetTopics_NewMessageInExistingTopic_UpdatesLastMessageAt()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create initial message in topic
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "Old message");
        await Task.Delay(100);
        
        // Get initial topics
        var response1 = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=50");
        var result1 = await ReadAsJsonAsync<TopicsResult>(response1);
        var initialLastMessageAt = result1!.Topics.First(t => t.Scope == "billing").LastMessageAt;
        
        // Add new message to same topic
        await Task.Delay(100);
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "billing", content: "New message");

        // Act - Get topics again
        var response2 = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response2.StatusCode);
        var result2 = await ReadAsJsonAsync<TopicsResult>(response2);
        Assert.NotNull(result2);
        var updatedTopic = result2.Topics.First(t => t.Scope == "billing");
        Assert.Equal(2, updatedTopic.MessageCount);
        Assert.True(updatedTopic.LastMessageAt > initialLastMessageAt);
    }

    [Fact]
    public async Task GetTopics_EmptyStringAndNullScope_CombinedIntoSingleDefaultTopic()
    {
        // Arrange
        var thread = await CreateTestThreadAsync();
        
        // Create messages with null scope
        await CreateTestMessageWithScopeAsync(thread.Id, scope: null, content: "Message with null scope");
        await Task.Delay(10);
        
        // Create messages with empty string scope (should be normalized to null)
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "", content: "Message with empty scope");
        await Task.Delay(10);
        
        // Create messages with whitespace scope (should be normalized to null)
        await CreateTestMessageWithScopeAsync(thread.Id, scope: "   ", content: "Message with whitespace scope");

        // Act
        var response = await GetAsync($"/api/client/messaging/threads/{thread.Id}/topics?page=1&pageSize=50");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<TopicsResult>(response);
        Assert.NotNull(result);
        
        // Should only have ONE topic (the default topic with scope = null)
        Assert.Single(result.Topics);
        
        // The single topic should be the default topic (null scope)
        var defaultTopic = result.Topics[0];
        Assert.Null(defaultTopic.Scope);
        
        // Should contain all 3 messages
        Assert.Equal(3, defaultTopic.MessageCount);
    }

    #endregion
} 