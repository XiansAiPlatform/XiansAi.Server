using Features.UserApi.Services;
using Shared.Auth;

namespace Features.UserApi.Utils;

/// <summary>
/// Handles Server-Sent Events stream connections for real-time messaging
/// </summary>
public class SSEStreamHandler
{
    private readonly IMessageEventPublisher _messageEventPublisher;
    private readonly ILogger _logger;
    private readonly HttpContext _httpContext;
    private readonly string _workflowId;
    private readonly string _participantId;
    private readonly string _tenantId;
    private readonly string? _scope;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan _heartbeatInterval;

    private readonly string _expectedGroupId;
    private readonly string _expectedTenantGroupId;
    private Timer? _heartbeatTimer;
    private TaskCompletionSource<bool>? _completionSource;

    public SSEStreamHandler(
        IMessageEventPublisher messageEventPublisher,
        ILogger logger,
        HttpContext httpContext,
        string workflow,
        string participantId,
        ITenantContext tenantContext,
        string? scope = null,
        CancellationToken cancellationToken = default,
        TimeSpan? heartbeatInterval = null)
    {
        _messageEventPublisher = messageEventPublisher;
        _logger = logger;
        _httpContext = httpContext;
        _workflowId = new WorkflowIdentifier(workflow, tenantContext).WorkflowId;
        _participantId = participantId;
        _tenantId = tenantContext.TenantId;
        _scope = scope;
        // Use the explicit cancellation token if provided, otherwise fall back to HttpContext.RequestAborted
        _cancellationToken = cancellationToken != default ? cancellationToken : httpContext.RequestAborted;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);

        _expectedGroupId = _workflowId + _participantId + _tenantId;
        _expectedTenantGroupId = _workflowId + _tenantId;
    }

    /// <summary>
    /// Starts the SSE stream and handles the complete lifecycle
    /// </summary>
    public async Task<IResult> HandleStreamAsync()
    {
        try
        {
            _logger.LogInformation("SSE connection established for workflow {WorkflowId}, participant {ParticipantId}, tenant {TenantId}",
                _workflowId, _participantId, _tenantId);

            // Set SSE headers
            SSEEventWriter.SetSSEHeaders(_httpContext.Response);

            // Send initial connection event
            await SSEEventWriter.WriteEventAsync(_httpContext.Response, "connected", 
                SSEEventWriter.CreateConnectionEvent(_workflowId, _participantId, _tenantId), 
                _cancellationToken);

            // Initialize completion source and subscribe to events
            _completionSource = new TaskCompletionSource<bool>();
            _cancellationToken.Register(() => _completionSource.TrySetCanceled());

            _messageEventPublisher.MessageReceived += HandleMessage;

            // Start heartbeat timer
            StartHeartbeatTimer();

            // Wait until the client disconnects or cancellation is requested
            await _completionSource.Task;

            return Results.Empty;
        }
        finally
        {
            Cleanup();
        }
    }

    private async Task HandleMessage(MessageStreamEvent messageEvent)
    {
        try
        {
            var message = messageEvent.Message;

            // Filter messages for this specific client
            if (!MessageEventFilter.ShouldSendMessage(messageEvent, _expectedGroupId, _expectedTenantGroupId, _tenantId, _scope))
            {
                return;
            }

            if (!_cancellationToken.IsCancellationRequested)
            {
                var eventType = message.MessageType?.ToString() ?? "unknown";
                _logger.LogDebug("Sending SSE event {EventType} for message {MessageId} to workflow {WorkflowId}, participant {ParticipantId}",
                        eventType, message.Id, _workflowId, _participantId);

                await SSEEventWriter.WriteEventAsync(_httpContext.Response, eventType, 
                    MessageEventFilter.CreateMessageEventData(message), 
                    _cancellationToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || _cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("SSE connection cancelled for workflow {WorkflowId}, participant {ParticipantId}", _workflowId, _participantId);
            _completionSource?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling SSE message event for workflow {WorkflowId}, participant {ParticipantId}", _workflowId, _participantId);
        }
    }

    private void StartHeartbeatTimer()
    {
        _heartbeatTimer = new Timer(async _ =>
        {
            if (!_cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await SSEEventWriter.WriteEventAsync(_httpContext.Response, "heartbeat", 
                        SSEEventWriter.CreateHeartbeatEvent(_messageEventPublisher.GetSubscriberCount()), 
                        _cancellationToken);
                }
                catch (Exception)
                {
                    // Heartbeat failed, connection likely closed
                    _completionSource?.TrySetResult(true);
                }
            }
        }, null, _heartbeatInterval, _heartbeatInterval);
    }

    private void Cleanup()
    {
        _heartbeatTimer?.Dispose();
        _messageEventPublisher.MessageReceived -= HandleMessage;
        
        _logger.LogInformation("SSE connection closed for workflow {WorkflowId}, participant {ParticipantId}, tenant {TenantId}",
            _workflowId, _participantId, _tenantId);
    }
} 