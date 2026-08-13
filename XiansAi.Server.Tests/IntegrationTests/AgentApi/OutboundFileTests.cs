using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Repositories;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

public class OutboundFileTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public OutboundFileTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task OutboundFile_WithRefs_PersistsOutgoingFileWithoutContent()
    {
        var workflowId = $"{TestTenantId}:FileAgent:Supervisor Workflow:{Guid.NewGuid()}";
        var participantId = $"file-user-{Guid.NewGuid()}@example.com";
        var fileId = ObjectId.GenerateNewId().ToString();

        var response = await _client.PostAsJsonAsync("/api/agent/conversation/outbound/file", new
        {
            participantId,
            workflowId,
            text = "Here is the report",
            data = new
            {
                files = new[]
                {
                    new
                    {
                        fileId,
                        fileName = "report.pdf",
                        contentType = "application/pdf",
                        fileSize = 12345
                    }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var message = await FindLatestOutgoingFileAsync(workflowId, participantId);
        Assert.NotNull(message);
        Assert.Equal(MessageDirection.Outgoing, message.Direction);
        Assert.Equal(MessageType.File, message.MessageType);
        Assert.False(string.IsNullOrWhiteSpace(message.Text));

        var data = ToBsonDocument(message.Data);
        Assert.NotNull(data);
        var files = data["files"].AsBsonArray;
        Assert.Single(files);
        Assert.Equal(fileId, files[0]["fileId"].AsString);
        Assert.Equal("report.pdf", files[0]["fileName"].AsString);
        Assert.False(files[0].AsBsonDocument.Contains("content"));
    }

    [Fact]
    public async Task OutboundFile_WithInlineContent_ReturnsBadRequest()
    {
        var workflowId = $"{TestTenantId}:FileAgent:Supervisor Workflow:{Guid.NewGuid()}";
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/outbound/file", new
        {
            participantId = $"file-user-{Guid.NewGuid()}@example.com",
            workflowId,
            data = new
            {
                files = new[]
                {
                    new
                    {
                        fileId = ObjectId.GenerateNewId().ToString(),
                        fileName = "report.pdf",
                        content = Convert.ToBase64String(new byte[] { 1, 2, 3 })
                    }
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("fileId references only", body);
    }

    [Fact]
    public async Task OutboundFile_WithNoFiles_ReturnsBadRequest()
    {
        var workflowId = $"{TestTenantId}:FileAgent:Supervisor Workflow:{Guid.NewGuid()}";
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/outbound/file", new
        {
            participantId = $"file-user-{Guid.NewGuid()}@example.com",
            workflowId,
            data = new { files = Array.Empty<object>() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data.files must be a non-empty array", body);
    }

    [Fact]
    public async Task OutboundFile_DoesNotCopyLastIncomingData()
    {
        var workflowId = $"{TestTenantId}:FileAgent:Supervisor Workflow:{Guid.NewGuid()}";
        var participantId = $"file-user-{Guid.NewGuid()}@example.com";
        var fileId = ObjectId.GenerateNewId().ToString();
        var threadId = await SeedThreadWithIncomingPlatformDataAsync(workflowId, participantId);

        var response = await _client.PostAsJsonAsync("/api/agent/conversation/outbound/file", new
        {
            participantId,
            workflowId,
            text = "file reply",
            data = new
            {
                files = new[]
                {
                    new
                    {
                        fileId,
                        fileName = "reply.txt",
                        contentType = "text/plain",
                        fileSize = 4
                    }
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var message = await FindLatestOutgoingFileAsync(workflowId, participantId);
        Assert.NotNull(message);
        Assert.Equal(threadId, message.ThreadId);
        Assert.Equal("app:slack:int-1", message.Origin);

        var data = ToBsonDocument(message.Data);
        Assert.NotNull(data);
        Assert.False(data.Contains("stolen"));
        Assert.False(data.Contains("channel"));
        Assert.Equal(fileId, data["files"].AsBsonArray[0]["fileId"].AsString);
    }

    private async Task<string> SeedThreadWithIncomingPlatformDataAsync(string workflowId, string participantId)
    {
        using var serviceScope = _factory.Services.CreateScope();
        var databaseService = serviceScope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var database = await databaseService.GetDatabaseAsync();

        var normalizedParticipantId = participantId.ToLowerInvariant();
        var threadId = ObjectId.GenerateNewId().ToString();
        var thread = new ConversationThread
        {
            Id = threadId,
            TenantId = TestTenantId,
            WorkflowId = workflowId,
            WorkflowType = "FileAgent:Supervisor Workflow",
            Agent = "FileAgent",
            ParticipantId = normalizedParticipantId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = "test-user-id",
            Status = ConversationThreadStatus.Active
        };
        await database.GetCollection<ConversationThread>("conversation_thread").InsertOneAsync(thread);

        var incoming = new ConversationMessage
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ThreadId = threadId,
            TenantId = TestTenantId,
            ParticipantId = normalizedParticipantId,
            WorkflowId = workflowId,
            WorkflowType = "FileAgent:Supervisor Workflow",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = "test-user-id",
            Direction = MessageDirection.Incoming,
            Text = "please send the file",
            Status = MessageStatus.DeliveredToWorkflow,
            Origin = "app:slack:int-1",
            Data = new BsonDocument
            {
                { "stolen", "slack-meta" },
                { "channel", "C123" }
            },
            MessageType = MessageType.Chat
        };
        await database.GetCollection<ConversationMessage>("conversation_message").InsertOneAsync(incoming);

        return threadId;
    }

    private async Task<ConversationMessage?> FindLatestOutgoingFileAsync(string workflowId, string participantId)
    {
        using var serviceScope = _factory.Services.CreateScope();
        var databaseService = serviceScope.ServiceProvider.GetRequiredService<IDatabaseService>();
        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationMessage>("conversation_message");
        var normalizedParticipantId = participantId.ToLowerInvariant();

        return await collection
            .Find(m =>
                m.WorkflowId == workflowId &&
                m.ParticipantId == normalizedParticipantId &&
                m.Direction == MessageDirection.Outgoing &&
                m.MessageType == MessageType.File)
            .SortByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();
    }

    private static BsonDocument ToBsonDocument(object? data)
    {
        return data switch
        {
            BsonDocument document => document,
            JsonElement element => BsonDocument.Parse(element.GetRawText()),
            _ => BsonDocument.Parse(JsonSerializer.Serialize(data))
        };
    }
}
