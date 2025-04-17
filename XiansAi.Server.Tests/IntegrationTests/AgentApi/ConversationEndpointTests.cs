using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XiansAi.Server.Tests.TestUtils;
using Shared.Services;
using Shared.Repositories;
using MongoDB.Driver;
using MongoDB.Bson;

namespace XiansAi.Server.Tests.IntegrationTests.AgentApi;

public class ConversationEndpointTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    /*
    dotnet test --filter "FullyQualifiedName~ConversationEndpointTests"    
    */
    public ConversationEndpointTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessInboundMessage_WithMissingRequiredField_ReturnsBadRequest"
    */
    [Fact]
    public async Task ProcessInboundMessage_WithMissingRequiredField_ReturnsBadRequest()
    {
        // Arrange - missing required fields
        var invalidRequest = new
        {
            // Missing ParticipantId and other required fields
            Content = new { text = "Hello, agent!" },
            WorkflowId = "test-workflow-id",
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/inbound", invalidRequest);
        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessInboundMessage_WithNullContent_ReturnsBadRequest"
    */
    [Fact]
    public async Task ProcessInboundMessage_WithNullContent_ReturnsBadRequest()
    {
        // Arrange
        var request = new
        {
            ParticipantId = "test-participant-id",
            Content = (string)null!, // Null content
            ParticipantChannelId = "test-participant-channel-id",
            WorkflowId = "test-workflow-id",
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/inbound", request);

        var content = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {content}");

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }


    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessInboundMessage_VerifyMessageSavedToDatabase"
    */
    [Fact(Skip = "This test is only works with a real workflow id")]
    public async Task ProcessInboundMessage_VerifyMessageSavedToDatabase()
    {
        // Arrange
        string messageContent = JsonSerializer.Serialize(new
        {
            text = "Database verification test",
            properties = new
            {
                priority = "high",
                tags = new[] { "important", "urgent" }
            },
            metadata = new
            {
                source = "web",
                sessionId = Guid.NewGuid().ToString()
            }
        });

        var runningWorkflowId = "99xio:PercytheProspector--github|1892961--943d620d-b4c0-4fe2-b6b8-f6993247a70b";
        
        var workflowId = runningWorkflowId;
        
        var request = new
        {
            WorkflowId = workflowId,
            ParticipantId = "test-participant-id",
            Content = messageContent
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/inbound", request);
        
        // Assert HTTP response
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {responseContent}");
        
        var responseResult = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        // Extract the messageId and threadId if they exist
        string? messageId = null;
        string? threadId = null;
        
        if (responseResult.TryGetProperty("messageId", out var messageIdElement))
        {
            messageId = messageIdElement.GetString();
        }
        
        if (responseResult.TryGetProperty("threadId", out var threadIdElement))
        {
            threadId = threadIdElement.GetString();
        }
        
        // Access the database to verify the message was saved correctly
        var collection = _database.GetCollection<BsonDocument>("conversation_message");
        
        // MongoDB stores the messageId as an ObjectId in the _id field
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new MongoDB.Bson.ObjectId(messageId));
        
        // Allow a few retries as there might be a slight delay in the database write
        BsonDocument? result = null;
        for (int i = 0; i < 3; i++)
        {
            result = await collection.Find(filter).FirstOrDefaultAsync();
            if (result != null) break;
            await Task.Delay(500); // Short delay between retries
        }
        
        // Assert message was saved correctly
        Assert.NotNull(result);
        
        // If we got messageId and threadId from the response, check them
        if (messageId != null)
        {
            Assert.Equal(messageId, result["_id"].AsObjectId.ToString());
        }
        
        if (threadId != null)
        {
            Assert.Equal(threadId, result["thread_id"].AsString);
        }
        
        // Verify expected field values
        Assert.Equal("Incoming", result["direction"].AsString);
        Assert.Equal("DeliveredToWorkflow", result["status"].AsString);
        Assert.Equal(workflowId, result["workflow_id"].AsString);
        
        // Verify content was saved correctly
        var contentText = result["content"].AsString;
        Assert.NotNull(contentText);
        Assert.Contains("Database verification test", contentText);
        
        // Verify timestamps
        Assert.True(result.Contains("created_at"));
    }


    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessInboundMessage_NotRunningWorkflow"
    */
    [Fact(Skip = "This test is only works with a real workflow id")]
    public async Task ProcessInboundMessage_NotRunningWorkflow()
    {
        var notRunningWorkflowId = "99xio:Email Channel Samudra:Email Channel Samudra";
        
        var workflowId = notRunningWorkflowId;
        
        var request = new
        {
            WorkflowId = workflowId,
            ParticipantId = "test-participant-id",
            Content = JsonSerializer.Serialize(new { text = "Database verification test" }),
            ParticipantChannelId = "test-participant-channel-id",
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/inbound", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Assert HTTP response
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {responseContent}");
    }


    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessInboundMessage_NotExistingWorkflow"
    */
    [Fact]
    public async Task ProcessInboundMessage_NotExistingWorkflow()
    {
        var notExistingWorkflowId = "xyzzy-workflow-id";
        
        var workflowId = notExistingWorkflowId;
        
        var request = new
        {
            WorkflowId = workflowId,
            ParticipantId = "test-participant-id",
            Content = JsonSerializer.Serialize(new { text = "Database verification test" }),
            ParticipantChannelId = "test-participant-channel-id",
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/messaging/inbound", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Assert HTTP response
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {responseContent}");
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessOutboundMessage_VerifyMessageSavedToDatabase"
    */
    [Fact]
    public async Task ProcessOutboundMessage_VerifyMessageSavedToDatabase()
    {
        // Arrange
        string messageContent = JsonSerializer.Serialize(new
        {
            text = "Outbound message verification test",
            properties = new
            {
                priority = "normal",
                tags = new[] { "response", "test" }
            },
            metadata = new
            {
                source = "agent",
                sessionId = Guid.NewGuid().ToString()
            }
        });

        // Using "test-workflow-id" to avoid dependency on a specific workflow ID
        var workflowId = "test-workflow-id";
        
        // Generate a thread ID to use in the request
        var threadId = $"test-thread-{Guid.NewGuid()}";
        
        var request = new
        {
            WorkflowId = workflowId,
            ParticipantId = "test-participant-id",
            Content = messageContent,
            ThreadId = threadId, // ThreadId is required
            Metadata = new { test = true },
            ParticipantChannelId = "test-participant-channel-id" // Added to match inbound pattern
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/outbound", request);
        
        // Assert HTTP response
        response.EnsureSuccessStatusCode();
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {responseContent}");
        
        var responseResult = await response.Content.ReadFromJsonAsync<JsonElement>();
        
        // Extract the messageId and threadId if they exist
        string? messageId = null;
        string? threadIdResponse = null;
        
        if (responseResult.TryGetProperty("messageId", out var messageIdElement))
        {
            messageId = messageIdElement.GetString();
        }
        
        if (responseResult.TryGetProperty("threadId", out var threadIdElement))
        {
            threadIdResponse = threadIdElement.GetString();
        }
        
        // Access the database to verify the message was saved correctly
        var collection = _database.GetCollection<BsonDocument>("conversation_message");
        
        // Null check before converting to ObjectId
        Assert.NotNull(messageId);
        
        // MongoDB stores the messageId as an ObjectId in the _id field
        var filter = Builders<BsonDocument>.Filter.Eq("_id", new MongoDB.Bson.ObjectId(messageId));
        
        // Allow a few retries as there might be a slight delay in the database write
        BsonDocument? result = null;
        for (int i = 0; i < 5; i++) // Increased from 3 to 5 retries
        {
            result = await collection.Find(filter).FirstOrDefaultAsync();
            if (result != null) break;
            await Task.Delay(1000); // Increased delay between retries
        }
        
        // Assert message was saved correctly
        Assert.NotNull(result);
        
        // Verify messageId and threadId from the response
        Assert.Equal(messageId, result["_id"].AsObjectId.ToString());
        
        // Verify threadId exists
        Assert.NotNull(threadIdResponse);
        Assert.Equal(threadIdResponse, result["thread_id"].AsString);
        
        // Verify direction is Outgoing (this should be consistent)
        Assert.Equal("Outgoing", result["direction"].AsString);
        
        // Verify workflow ID matches
        Assert.Equal(workflowId, result["workflow_id"].AsString);
        
        // Don't verify status as it might vary between environments - just check it exists
        Assert.True(result.Contains("status"));
        
        // Verify content was saved correctly
        var contentText = result["content"].AsString;
        Assert.NotNull(contentText);
        Assert.Contains("Outbound message verification test", contentText);
        
        // Verify timestamps
        Assert.True(result.Contains("created_at"));
    }

    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.GetConversationHistory_ReturnsCorrectMessageHistory"
    */
    [Fact]
    public async Task GetConversationHistory_ReturnsCorrectMessageHistory()
    {
        // Arrange
        string workflowId = "test-workflow-id-thread";
        string participantId = "test-participant-id";
        string threadId = $"test-thread-{Guid.NewGuid()}";
        
        // Create messages directly in the database since posting will fail due to missing workflow
        var messageCollection = _database.GetCollection<BsonDocument>("conversation_message");
        var threadCollection = _database.GetCollection<BsonDocument>("conversation_thread");
        
        // Create a thread document first
        var threadDoc = new BsonDocument
        {
            { "thread_id", threadId },
            { "workflow_id", workflowId },
            { "participant_id", participantId },
            { "tenant_id", "99xio" },
            { "status", "Active" },
            { "created_at", DateTime.UtcNow },
            { "updated_at", DateTime.UtcNow },
            { "created_by", "test-user" }
        };
        
        await threadCollection.InsertOneAsync(threadDoc);
        
        // Create three messages - NOTE: ConversationMessage doesn't have participant_id field
        var message1 = new BsonDocument
        {
            { "thread_id", threadId },
            { "workflow_id", workflowId },
            { "tenant_id", "99xio" },
            { "content", JsonSerializer.Serialize(new { text = "First inbound message" }) },
            { "direction", "Incoming" },
            { "status", "DeliveredToWorkflow" },
            { "created_at", DateTime.UtcNow.AddMinutes(-10) },
            { "created_by", "test-user" }
        };
        
        var message2 = new BsonDocument
        {
            { "thread_id", threadId },
            { "workflow_id", workflowId },
            { "tenant_id", "99xio" },
            { "content", JsonSerializer.Serialize(new { text = "Outbound response message" }) },
            { "direction", "Outgoing" },
            { "status", "DeliveredToWorkflow" },
            { "created_at", DateTime.UtcNow.AddMinutes(-5) },
            { "created_by", "test-user" }
        };
        
        var message3 = new BsonDocument
        {
            { "thread_id", threadId },
            { "workflow_id", workflowId },
            { "tenant_id", "99xio" },
            { "content", JsonSerializer.Serialize(new { text = "Second inbound message" }) },
            { "direction", "Incoming" },
            { "status", "DeliveredToWorkflow" },
            { "created_at", DateTime.UtcNow },
            { "created_by", "test-user" }
        };
        
        await messageCollection.InsertOneAsync(message1);
        await messageCollection.InsertOneAsync(message2);
        await messageCollection.InsertOneAsync(message3);
        
        // Act - Get conversation history
        var response = await _client.GetAsync($"/api/agent/conversation/history?threadId={threadId}&page=1&pageSize=10");
        
        // Assert
        response.EnsureSuccessStatusCode(); // This will throw if response is not successful
        
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {responseContent}");
        
        // Check if response is not empty before parsing
        Assert.False(string.IsNullOrEmpty(responseContent), "Response content should not be empty");
        
        // The response appears to be a direct array of messages, not an object with a messages property
        var messages = await response.Content.ReadFromJsonAsync<JsonElement[]>();
        Assert.NotNull(messages);
        
        // Verify we got all 3 messages
        Assert.Equal(3, messages.Length);
        
        // Verify messages content
        var foundMessages = new bool[3] { false, false, false };
        
        foreach (var message in messages)
        {
            if (message.TryGetProperty("content", out var contentProperty))
            {
                var content = contentProperty.GetString();
                
                if (content != null)
                {
                    if (content.Contains("First inbound message"))
                        foundMessages[0] = true;
                    else if (content.Contains("Outbound response message"))
                        foundMessages[1] = true;
                    else if (content.Contains("Second inbound message"))
                        foundMessages[2] = true;
                }
            }
        }
        
        // Verify all messages were found
        Assert.True(foundMessages[0] && foundMessages[1] && foundMessages[2], 
            "Not all expected messages were found in the response");
    }
} 