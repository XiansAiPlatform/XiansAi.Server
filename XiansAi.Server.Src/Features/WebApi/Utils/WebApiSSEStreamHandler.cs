using Features.UserApi.Services;
using Features.UserApi.Utils;
using Shared.Auth;

namespace Features.WebApi.Utils;

/// <summary>
/// Handles Server-Sent Events stream connections for thread-based real-time messaging in WebAPI
/// </summary>
public class WebApiSSEStreamHandler
{
    private readonly IMessageEventPublisher _messageEventPublisher;
    private readonly ILogger _logger;
    private readonly HttpContext _httpContext;
    private readonly string _threadId;
    private readonly string _tenantId;
    private readonly CancellationToken _cancellationToken;
    private readonly TimeSpan _heartbeatInterval;

    private Timer? _heartbeatTimer;
    private TaskCompletionSource<bool>? _completionSource;

    public WebApiSSEStreamHandler(
        IMessageEventPublisher messageEventPublisher,
        ILogger logger,
        HttpContext httpContext,
        string threadId,
        ITenantContext tenantContext,
        CancellationToken cancellationToken = default,
        TimeSpan? heartbeatInterval = null)
    {
        _messageEventPublisher = messageEventPublisher;
        _logger = logger;
        _httpContext = httpContext;
        _threadId = threadId;
        _tenantId = tenantContext.TenantId;
        // Use the explicit cancellation token if provided, otherwise fall back to HttpContext.RequestAborted
        _cancellationToken = cancellationToken != default ? cancellationToken : httpContext.RequestAborted;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>
    /// Starts the SSE stream and handles the complete lifecycle
    /// </summary>
    public async Task<IResult> HandleStreamAsync()
    {
        try
        {
            _logger.LogInformation("WebAPI SSE connection established for thread {ThreadId}, tenant {TenantId}",
                _threadId, _tenantId);

            // Set SSE headers
            SSEEventWriter.SetSSEHeaders(_httpContext.Response);

            // Send initial connection event
            await SSEEventWriter.WriteEventAsync(_httpContext.Response, "connected", 
                CreateConnectionEvent(), 
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

            // Filter messages for this specific thread and tenant
            if (message.ThreadId != _threadId || message.TenantId != _tenantId)
            {
                return;
            }

            if (!_cancellationToken.IsCancellationRequested)
            {
                var eventType = message.MessageType?.ToString() ?? "unknown";
                _logger.LogDebug("Sending WebAPI SSE event {EventType} for message {MessageId} to thread {ThreadId}",
                        eventType, message.Id, _threadId);

                await SSEEventWriter.WriteEventAsync(_httpContext.Response, eventType, 
                    MessageEventFilter.CreateMessageEventData(message), 
                    _cancellationToken);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException || _cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("WebAPI SSE connection cancelled for thread {ThreadId}", _threadId);
            _completionSource?.TrySetResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling WebAPI SSE message event for thread {ThreadId}", _threadId);
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

    private object CreateConnectionEvent()
    {
        return new
        {
            message = "Connected to thread message stream",
            threadId = _threadId,
            tenantId = _tenantId,
            timestamp = DateTime.UtcNow
        };
    }

    private void Cleanup()
    {
        _heartbeatTimer?.Dispose();
        _messageEventPublisher.MessageReceived -= HandleMessage;
        
        _logger.LogInformation("WebAPI SSE connection closed for thread {ThreadId}, tenant {TenantId}",
            _threadId, _tenantId);
    }
}



