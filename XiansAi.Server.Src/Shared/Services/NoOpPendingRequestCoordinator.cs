using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Keeps pending request coordination local to the current process.
/// </summary>
public sealed class NoOpPendingRequestCoordinator : IPendingRequestCoordinator
{
    public event Action<string, ConversationMessage, MessageType?> CompletionReceived
    {
        add { }
        remove { }
    }

    public Task AnnounceWaitAsync(
        string requestId,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task PublishCompletionAsync(
        string requestId,
        ConversationMessage response,
        MessageType? messageType,
        CancellationToken cancellationToken = default) =>
        Task.CompletedTask;
}
