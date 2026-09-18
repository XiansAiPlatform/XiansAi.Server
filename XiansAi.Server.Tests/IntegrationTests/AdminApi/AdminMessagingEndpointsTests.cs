using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Repositories;
using Shared.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

public class AdminMessagingEndpointsTests : AdminApiIntegrationTestBase
{
    public AdminMessagingEndpointsTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    [Fact]
    public async Task MarkThreadAsRead_WithTimestamp_MarksMessagesUpToCutoffAndReturnsRemainingUnreadCount()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        var msg1 = await CreateTestMessageAsync(tenantId, threadId, baseTime);
        var msg2 = await CreateTestMessageAsync(tenantId, threadId, baseTime.AddMinutes(1));
        var msg3 = await CreateTestMessageAsync(tenantId, threadId, baseTime.AddMinutes(2));

        var request = new { timestamp = msg2.CreatedAt };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<MarkThreadReadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(2, result!.MarkedCount);
        Assert.Equal(1, result.UnreadCount);

        Assert.Equal(MessageStatus.Read, (await GetMessageByIdAsync(msg1.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetMessageByIdAsync(msg2.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetMessageByIdAsync(msg3.Id))!.Status);
    }

    [Fact]
    public async Task MarkThreadAsRead_WithMessageId_ResolvesCutoffFromMessageCreatedAt()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        var msg1 = await CreateTestMessageAsync(tenantId, threadId, baseTime);
        var msg2 = await CreateTestMessageAsync(tenantId, threadId, baseTime.AddMinutes(1));
        var msg3 = await CreateTestMessageAsync(tenantId, threadId, baseTime.AddMinutes(2));

        var request = new { messageId = msg2.Id };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<MarkThreadReadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(2, result!.MarkedCount);
        Assert.Equal(1, result.UnreadCount);

        Assert.Equal(MessageStatus.Read, (await GetMessageByIdAsync(msg1.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetMessageByIdAsync(msg2.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetMessageByIdAsync(msg3.Id))!.Status);
    }

    [Fact]
    public async Task MarkThreadAsRead_WithNeitherTimestampNorMessageId_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{ObjectId.GenerateNewId()}/read",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MarkThreadAsRead_WithBothTimestampAndMessageId_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var msg = await CreateTestMessageAsync(tenantId, threadId, DateTime.UtcNow);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read",
            new { timestamp = DateTime.UtcNow, messageId = msg.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MarkThreadAsRead_WithMessageIdFromAnotherThread_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var otherThreadId = ObjectId.GenerateNewId().ToString();
        var msgInOtherThread = await CreateTestMessageAsync(tenantId, otherThreadId, DateTime.UtcNow);

        var targetThreadId = ObjectId.GenerateNewId().ToString();

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{targetThreadId}/read",
            new { messageId = msgInOtherThread.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private class MarkThreadReadResponse
    {
        public long MarkedCount { get; set; }
        public long UnreadCount { get; set; }
    }

    private async Task<ConversationMessage> CreateTestMessageAsync(string tenantId, string threadId, DateTime createdAt)
    {
        using var scope = _factory.Services.CreateScope();
        var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var message = new ConversationMessage
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ThreadId = threadId,
            TenantId = tenantId,
            ParticipantId = $"test-participant-{Guid.NewGuid()}",
            WorkflowId = $"test-workflow-{Guid.NewGuid()}",
            WorkflowType = "TestWorkflowType",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            CreatedBy = "test-user-id",
            Direction = MessageDirection.Incoming,
            Text = "Test message",
            Status = MessageStatus.Unread
        };

        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationMessage>("conversation_message");
        await collection.InsertOneAsync(message);

        return message;
    }

    private async Task<ConversationMessage?> GetMessageByIdAsync(string messageId)
    {
        using var scope = _factory.Services.CreateScope();
        var conversationRepository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        return await conversationRepository.GetMessageByIdAsync(messageId, _adminTenantId!);
    }

    [Fact]
    public async Task SendDataToWorkflow_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var request = new
        {
            threadId = $"thread-{Guid.NewGuid()}",
            data = new { key = "value" },
            agent = $"agent-{Guid.NewGuid()}"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/inbound/data", request);

        // Assert
        // The response depends on workflow processing, but should not be 401/403 if authenticated
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SendChatToWorkflow_WithValidRequest_ReturnsSuccess()
    {
        // Arrange
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var request = new
        {
            threadId = $"thread-{Guid.NewGuid()}",
            message = "Test message",
            agent = $"agent-{Guid.NewGuid()}"
        };

        // Act
        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/inbound/chat", request);

        // Assert
        // The response depends on workflow processing, but should not be 401/403 if authenticated
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DownloadMessageFile_AgentUploadedFile_ReturnsBytesNameTypeAndAttachmentDisposition()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var bytes = Encoding.UTF8.GetBytes("agent-sent file body");
        var stored = await UploadFileAsync(
            tenantId,
            "user@example.com",
            "Q2 report (final).pdf",
            "application/pdf",
            bytes);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/files/{stored.FileId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? string.Empty;
        Assert.Contains("Q2 report (final).pdf", fileName.Trim('"'));
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DownloadMessageFile_WrongTenant_ReturnsNotFound()
    {
        var ownerTenantId = $"owner-tenant-{Guid.NewGuid()}";
        var otherTenantId = $"other-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(ownerTenantId);
        await CreateTestTenantAsync(otherTenantId);
        await ConfigureAdminApiClientAsync(otherTenantId);

        var stored = await UploadFileAsync(
            ownerTenantId,
            "user@example.com",
            "secret.txt",
            "text/plain",
            Encoding.UTF8.GetBytes("cross-tenant secret"));

        var response = await GetAsync($"/api/v1/admin/tenants/{otherTenantId}/messaging/files/{stored.FileId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadMessageFile_MissingId_ReturnsNotFound()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/files/{MongoDB.Bson.ObjectId.GenerateNewId()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private async Task<StoredFileRef> UploadFileAsync(
        string tenantId,
        string participantId,
        string fileName,
        string contentType,
        byte[] content)
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IMessageFileStorage>();
        return await storage.UploadAsync(tenantId, participantId, fileName, contentType, content);
    }
}

