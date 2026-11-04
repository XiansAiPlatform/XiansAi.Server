using System.Net;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
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

        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(msg1.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(msg2.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(msg3.Id))!.Status);
    }

    [Fact]
    public async Task MarkThreadAsRead_UnreadCountIgnoresUserMessagesThatWerePreviouslyUnread()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);

        var userBeforeCutoff = await CreateTestMessageAsync(
            tenantId, threadId, baseTime, MessageDirection.Incoming, MessageType.Chat, MessageStatus.Unread);
        var agentChat = await CreateTestMessageAsync(
            tenantId, threadId, baseTime.AddMinutes(1), MessageDirection.Outgoing, MessageType.Chat, MessageStatus.Unread);
        var agentFile = await CreateTestMessageAsync(
            tenantId, threadId, baseTime.AddMinutes(1), MessageDirection.Outgoing, MessageType.File, MessageStatus.Unread);

        var userStillUnread = await CreateTestMessageAsync(
            tenantId, threadId, baseTime.AddMinutes(2), MessageDirection.Incoming, MessageType.Chat, MessageStatus.Unread);
        var tool = await CreateTestMessageAsync(
            tenantId, threadId, baseTime.AddMinutes(2), MessageDirection.Outgoing, MessageType.Tool, MessageStatus.Unread);
        var reasoning = await CreateTestMessageAsync(
            tenantId, threadId, baseTime.AddMinutes(2), MessageDirection.Outgoing, MessageType.Reasoning, MessageStatus.Unread);
        var agentChatAfterCutoff = await CreateTestMessageAsync(
            tenantId, threadId, baseTime.AddMinutes(3), MessageDirection.Outgoing, MessageType.Chat, MessageStatus.Unread);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read",
            new { timestamp = agentChat.CreatedAt });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<MarkThreadReadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(2, result!.MarkedCount);
        Assert.Equal(1, result.UnreadCount);

        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(userBeforeCutoff.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(agentChat.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(agentFile.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(userStillUnread.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(tool.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(reasoning.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(agentChatAfterCutoff.Id))!.Status);
    }

    [Fact]
    public async Task GetHistory_ReportsLegacyRowsThatCannotBeUnreadAsReadWithoutRewritingThem()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");
        var participantId = "reader@example.com";
        var seeded = await SeedConversationAsync(tenantId, agent.Name, activation.Name, participantId, "seeded chat");

        var legacyUnread = await InsertHistoryMessageAsync(
            tenantId, seeded.ThreadId, seeded.WorkflowId, seeded.WorkflowType, participantId,
            MessageDirection.Incoming, MessageType.Chat, MessageStatus.Unread, "legacy unread user");
        var legacyNull = await InsertHistoryMessageAsync(
            tenantId, seeded.ThreadId, seeded.WorkflowId, seeded.WorkflowType, participantId,
            MessageDirection.Incoming, MessageType.File, null, "legacy null user file");
        var legacyTool = await InsertHistoryMessageAsync(
            tenantId, seeded.ThreadId, seeded.WorkflowId, seeded.WorkflowType, participantId,
            MessageDirection.Outgoing, MessageType.Tool, MessageStatus.Unread, "legacy tool");
        var agentChat = await InsertHistoryMessageAsync(
            tenantId, seeded.ThreadId, seeded.WorkflowId, seeded.WorkflowType, participantId,
            MessageDirection.Outgoing, MessageType.Chat, MessageStatus.Unread, "agent reply");

        var query = $"agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&participantId={Uri.EscapeDataString(participantId)}";
        var historyResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await ReadAsJsonAsync<List<ConversationMessage>>(historyResponse);
        Assert.NotNull(history);

        Assert.Equal(MessageStatus.Read, history!.Single(m => m.Id == legacyUnread.Id).Status);
        Assert.Equal(MessageStatus.Read, history.Single(m => m.Id == legacyNull.Id).Status);
        Assert.Equal(MessageStatus.Read, history.Single(m => m.Id == legacyTool.Id).Status);
        Assert.Equal(MessageStatus.Unread, history.Single(m => m.Id == agentChat.Id).Status);

        using (var scope = _factory.Services.CreateScope())
        {
            var repository = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
            Assert.Equal(MessageStatus.Read, (await repository.GetMessageByIdAsync(legacyUnread.Id, tenantId))!.Status);
            Assert.Equal(MessageStatus.Read, (await repository.GetMessageByIdAsync(legacyNull.Id, tenantId))!.Status);
            Assert.Equal(MessageStatus.Unread, (await repository.GetMessageByIdAsync(agentChat.Id, tenantId))!.Status);
        }

        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(legacyUnread.Id))!.Status);
        Assert.Null((await GetStoredMessageAsync(legacyNull.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(legacyTool.Id))!.Status);
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

        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(msg1.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(msg2.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(msg3.Id))!.Status);
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

    [Fact]
    public async Task MarkThreadAsRead_WithScope_MarksOnlyThatTopicAndReportsThreadWideUnread()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        var billing1 = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: "billing");
        var billing2 = await CreateTestMessageAsync(tenantId, threadId, baseTime.AddMinutes(1), scope: "billing");
        var billingAfterCutoff = await CreateTestMessageAsync(tenantId, threadId, baseTime.AddMinutes(5), scope: "billing");
        var sales = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: "sales");
        var defaultTopic = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: null);

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read",
            new { timestamp = billing2.CreatedAt, scope = "billing" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<MarkThreadReadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(2, result!.MarkedCount);
        Assert.Equal(3, result.UnreadCount);

        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(billing1.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(billing2.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(billingAfterCutoff.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(sales.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(defaultTopic.Id))!.Status);
    }

    [Fact]
    public async Task MarkThreadAsRead_WithEmptyScope_MarksOnlyTheDefaultTopic()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        var defaultTopic = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: null);
        var legacyNullStatus = await CreateTestMessageAsync(tenantId, threadId, baseTime, status: null, scope: null);
        var billing = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: "billing");

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read",
            new { timestamp = DateTime.UtcNow, scope = "" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<MarkThreadReadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(2, result!.MarkedCount);
        Assert.Equal(1, result.UnreadCount);

        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(defaultTopic.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(legacyNullStatus.Id))!.Status);
        Assert.Equal(MessageStatus.Unread, (await GetStoredMessageAsync(billing.Id))!.Status);
    }

    [Fact]
    public async Task MarkThreadAsRead_WithoutScope_MarksEveryTopic()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var baseTime = DateTime.UtcNow.AddMinutes(-10);
        var defaultTopic = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: null);
        var billing = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: "billing");
        var sales = await CreateTestMessageAsync(tenantId, threadId, baseTime, scope: "sales");

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read",
            new { timestamp = DateTime.UtcNow });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<MarkThreadReadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(3, result!.MarkedCount);
        Assert.Equal(0, result.UnreadCount);

        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(defaultTopic.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(billing.Id))!.Status);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(sales.Id))!.Status);
    }

    [Fact]
    public async Task MarkThreadAsRead_AsTenantAdmin_IsAllowedByTheCapabilityMatrix()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);
        await CreateTestTenantAsync(tenantId);

        var threadId = ObjectId.GenerateNewId().ToString();
        var msg = await CreateTestMessageAsync(tenantId, threadId, DateTime.UtcNow.AddMinutes(-1));

        var response = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/threads/{threadId}/read",
            new { messageId = msg.Id });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(MessageStatus.Read, (await GetStoredMessageAsync(msg.Id))!.Status);
    }

    [Fact]
    public async Task GetUnread_CountsOutgoingChatAndFilePerTopic_AndIgnoresIncomingAndData()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");
        var participantId = "reader@example.com";
        var seeded = await SeedConversationAsync(tenantId, agent.Name, activation.Name, participantId, "seeded chat");

        // Counted: Outgoing Chat/File that are Unread or carry no status (legacy rows).
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.Chat, MessageStatus.Unread, "acme 1", "Acme Corp");
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.File, MessageStatus.Unread, "acme 2", "Acme Corp");
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.Chat, null, "acme legacy", "Acme Corp");
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.Chat, MessageStatus.Unread, "default topic", null);

        // Not counted: already read, participant's own sends, Data messages, legacy Tool rows.
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.Chat, MessageStatus.Read, "acme read", "Acme Corp");
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.Chat, MessageStatus.Read, "quiet topic read", "Quiet");
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Incoming, MessageType.Chat, MessageStatus.Unread, "user unread", "Acme Corp");
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Incoming, MessageType.Chat, null, "user legacy", null);
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.Data, MessageStatus.Unread, "data", "Acme Corp");
        await InsertHistoryMessageAsync(tenantId, seeded, participantId,
            MessageDirection.Outgoing, MessageType.Tool, null, "tool", null);

        var query = $"agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&participantId={Uri.EscapeDataString(participantId)}";
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/unread?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<ThreadUnreadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(seeded.ThreadId, result!.ThreadId);
        Assert.Equal(4, result.UnreadMessages);
        Assert.Equal(2, result.UnreadTopics);
        Assert.Equal(2, result.Topics.Count);
        Assert.Equal(1, result.Topics.Single(t => t.Scope == null).UnreadCount);
        Assert.Equal(3, result.Topics.Single(t => t.Scope == "Acme Corp").UnreadCount);
    }

    [Fact]
    public async Task GetUnread_ThreadWithNoUnreadMessages_ReturnsZerosAndNoTopics()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");
        var participantId = "reader@example.com";
        // Seeds the thread with one Incoming (participant-sent) message only.
        var seeded = await SeedConversationAsync(tenantId, agent.Name, activation.Name, participantId, "seeded chat");

        var query = $"agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&participantId={Uri.EscapeDataString(participantId)}";
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/unread?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<ThreadUnreadResponse>(response);
        Assert.NotNull(result);
        Assert.Equal(seeded.ThreadId, result!.ThreadId);
        Assert.Equal(0, result.UnreadMessages);
        Assert.Equal(0, result.UnreadTopics);
        Assert.Empty(result.Topics);
    }

    [Fact]
    public async Task GetUnread_WhenNoThreadExists_ReturnsZerosWithNullThreadId()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");

        var query = $"agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&participantId=nobody%40example.com";
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/unread?{query}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await ReadAsJsonAsync<ThreadUnreadResponse>(response);
        Assert.NotNull(result);
        Assert.Null(result!.ThreadId);
        Assert.Equal(0, result.UnreadMessages);
        Assert.Equal(0, result.UnreadTopics);
        Assert.Empty(result.Topics);
    }

    [Fact]
    public async Task GetUnread_WithMissingQuery_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/unread?agentName=only-agent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_WithMissingQuery_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetTopics_WithMissingQuery_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/topics?agentName=only-agent");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SendMessage_WhenAgentHasNoWorkflow_ReturnsBadRequest()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);

        var response = await PostAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/messaging/send", new
        {
            agentName = agent.Name,
            activationName = "missing-activation",
            participantId = "user@example.com",
            text = "hello"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetHistoryAndTopics_ThenDeleteMessages_RoundTripsMongo()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");
        var participantId = "reader@example.com";
        await SeedConversationAsync(tenantId, agent.Name, activation.Name, participantId, "seeded chat", "billing");

        var query = $"agentName={Uri.EscapeDataString(agent.Name)}&activationName={Uri.EscapeDataString(activation.Name)}&participantId={Uri.EscapeDataString(participantId)}";

        var historyResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}&topic=billing");
        Assert.Equal(HttpStatusCode.OK, historyResponse.StatusCode);
        var history = await historyResponse.Content.ReadAsStringAsync();
        Assert.Contains("seeded chat", history);

        var topicsResponse = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/topics?{query}");
        Assert.Equal(HttpStatusCode.OK, topicsResponse.StatusCode);
        var topics = await topicsResponse.Content.ReadAsStringAsync();
        Assert.Contains("billing", topics);

        var deleteResponse = await DeleteAsync($"/api/v1/admin/tenants/{tenantId}/messaging/messages?{query}&topic=billing");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        var historyAfterDelete = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/history?{query}&topic=billing");
        Assert.Equal(HttpStatusCode.OK, historyAfterDelete.StatusCode);
        var after = await historyAfterDelete.Content.ReadAsStringAsync();
        Assert.DoesNotContain("seeded chat", after);
    }

    [Fact]
    public async Task DeleteMessagesByActivation_RemovesSeededThreads()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);

        var agent = await CreateTestAgentAsync($"agent-{Guid.NewGuid()}", tenantId);
        await CreateBuiltInFlowDefinitionAsync(agent.Name, tenantId);
        var activation = await CreateTestActivationAsync(agent.Name, tenantId, isActive: true, name: "inbox");
        await SeedConversationAsync(tenantId, agent.Name, activation.Name, "reader@example.com", "to-delete");

        var response = await DeleteAsync(
            $"/api/v1/admin/tenants/{tenantId}/messaging/agents/{Uri.EscapeDataString(agent.Name)}/activation/{Uri.EscapeDataString(activation.Name)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("deletedCount", body);
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

    private class MarkThreadReadResponse
    {
        public long MarkedCount { get; set; }
        public long UnreadCount { get; set; }
    }

    private class ThreadUnreadResponse
    {
        public string? ThreadId { get; set; }
        public long UnreadMessages { get; set; }
        public int UnreadTopics { get; set; }
        public List<UnreadTopicResponse> Topics { get; set; } = new();
    }

    private class UnreadTopicResponse
    {
        public string? Scope { get; set; }
        public long UnreadCount { get; set; }
    }

    private async Task<ConversationMessage> CreateTestMessageAsync(
        string tenantId,
        string threadId,
        DateTime createdAt,
        MessageDirection direction = MessageDirection.Outgoing,
        MessageType messageType = MessageType.Chat,
        MessageStatus? status = MessageStatus.Unread,
        string? scope = null)
    {
        using var serviceScope = _factory.Services.CreateScope();
        var databaseService = serviceScope.ServiceProvider.GetRequiredService<IDatabaseService>();

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
            Direction = direction,
            MessageType = messageType,
            Text = "Test message",
            Status = status,
            Scope = scope
        };

        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationMessage>("conversation_message");
        await collection.InsertOneAsync(message);

        return message;
    }

    private Task<ConversationMessage> InsertHistoryMessageAsync(
        string tenantId,
        (string ThreadId, string WorkflowId, string WorkflowType, ConversationMessage Message) seeded,
        string participantId,
        MessageDirection direction,
        MessageType messageType,
        MessageStatus? status,
        string text,
        string? scope)
        => InsertHistoryMessageAsync(
            tenantId, seeded.ThreadId, seeded.WorkflowId, seeded.WorkflowType, participantId,
            direction, messageType, status, text, scope);

    private async Task<ConversationMessage> InsertHistoryMessageAsync(
        string tenantId,
        string threadId,
        string workflowId,
        string workflowType,
        string participantId,
        MessageDirection direction,
        MessageType messageType,
        MessageStatus? status,
        string text,
        string? scope = null)
    {
        using var serviceScope = _factory.Services.CreateScope();
        var databaseService = serviceScope.ServiceProvider.GetRequiredService<IDatabaseService>();

        var message = new ConversationMessage
        {
            Id = ObjectId.GenerateNewId().ToString(),
            ThreadId = threadId,
            TenantId = tenantId,
            ParticipantId = participantId.ToLowerInvariant(),
            WorkflowId = workflowId,
            WorkflowType = workflowType,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = "test-user-id",
            Direction = direction,
            MessageType = messageType,
            Text = text,
            Status = status,
            Scope = scope
        };

        var database = await databaseService.GetDatabaseAsync();
        var collection = database.GetCollection<ConversationMessage>("conversation_message");
        await collection.InsertOneAsync(message);
        return message;
    }

    /// <summary>
    /// Reads the stored document directly, so assertions see the persisted status rather than
    /// the read-time status the repository reports.
    /// </summary>
    private async Task<ConversationMessage?> GetStoredMessageAsync(string messageId)
    {
        using var scope = _factory.Services.CreateScope();
        var database = await scope.ServiceProvider.GetRequiredService<IDatabaseService>().GetDatabaseAsync();
        return await database.GetCollection<ConversationMessage>("conversation_message")
            .Find(m => m.Id == messageId)
            .FirstOrDefaultAsync();
    }
}

