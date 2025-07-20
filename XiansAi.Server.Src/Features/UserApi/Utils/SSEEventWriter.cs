using System.Text;
using System.Text.Json;

namespace Features.UserApi.Utils;

/// <summary>
/// Utility class for writing Server-Sent Events to HTTP response streams
/// </summary>
public static class SSEEventWriter
{
    /// <summary>
    /// Writes a Server-Sent Event to the response stream
    /// </summary>
    /// <param name="response">The HTTP response to write to</param>
    /// <param name="eventType">The type of the SSE event</param>
    /// <param name="data">The data to serialize and send</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public static async Task WriteEventAsync(HttpResponse response, string eventType, object data, CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var sseData = $"event: {eventType}\ndata: {json}\n\n";
        var bytes = Encoding.UTF8.GetBytes(sseData);

        await response.Body.WriteAsync(bytes, cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }

    /// <summary>
    /// Sets the standard SSE headers on the HTTP response
    /// </summary>
    /// <param name="response">The HTTP response to configure</param>
    public static void SetSSEHeaders(HttpResponse response)
    {
        response.Headers.Append("Content-Type", "text/event-stream");
        response.Headers.Append("Cache-Control", "no-cache");
        response.Headers.Append("Connection", "keep-alive");
        response.Headers.Append("Access-Control-Allow-Origin", "*");
    }

    /// <summary>
    /// Creates a connection event data object
    /// </summary>
    public static object CreateConnectionEvent(string workflowId, string participantId, string tenantId)
    {
        return new
        {
            message = "Connected to message stream",
            workflowId,
            participantId,
            tenantId,
            timestamp = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Creates a heartbeat event data object
    /// </summary>
    public static object CreateHeartbeatEvent(int subscriberCount)
    {
        return new
        {
            timestamp = DateTime.UtcNow,
            subscriberCount
        };
    }
} 