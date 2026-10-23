using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AgentApi;

/// <summary>
/// The participant on a signal-with-start request becomes the user id of a workflow it starts.
/// A heartbeat ping must not start a workflow as the "heartbeat" pseudo-user.
/// </summary>
public class MessageSignalParticipantTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    public MessageSignalParticipantTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task Heartbeat_DoesNotPassParticipantAsWorkflowUser()
    {
        var signalled = await ProcessAndCaptureSignalAsync("heartbeat", MessageType.Heartbeat);

        Assert.Null(signalled.ParticipantId);
    }

    [Fact]
    public async Task Chat_PassesParticipantAsWorkflowUser()
    {
        var participantId = $"chat-{Guid.NewGuid()}@example.com";

        var signalled = await ProcessAndCaptureSignalAsync(participantId, MessageType.Chat);

        Assert.Equal(participantId, signalled.ParticipantId);
    }

    private async Task<WorkflowSignalWithStartRequest> ProcessAndCaptureSignalAsync(
        string participantId,
        MessageType messageType)
    {
        using var scope = _factory.Services.CreateScope();

        WorkflowSignalWithStartRequest? captured = null;
        var workflowSignal = new Mock<IWorkflowSignalService>();
        workflowSignal
            .Setup(signal => signal.SignalWithStartWorkflow(It.IsAny<WorkflowSignalWithStartRequest>()))
            .Callback<WorkflowSignalWithStartRequest>(request => captured = request)
            .ReturnsAsync(Results.Ok());

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Messaging:ValidateActivation"] = "false"
            })
            .Build();

        var messageService = new MessageService(
            scope.ServiceProvider.GetRequiredService<ILogger<MessageService>>(),
            scope.ServiceProvider.GetRequiredService<Shared.Auth.ITenantContext>(),
            scope.ServiceProvider.GetRequiredService<IConversationRepository>(),
            workflowSignal.Object,
            scope.ServiceProvider.GetRequiredService<IFeedbackService>(),
            scope.ServiceProvider.GetRequiredService<IMessageFileStorage>(),
            scope.ServiceProvider.GetRequiredService<IActivationValidationService>(),
            configuration,
            scope.ServiceProvider.GetRequiredService<IIncomingOriginCache>());

        var result = await messageService.ProcessIncomingMessage(new ChatOrDataRequest
        {
            ParticipantId = participantId,
            WorkflowId = $"{TestTenantId}:SignalUserAgent:Supervisor:{Guid.NewGuid()}",
            WorkflowType = "SignalUserAgent:Supervisor",
            Text = "ping",
            Origin = "test"
        }, messageType);

        Assert.True(result.IsSuccess, result.ErrorMessage);
        Assert.NotNull(captured);
        return captured!;
    }
}
