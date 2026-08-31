using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Repositories;
using Shared.Services;

namespace Tests.UnitTests.Shared.Services;

public class PendingRequestServiceCrossInstanceTests
{
    [Fact]
    public void CompleteRequest_WhenNotLocal_DelegatesToCoordinator()
    {
        var coordinator = new Mock<IPendingRequestCoordinator>();
        using var service = new PendingRequestService(
            NullLogger<PendingRequestService>.Instance,
            coordinator.Object);
        var response = CreateMessage("req-1");

        service.CompleteRequest("req-1", response, MessageType.Chat);

        coordinator.Verify(value => value.PublishCompletionAsync(
            "req-1",
            response,
            MessageType.Chat,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteRequest_WhenLocal_CompletesWithoutCoordinatorPublish()
    {
        var coordinator = new Mock<IPendingRequestCoordinator>();
        coordinator
            .Setup(value => value.AnnounceWaitAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var service = new PendingRequestService(
            NullLogger<PendingRequestService>.Instance,
            coordinator.Object);
        var response = CreateMessage("req-1");
        var waitTask = service.WaitForResponseAsync<ConversationMessage>(
            "req-1",
            TimeSpan.FromSeconds(5),
            MessageType.Chat);

        service.CompleteRequest("req-1", response, MessageType.Chat);

        Assert.Same(response, await waitTask);
        coordinator.Verify(value => value.PublishCompletionAsync(
            It.IsAny<string>(),
            It.IsAny<ConversationMessage>(),
            It.IsAny<MessageType?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WaitForResponseAsync_AfterRegisteringLocally_AnnouncesWait()
    {
        var coordinator = new Mock<IPendingRequestCoordinator>();
        var announcedAfterRegistration = false;
        CancellationToken announcedToken = default;
        PendingRequestService? service = null;
        coordinator
            .Setup(value => value.AnnounceWaitAsync(
                "req-1",
                It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((_, token) =>
            {
                announcedAfterRegistration = service!.GetPendingRequestCount() == 1;
                announcedToken = token;
            })
            .Returns(Task.CompletedTask);
        using var cancellation = new CancellationTokenSource();
        using (service = new PendingRequestService(
            NullLogger<PendingRequestService>.Instance,
            coordinator.Object))
        {
            var waitTask = service.WaitForResponseAsync<ConversationMessage>(
                "req-1",
                TimeSpan.FromSeconds(5),
                MessageType.Chat,
                cancellation.Token);

            coordinator.Verify(value => value.AnnounceWaitAsync(
                "req-1",
                It.IsAny<CancellationToken>()), Times.Once);
            Assert.True(announcedAfterRegistration);

            cancellation.Cancel();
            Assert.True(announcedToken.IsCancellationRequested);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitTask);
        }
    }

    [Fact]
    public async Task CoordinatorCompletion_CompletesLocalWaiter()
    {
        var coordinator = new Mock<IPendingRequestCoordinator>();
        coordinator
            .Setup(value => value.AnnounceWaitAsync(
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        using var service = new PendingRequestService(
            NullLogger<PendingRequestService>.Instance,
            coordinator.Object);
        var response = CreateMessage("req-1");
        var waitTask = service.WaitForResponseAsync<ConversationMessage>(
            "req-1",
            TimeSpan.FromSeconds(5),
            MessageType.Chat);

        coordinator.Raise(
            value => value.CompletionReceived += null!,
            "req-1",
            response,
            MessageType.Chat);

        Assert.Same(response, await waitTask);
        coordinator.Verify(value => value.PublishCompletionAsync(
            It.IsAny<string>(),
            It.IsAny<ConversationMessage>(),
            It.IsAny<MessageType?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ConversationMessage CreateMessage(string requestId) =>
        new()
        {
            RequestId = requestId,
            MessageType = MessageType.Chat,
            ThreadId = "thread-1",
            TenantId = "tenant-1",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test",
            Direction = MessageDirection.Outgoing,
            ParticipantId = "participant-1",
            WorkflowId = "workflow-1",
            WorkflowType = "test"
        };
}
