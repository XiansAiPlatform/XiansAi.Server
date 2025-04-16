using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using XiansAi.Server.Tests.TestUtils;
using Features.AgentApi.Services.Agent;
using XiansAi.Server.Src.Features.AgentApi.Repositories;
using MongoDB.Driver;

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
            ParticipantChannelId = "test-participant-channel-id",
            WorkflowId = "test-workflow-id",
            FailIfNotRunning = false
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversations/inbound", invalidRequest);
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
        var request = new InboundMessageRequest
        {
            ParticipantId = "test-participant-id",
            Content = null!, // Null content
            ParticipantChannelId = "test-participant-channel-id",
            WorkflowId = "test-workflow-id",
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversations/inbound", request);

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
        string uniqueChannelKey = $"test-channel-key-{Guid.NewGuid()}";
        var messageContent = new
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
        };

        var runningWorkflowId = "99xio:PercytheProspector--github|1892961--943d620d-b4c0-4fe2-b6b8-f6993247a70b";
        
        var workflowId = runningWorkflowId;
        
        var request = new InboundMessageRequest
        {
            WorkflowId = workflowId,
            ParticipantId = "test-participant-id",
            Content = messageContent,
            ParticipantChannelId = uniqueChannelKey, // Use a unique key to easily find this message
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversations/inbound", request);
        
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
        var collection = _database.GetCollection<ConversationMessage>("conversation_message");
        var filter = Builders<ConversationMessage>.Filter.Eq("participant_channel_id", uniqueChannelKey);
        
        // Allow a few retries as there might be a slight delay in the database write
        ConversationMessage? result = null;
        for (int i = 0; i < 3; i++)
        {
            result = await collection.Find(filter).FirstOrDefaultAsync();
            if (result != null) break;
            await Task.Delay(500); // Short delay between retries
        }
        
        // Assert message was saved correctly
        Assert.NotNull(result);
        Assert.Equal(uniqueChannelKey, result.ParticipantChannelId);
        
        // If we got messageId and threadId from the response, check them
        if (messageId != null)
        {
            Assert.Equal(messageId, result.Id);
        }
        
        if (threadId != null)
        {
            Assert.Equal(threadId, result.ThreadId);
        }
        
        Assert.Equal("test-participant-id", result.CreatedBy);
        Assert.Equal(MessageDirection.Inbound, result.Direction);
        Assert.Equal(MessageStatus.DeliveredToWorkflow, result.Status);
        Assert.Equal(workflowId, result.WorkflowId);
        
        // Verify content was saved correctly
        var contentDoc = result.Content;
        Assert.NotNull(contentDoc);
        Assert.True(contentDoc.Contains("text") && contentDoc["text"].AsString == "Database verification test");
        
        // Verify timestamps
        Assert.NotEqual(default, result.CreatedAt);
    }


    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessInboundMessage_VerifyMessageSavedToDatabase_NotRunningWorkflow"
    */
    [Fact(Skip = "This test is only works with a real workflow id")]
    public async Task ProcessInboundMessage_VerifyMessageSavedToDatabase_NotRunningWorkflow()
    {

        var notRunningWorkflowId = "99xio:Email Channel Samudra:Email Channel Samudra";
        
        var workflowId = notRunningWorkflowId;
        
        var request = new InboundMessageRequest
        {
            WorkflowId = workflowId,
            ParticipantId = "test-participant-id",
            Content = new { text = "Database verification test" },
            ParticipantChannelId = "test-participant-channel-id",
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversations/inbound", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Assert HTTP response
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {responseContent}");
    
    }


    /*
    dotnet test --filter "FullyQualifiedName=XiansAi.Server.Tests.IntegrationTests.AgentApi.ConversationEndpointTests.ProcessInboundMessage_VerifyMessageSavedToDatabase_NotExistingWorkflow"
    */
    [Fact]
    public async Task ProcessInboundMessage_VerifyMessageSavedToDatabase_NotExistingWorkflow()
    {

        var notExistingWorkflowId = "xyzzy-workflow-id";
        
        var workflowId = notExistingWorkflowId;
        
        var request = new InboundMessageRequest
        {
            WorkflowId = workflowId,
            ParticipantId = "test-participant-id",
            Content = new { text = "Database verification test" },
            ParticipantChannelId = "test-participant-channel-id",
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/agent/conversations/inbound", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        // Assert HTTP response
        var responseContent = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Response content: {responseContent}");
    
    }
} 