using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Coordinates pending synchronous requests between server instances.
/// </summary>
public interface IPendingRequestCoordinator
{
    event Action<string, ConversationMessage, MessageType?> CompletionReceived;

    Task AnnounceWaitAsync(
        string requestId,
        CancellationToken cancellationToken = default);

    Task PublishCompletionAsync(
        string requestId,
        ConversationMessage response,
        MessageType? messageType,
        CancellationToken cancellationToken = default);
}
