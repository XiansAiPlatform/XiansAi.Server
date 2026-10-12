using System.Text.Json;
using Features.UserApi.Services;
using Features.UserApi.Utils;
using Shared.Repositories;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Features.UserApi.Utils;

public class MessageEventFilterTests
{
    private const string TenantId = "acme";
    private const string WorkflowId = "acme:Sales Agent:Sales Flow";
    private const string ParticipantId = "alice@acme.com";
    private const string OtherParticipantId = "bob@acme.com";

    [Fact]
    public void ShouldSendMessage_SendsMessageAddressedToTheConnectedParticipant()
    {
        var messageEvent = BuildEvent(WorkflowId, ParticipantId, TenantId);

        var shouldSend = MessageEventFilter.ShouldSendMessage(
            messageEvent,
            MessageGroupKey.ForParticipant(WorkflowId, ParticipantId, TenantId),
            TenantId);

        Assert.True(shouldSend);
    }

    [Fact]
    public void ShouldSendMessage_DoesNotLeakAnotherParticipantOnTheSameWorkflow()
    {
        // Regression: every SSE connection used to also match a tenant-wide key that
        // carried no participant, so one participant's replies reached all connections
        // listening to the same workflow.
        var messageEvent = BuildEvent(WorkflowId, OtherParticipantId, TenantId);

        var shouldSend = MessageEventFilter.ShouldSendMessage(
            messageEvent,
            MessageGroupKey.ForParticipant(WorkflowId, ParticipantId, TenantId),
            TenantId);

        Assert.False(shouldSend);
    }

    [Fact]
    public void ShouldSendMessage_DoesNotLeakAnotherParticipantWhenScopeIsAbsent()
    {
        // Scope is optional, so it must not be what keeps participants apart.
        var messageEvent = BuildEvent(WorkflowId, OtherParticipantId, TenantId, scope: null);

        var shouldSend = MessageEventFilter.ShouldSendMessage(
            messageEvent,
            MessageGroupKey.ForParticipant(WorkflowId, ParticipantId, TenantId),
            TenantId,
            scope: null);

        Assert.False(shouldSend);
    }

    [Fact]
    public void ShouldSendMessage_DoesNotSendMessageFromAnotherTenant()
    {
        var messageEvent = BuildEvent(WorkflowId, ParticipantId, "other-tenant");

        var shouldSend = MessageEventFilter.ShouldSendMessage(
            messageEvent,
            MessageGroupKey.ForParticipant(WorkflowId, ParticipantId, TenantId),
            TenantId);

        Assert.False(shouldSend);
    }

    [Fact]
    public void ShouldSendMessage_DoesNotSendMessageFromAnotherWorkflow()
    {
        var messageEvent = BuildEvent("acme:Support Agent:Support Flow", ParticipantId, TenantId);

        var shouldSend = MessageEventFilter.ShouldSendMessage(
            messageEvent,
            MessageGroupKey.ForParticipant(WorkflowId, ParticipantId, TenantId),
            TenantId);

        Assert.False(shouldSend);
    }

    [Fact]
    public void CreateMessageEventData_ReturnsStoredStatusWithoutRemappingUserMessages()
    {
        var userMessage = BuildEvent(WorkflowId, ParticipantId, TenantId).Message;
        userMessage.Direction = MessageDirection.Incoming;
        userMessage.MessageType = MessageType.Chat;
        userMessage.Status = MessageStatus.Read;

        var agentMessage = BuildEvent(WorkflowId, ParticipantId, TenantId).Message;
        agentMessage.Direction = MessageDirection.Outgoing;
        agentMessage.MessageType = MessageType.Chat;
        agentMessage.Status = MessageStatus.Unread;

        using var userJson = JsonDocument.Parse(JsonSerializer.Serialize(MessageEventFilter.CreateMessageEventData(userMessage)));
        using var agentJson = JsonDocument.Parse(JsonSerializer.Serialize(MessageEventFilter.CreateMessageEventData(agentMessage)));

        Assert.Equal("Read", userJson.RootElement.GetProperty("status").GetString());
        Assert.Equal("Incoming", userJson.RootElement.GetProperty("direction").GetString());
        Assert.Equal("Unread", agentJson.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public void ShouldSendMessage_AppliesScopeFilterWhenScopeRequested()
    {
        var messageEvent = BuildEvent(WorkflowId, ParticipantId, TenantId, scope: "orders");

        var expectedGroupId = MessageGroupKey.ForParticipant(WorkflowId, ParticipantId, TenantId);

        Assert.True(MessageEventFilter.ShouldSendMessage(messageEvent, expectedGroupId, TenantId, "orders"));
        Assert.False(MessageEventFilter.ShouldSendMessage(messageEvent, expectedGroupId, TenantId, "billing"));
    }

    private static MessageStreamEvent BuildEvent(
        string workflowId,
        string participantId,
        string tenantId,
        string? scope = null)
    {
        var message = new ConversationMessage
        {
            Id = Guid.NewGuid().ToString(),
            ThreadId = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            WorkflowId = workflowId,
            WorkflowType = "Sales Flow",
            ParticipantId = participantId,
            Direction = MessageDirection.Outgoing,
            MessageType = MessageType.Chat,
            Text = "agent reply",
            Scope = scope,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "agent"
        };

        return new MessageStreamEvent
        {
            Message = message,
            GroupId = MessageGroupKey.ForParticipant(workflowId, participantId, tenantId)
        };
    }
}
