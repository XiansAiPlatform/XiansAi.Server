using Features.UserApi.Services;

namespace Features.UserApi.Utils;

/// <summary>
/// Utility class for filtering message events in SSE streams
/// </summary>
public static class MessageEventFilter
{
    /// <summary>
    /// Determines if a message event should be sent to a specific client
    /// </summary>
    /// <param name="messageEvent">The message event to filter</param>
    /// <param name="expectedGroupId">Expected group ID for the client</param>
    /// <param name="expectedTenantGroupId">Expected tenant group ID for the client</param>
    /// <param name="tenantId">Expected tenant ID</param>
    /// <param name="scope">Optional scope filter</param>
    /// <returns>True if the message should be sent to the client</returns>
    public static bool ShouldSendMessage(
        MessageStreamEvent messageEvent, 
        string expectedGroupId, 
        string expectedTenantGroupId, 
        string tenantId, 
        string? scope = null)
    {
        var message = messageEvent.Message;

        // Filter messages for this specific workflow, participant, and tenant
        var messageMatches = (messageEvent.GroupId == expectedGroupId ||
                            messageEvent.TenantGroupId == expectedTenantGroupId) &&
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