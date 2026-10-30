using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Moq;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AgentApi;

public class ConversationMessageReadStatusTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public ConversationMessageReadStatusTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task CreatedMessages_StoreUnreadOnlyForOutgoingChatAndFile()
    {
        using var scope = _factory.Services.CreateScope();
        var messageService = CreateMessageService(scope);

        var incomingChat = await SaveIncomingAsync(messageService, MessageType.Chat);
        var incomingFile = await SaveIncomingAsync(messageService, MessageType.File);
        var outgoingChat = await SaveOutgoingAsync(messageService, MessageType.Chat);
        var outgoingFile = await SaveOutgoingAsync(messageService, MessageType.File);
        var outgoingTool = await SaveOutgoingAsync(messageService, MessageType.Tool);
        var outgoingReasoning = await SaveOutgoingAsync(messageService, MessageType.Reasoning);

        Assert.Equal(MessageStatus.Read, (await FindStoredAsync(incomingChat, MessageDirection.Incoming, MessageType.Chat)).Status);
        Assert.Equal(MessageStatus.Read, (await FindStoredAsync(incomingFile, MessageDirection.Incoming, MessageType.File)).Status);
        Assert.Equal(MessageStatus.Unread, (await FindStoredAsync(outgoingChat, MessageDirection.Outgoing, MessageType.Chat)).Status);
        Assert.Equal(MessageStatus.Unread, (await FindStoredAsync(outgoingFile, MessageDirection.Outgoing, MessageType.File)).Status);
        Assert.Equal(MessageStatus.Read, (await FindStoredAsync(outgoingTool, MessageDirection.Outgoing, MessageType.Tool)).Status);
        Assert.Equal(MessageStatus.Read, (await FindStoredAsync(outgoingReasoning, MessageDirection.Outgoing, MessageType.Reasoning)).Status);
    }

    private static MessageService CreateMessageService(IServiceScope scope)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:ValidateActivation"] = "false"
            })
            .Build();

        var workflowSignal = new Mock<IWorkflowSignalService>();
        workflowSignal
            .Setup(signal => signal.SignalWithStartWorkflow(It.IsAny<WorkflowSignalWithStartRequest>()))
            .ReturnsAsync(Results.Ok());

        return new MessageService(
            scope.ServiceProvider.GetRequiredService<ILogger<MessageService>>(),
            scope.ServiceProvider.GetRequiredService<Shared.Auth.ITenantContext>(),
            scope.ServiceProvider.GetRequiredService<IConversationRepository>(),
            workflowSignal.Object,
            scope.ServiceProvider.GetRequiredService<IFeedbackService>(),
            scope.ServiceProvider.GetRequiredService<IMessageFileStorage>(),
            scope.ServiceProvider.GetRequiredService<IActivationValidationService>(),
            configuration,
            scope.ServiceProvider.GetRequiredService<IIncomingOriginCache>());
    }

    private static async Task<string> SaveIncomingAsync(MessageService messageService, MessageType messageType)
    {
        var participantId = NewParticipantId();
        var result = await messageService.ProcessIncomingMessage(NewRequest(participantId), messageType);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return participantId;
    }

    private static async Task<string> SaveOutgoingAsync(MessageService messageService, MessageType messageType)
    {
        var participantId = NewParticipantId();
        var result = await messageService.ProcessOutgoingMessage(NewRequest(participantId), messageType);
        Assert.True(result.IsSuccess, result.ErrorMessage);
        return participantId;
    }

    private static ChatOrDataRequest NewRequest(string participantId)
    {
        return new ChatOrDataRequest
        {
            ParticipantId = participantId,
            WorkflowId = $"{TestTenantId}:ReadStatusAgent:Chat:{Guid.NewGuid()}",
            WorkflowType = "ReadStatusAgent:Chat",
            Text = "status check",
            Origin = "test",
            Data = new { source = "test" }
        };
    }

    private static string NewParticipantId() => $"reader-{Guid.NewGuid()}@example.com";

    private async Task<ConversationMessage> FindStoredAsync(
        string participantId,
        MessageDirection direction,
        MessageType messageType)
    {
        var message = await _database.GetCollection<ConversationMessage>("conversation_message")
            .Find(m => m.ParticipantId == participantId && m.Direction == direction && m.MessageType == messageType)
            .FirstOrDefaultAsync();

        Assert.NotNull(message);
        return message!;
    }
}
