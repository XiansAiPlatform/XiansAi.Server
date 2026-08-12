using Features.UserApi.Services;

namespace Features.UserApi.Utils;

/// <summary>
/// Utility class for filtering message events in SSE streams
/// </summary>
public static class MessageEventFilter
{
    /// <summary>
    /// Determines if a message event should be sent to a specific client.
    /// A client only ever receives messages for its own workflow, participant and
    /// tenant: matching on anything broader would stream one participant's
    /// conversation into another participant's connection.
    /// </summary>
    /// <param name="messageEvent">The message event to filter</param>
    /// <param name="expectedGroupId">Expected participant group ID for the client</param>
    /// <param name="tenantId">Expected tenant ID</param>
    /// <param name="scope">Optional scope filter</param>
    /// <returns>True if the message should be sent to the client</returns>
    public static bool ShouldSendMessage(
        MessageStreamEvent messageEvent,
        string expectedGroupId,
        string tenantId,
        string? scope = null)
    {
        if (messageEvent?.Message == null)
        {
            return false;
        }

        var message = messageEvent.Message;

        var messageMatches = messageEvent.GroupId == expectedGroupId &&
                             message.TenantId == tenantId;

        // Apply scope filter if provided
        if (!string.IsNullOrEmpty(scope))
        {
            messageMatches = messageMatches && message.Scope == scope;
        }

        return messageMatches;
    }

    /// <summary>
    /// Creates a message event data object for SSE transmission
    /// </summary>
    /// <param name="message">The message to convert</param>
    /// <returns>Anonymous object with message data</returns>
    public static object CreateMessageEventData(dynamic message)
    {
        return new
        {
            id = message.Id,
            threadId = message.ThreadId,
            workflowId = message.WorkflowId,
            workflowType = message.WorkflowType,
            participantId = message.ParticipantId,
            direction = message.Direction.ToString(),
            messageType = message.MessageType?.ToString(),
            text = message.Text,
            data = message.Data,
            hint = message.Hint,
            scope = message.Scope,
            requestId = message.RequestId,
            createdAt = message.CreatedAt,
            createdBy = message.CreatedBy
        };
    }
} 