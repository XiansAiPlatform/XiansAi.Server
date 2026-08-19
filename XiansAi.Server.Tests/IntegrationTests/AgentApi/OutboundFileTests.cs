using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Repositories;
using Shared.Services;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

public class OutboundFileTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public OutboundFileTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task OutboundFile_WithOwnedRef_PersistsStoredMetadataWithoutContent()
    {
        var workflowId = NewWorkflowId();
        var participantId = NewParticipantId();
        var bytes = Encoding.UTF8.GetBytes("the report body");
        var uploaded = await UploadFileAsync(participantId, "report.pdf", "application/pdf", bytes);

        // The agent claims a different name and size; the server must persist the stored values.
        var response = await SendOutboundFileAsync(workflowId, participantId, "Here is the report", new
        {
            fileId = uploaded.FileId,
            fileName = "spoofed.pdf",
            contentType = "text/html",
            fileSize = 999999
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

        var file = files[0].AsBsonDocument;
        Assert.Equal(uploaded.FileId, file["fileId"].AsString);
        Assert.Equal("report.pdf", file["fileName"].AsString);
        Assert.Equal("application/pdf", file["contentType"].AsString);
        Assert.Equal(bytes.Length, file["fileSize"].ToInt64());
        Assert.False(file.Contains("content"));
    }

    [Fact]
    public async Task OutboundFile_WithAnotherParticipantsFile_ReturnsBadRequest()
    {
        var workflowId = NewWorkflowId();
        var fileOwnerId = NewParticipantId();
        var recipientId = NewParticipantId();
        var uploaded = await UploadFileAsync(
            fileOwnerId, "payslip.pdf", "application/pdf", Encoding.UTF8.GetBytes("owner only"));

        var response = await SendOutboundFileAsync(workflowId, recipientId, "leaked?", new
        {
            fileId = uploaded.FileId,
            fileName = "payslip.pdf"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not available for this participant", body);

        Assert.Null(await FindLatestOutgoingFileAsync(workflowId, recipientId));
    }

    [Fact]
    public async Task OutboundFile_WithCrossTenantFile_ReturnsBadRequest()
    {
        var workflowId = NewWorkflowId();
        var participantId = NewParticipantId();

        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IMessageFileStorage>();
        var stored = await storage.UploadAsync(
            "other-tenant",
            participantId,
            "secret.txt",
            "text/plain",
            Encoding.UTF8.GetBytes("cross-tenant secret"));

        var response = await SendOutboundFileAsync(workflowId, participantId, "leaked?", new
        {
            fileId = stored.FileId,
            fileName = "secret.txt"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await FindLatestOutgoingFileAsync(workflowId, participantId));
    }

    [Fact]
    public async Task OutboundFile_WithUnknownFileId_ReturnsBadRequest()
    {
        var workflowId = NewWorkflowId();
        var participantId = NewParticipantId();

        var response = await SendOutboundFileAsync(workflowId, participantId, "ghost", new
        {
            fileId = ObjectId.GenerateNewId().ToString(),
            fileName = "ghost.pdf"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await FindLatestOutgoingFileAsync(workflowId, participantId));
    }

    [Fact]
    public async Task OutboundFile_WithMalformedFileId_ReturnsBadRequest()
    {
        var workflowId = NewWorkflowId();
        var participantId = NewParticipantId();

        var response = await SendOutboundFileAsync(workflowId, participantId, "bad id", new
        {
            fileId = "not-an-object-id",
            fileName = "ghost.pdf"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Null(await FindLatestOutgoingFileAsync(workflowId, participantId));
    }

    [Fact]
    public async Task OutboundFile_WithInlineContent_ReturnsBadRequest()
    {
        var response = await SendOutboundFileAsync(NewWorkflowId(), NewParticipantId(), null, new
        {
            fileId = ObjectId.GenerateNewId().ToString(),
            fileName = "report.pdf",
            content = Convert.ToBase64String(new byte[] { 1, 2, 3 })
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("fileId references only", body);
    }

    [Fact]
    public async Task OutboundFile_WithNoFiles_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/agent/conversation/outbound/file", new
        {
            participantId = NewParticipantId(),
            workflowId = NewWorkflowId(),
            data = new { files = Array.Empty<object>() }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("data.files must be a non-empty array", body);
    }

    [Fact]
    public async Task OutboundFile_DoesNotCopyLastIncomingData()
    {
        var workflowId = NewWorkflowId();
        var participantId = NewParticipantId();
        var threadId = await SeedThreadWithIncomingPlatformDataAsync(workflowId, participantId);
        var uploaded = await UploadFileAsync(
            participantId, "reply.txt", "text/plain", Encoding.UTF8.GetBytes("body"));

        var response = await SendOutboundFileAsync(workflowId, participantId, "file reply", new
        {
            fileId = uploaded.FileId,
            fileName = "reply.txt",
            contentType = "text/plain",
            fileSize = 4
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
        Assert.Equal(uploaded.FileId, data["files"].AsBsonArray[0]["fileId"].AsString);
    }

    private static string NewWorkflowId() =>
        $"{TestTenantId}:FileAgent:Supervisor Workflow:{Guid.NewGuid()}";

    private static string NewParticipantId() => $"file-user-{Guid.NewGuid()}@example.com";

    private async Task<UploadedFileRef> UploadFileAsync(
        string participantId, string fileName, string contentType, byte[] bytes)
    {
        var response = await _client.PostAsJsonAsync("/api/agent/files", new
        {
            participantId,
            files = new[]
            {
                new
                {
                    content = Convert.ToBase64String(bytes),
                    fileName,
                    contentType,
                    fileSize = bytes.Length
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var uploaded = await response.Content.ReadFromJsonAsync<UploadedFilesResponse>(JsonOptions);
        Assert.NotNull(uploaded);
        return Assert.Single(uploaded.Files);
    }

    private async Task<HttpResponseMessage> SendOutboundFileAsync(
        string workflowId, string participantId, string? text, object file)
    {
        return await _client.PostAsJsonAsync("/api/agent/conversation/outbound/file", new
        {
            participantId,
            workflowId,
            text,
            data = new { files = new[] { file } }
        });
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

    private sealed class UploadedFilesResponse
    {
        public List<UploadedFileRef> Files { get; set; } = new();
    }

    private sealed class UploadedFileRef
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }
}
