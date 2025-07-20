using System.Collections.Concurrent;
using Shared.Repositories;

namespace Features.UserApi.Services;

/// <summary>
/// Event data for message stream events
/// </summary>
public class MessageStreamEvent
{
    public required ConversationMessage Message { get; set; }
    public required string GroupId { get; set; }
    public required string TenantGroupId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Interface for publishing message stream events to SSE subscribers
/// </summary>
public interface IMessageEventPublisher
{
    event Func<MessageStreamEvent, Task>? MessageReceived;
    Task PublishMessageAsync(MessageStreamEvent messageEvent, CancellationToken cancellationToken = default);
    int GetSubscriberCount();
}

/// <summary>
/// Service for publishing message events to SSE subscribers
/// Thread-safe publisher that distributes messages to multiple subscribers
/// </summary>
public class MessageEventPublisher : IMessageEventPublisher
{
    private readonly ILogger<MessageEventPublisher> _logger;
    private readonly ConcurrentDictionary<string, Func<MessageStreamEvent, Task>> _subscribers = new();

    public MessageEventPublisher(ILogger<MessageEventPublisher> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public event Func<MessageStreamEvent, Task>? MessageReceived;

    public async Task PublishMessageAsync(MessageStreamEvent messageEvent, CancellationToken cancellationToken = default)
    {
        if (messageEvent?.Message == null)
        {
            _logger.LogWarning("Attempted to publish null message event");
            return;
        }

        // Check for cancellation before processing
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // Publish to event subscribers
            var messageReceived = MessageReceived;
            if (messageReceived != null)
            {
                var tasks = messageReceived.GetInvocationList()
                    .Cast<Func<MessageStreamEvent, Task>>()
                    .Select(handler => SafeInvokeHandler(handler, messageEvent, cancellationToken))
                    .ToArray();

                await Task.WhenAll(tasks);
            }

            _logger.LogDebug("Published message event for workflow {WorkflowId}, participant {ParticipantId}, direction {Direction}",
                messageEvent.Message.WorkflowId, messageEvent.Message.ParticipantId, messageEvent.Message.Direction);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Message publishing cancelled for message {MessageId}", messageEvent.Message.Id);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing message event for message {MessageId}", messageEvent.Message.Id);
        }
    }

    public int GetSubscriberCount()
    {
        return MessageReceived?.GetInvocationList()?.Length ?? 0;
    }

    private async Task SafeInvokeHandler(Func<MessageStreamEvent, Task> handler, MessageStreamEvent messageEvent, CancellationToken cancellationToken)
    {
        try
        {
            await handler(messageEvent);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Message handler cancelled for message {MessageId}", messageEvent.Message.Id);
            // Don't rethrow cancellation exceptions from individual handlers
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in message event handler for message {MessageId}", messageEvent.Message.Id);
        }
    }
} 