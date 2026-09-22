using Shared.Repositories;
using Shared.Services;
using Temporalio.Activities;

namespace Tests.TestUtils;

/// <summary>
/// Completes Admin API sync waits (heartbeat) from a Temporal activity running
/// in the test process, using the same <see cref="IPendingRequestService"/> as the host.
/// </summary>
public sealed class StubReplyActivities
{
    private readonly IPendingRequestService _pendingRequests;

    public StubReplyActivities(IPendingRequestService pendingRequests)
    {
        _pendingRequests = pendingRequests;
    }

    [Activity]
    public void CompletePendingRequest(string requestId)
    {
        _pendingRequests.CompleteRequest(
            requestId,
            new ConversationMessage
            {
                ThreadId = "stub-thread",
                TenantId = "stub-tenant",
                CreatedAt = DateTime.UtcNow,
                CreatedBy = "stub-worker",
                Direction = MessageDirection.Outgoing,
                ParticipantId = "heartbeat",
                WorkflowId = "stub-workflow",
                WorkflowType = "stub-type",
                RequestId = requestId,
                Text = "available",
                MessageType = MessageType.Data
            },
            MessageType.Data);
    }
}
