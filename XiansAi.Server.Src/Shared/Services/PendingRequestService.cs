using System.Collections.Concurrent;

namespace Shared.Services;

/// <summary>
/// Service for managing pending synchronous requests that wait for async responses
/// </summary>
public interface IPendingRequestService
{
    Task<T?> WaitForResponseAsync<T>(string requestId, TimeSpan timeout, CancellationToken cancellationToken = default);
    void CompleteRequest<T>(string requestId, T response);
    void CompleteRequestWithError(string requestId, Exception exception);
    void CancelRequest(string requestId);
    int GetPendingRequestCount();
}

public class PendingRequestService : IPendingRequestService, IDisposable
{
    private readonly ConcurrentDictionary<string, PendingRequest> _pendingRequests = new();
    private readonly ILogger<PendingRequestService> _logger;
    private readonly Timer _cleanupTimer;

    public PendingRequestService(ILogger<PendingRequestService> logger)
    {
        _logger = logger;
        
        // Clean up expired requests every 30 seconds
        _cleanupTimer = new Timer(CleanupExpiredRequests, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<T?> WaitForResponseAsync<T>(string requestId, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(requestId))
            throw new ArgumentException("RequestId cannot be null or empty", nameof(requestId));

        var pendingRequest = new PendingRequest<T>(requestId, DateTime.UtcNow.Add(timeout));
        
        if (!_pendingRequests.TryAdd(requestId, pendingRequest))
        {
            _logger.LogWarning("Request with ID {RequestId} already exists", requestId);
            return default(T);
        }

        try
        {
            _logger.LogDebug("Waiting for response to request {RequestId} with timeout {Timeout}", requestId, timeout);
            
            using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            combinedCts.CancelAfter(timeout);
            
            var response = await pendingRequest.TaskCompletionSource.Task.WaitAsync(combinedCts.Token);
            _logger.LogDebug("Received response for request {RequestId}", requestId);
            
            return response;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug("Request {RequestId} was cancelled by caller", requestId);
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Request {RequestId} timed out after {Timeout}", requestId, timeout);
            throw new TimeoutException($"Request {requestId} timed out after {timeout}");
        }
        finally
        {
            _pendingRequests.TryRemove(requestId, out _);
        }
    }

    public void CompleteRequest<T>(string requestId, T response)
    {
        if (string.IsNullOrEmpty(requestId))
        {
            _logger.LogWarning("Cannot complete request with null or empty RequestId");
            return;
        }

        if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
        {
            if (pendingRequest is PendingRequest<T> typedRequest)
            {
                _logger.LogDebug("Completing request {RequestId} with response", requestId);
                typedRequest.TaskCompletionSource.SetResult(response);
            }
            else
            {
                _logger.LogWarning("Type mismatch when completing request {RequestId}. Expected {ExpectedType}, got {ActualType}", 
                    requestId, typeof(T).Name, pendingRequest.GetType().GetGenericArguments().FirstOrDefault()?.Name ?? "unknown");
            }
        }
        else
        {
            _logger.LogDebug("No pending request found for RequestId {RequestId} - may have already completed or timed out", requestId);
        }
    }

    public void CompleteRequestWithError(string requestId, Exception exception)
    {
        if (string.IsNullOrEmpty(requestId))
        {
            _logger.LogWarning("Cannot complete request with error - null or empty RequestId");
            return;
        }

        if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
        {
            _logger.LogDebug("Completing request {RequestId} with error: {Error}", requestId, exception.Message);
            pendingRequest.SetException(exception);
        }
        else
        {
            _logger.LogDebug("No pending request found for RequestId {RequestId} when setting error", requestId);
        }
    }

    public void CancelRequest(string requestId)
    {
        if (string.IsNullOrEmpty(requestId))
        {
            _logger.LogWarning("Cannot cancel request with null or empty RequestId");
            return;
        }

        if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
        {
            _logger.LogDebug("Cancelling request {RequestId}", requestId);
            pendingRequest.SetCanceled();
        }
    }

    public int GetPendingRequestCount()
    {
        return _pendingRequests.Count;
    }

    private void CleanupExpiredRequests(object? state)
    {
        var now = DateTime.UtcNow;
        var expiredRequests = new List<string>();

        foreach (var kvp in _pendingRequests)
        {
            if (kvp.Value.ExpiresAt < now)
            {
                expiredRequests.Add(kvp.Key);
            }
        }

        foreach (var requestId in expiredRequests)
        {
            if (_pendingRequests.TryRemove(requestId, out var pendingRequest))
            {
                _logger.LogDebug("Cleaning up expired request {RequestId}", requestId);
                pendingRequest.SetException(new TimeoutException($"Request {requestId} expired"));
            }
        }

        if (expiredRequests.Count > 0)
        {
            _logger.LogDebug("Cleaned up {Count} expired requests", expiredRequests.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        
        // Cancel all pending requests
        foreach (var kvp in _pendingRequests)
        {
            kvp.Value.SetCanceled();
        }
        _pendingRequests.Clear();
    }
}

// Base class for pending requests
public abstract class PendingRequest
{
    public string RequestId { get; }
    public DateTime ExpiresAt { get; }

    protected PendingRequest(string requestId, DateTime expiresAt)
    {
        RequestId = requestId;
        ExpiresAt = expiresAt;
    }

    public abstract void SetException(Exception exception);
    public abstract void SetCanceled();
}

// Typed pending request
public class PendingRequest<T> : PendingRequest
{
    public TaskCompletionSource<T> TaskCompletionSource { get; }

    public PendingRequest(string requestId, DateTime expiresAt) : base(requestId, expiresAt)
    {
        TaskCompletionSource = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override void SetException(Exception exception)
    {
        TaskCompletionSource.TrySetException(exception);
    }

    public override void SetCanceled()
    {
        TaskCompletionSource.TrySetCanceled();
    }
} 