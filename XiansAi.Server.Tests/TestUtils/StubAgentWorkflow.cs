using System.Text.Json;
using Shared.Services;
using Shared.Utils;
using Temporalio.Converters;
using Temporalio.Workflows;

namespace Tests.TestUtils;

/// <summary>
/// Dynamic stub workflow used by Admin API Temporal tests. It accepts any workflow
/// type name, replies to inbound chat/heartbeat signals, and implements the HITL
/// query/signal surface used by Admin task endpoints.
/// </summary>
[Workflow(Dynamic = true)]
public class StubAgentWorkflow
{
    private bool _completed;
    private readonly Dictionary<string, object?> _taskInfo = new()
    {
        ["Title"] = "Test HITL task",
        ["Description"] = "stub task",
        ["InitialWork"] = "draft",
        ["FinalWork"] = "draft",
        ["ParticipantId"] = "worker-user",
        ["IsCompleted"] = false,
        ["TimedOut"] = false,
        ["AvailableActions"] = new[] { "approve", "reject" },
        ["PerformedAction"] = null,
        ["Comment"] = null,
        ["Metadata"] = new Dictionary<string, object>()
    };

    [WorkflowRun]
    public async Task RunAsync(IRawValue[] args)
    {
        await Workflow.WaitConditionAsync(() => _completed);
    }

    [WorkflowQuery("GetTaskInfo")]
    public IReadOnlyDictionary<string, object?> GetTaskInfo() => _taskInfo;

    [WorkflowSignal(Dynamic = true)]
    public async Task OnSignalAsync(string name, IRawValue[] args)
    {
        switch (name)
        {
            case Constants.SIGNAL_INBOUND_CHAT_OR_DATA:
                await HandleInboundAsync(args);
                break;
            case "UpdateDraft":
                if (args.Length > 0)
                {
                    _taskInfo["FinalWork"] = Workflow.PayloadConverter.ToValue<string>(args[0]);
                }
                break;
            case "UpdateMetadata":
                if (args.Length > 0)
                {
                    _taskInfo["Metadata"] = Workflow.PayloadConverter.ToValue<Dictionary<string, object>>(args[0]);
                }
                break;
            case "PerformAction":
                ApplyPerformAction(args);
                _completed = true;
                break;
        }
    }

    private async Task HandleInboundAsync(IRawValue[] args)
    {
        var requestId = ExtractRequestId(args);
        if (string.IsNullOrWhiteSpace(requestId))
        {
            return;
        }

        await Workflow.ExecuteActivityAsync(
            (StubReplyActivities activities) => activities.CompletePendingRequest(requestId),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(10) });
    }

    private void ApplyPerformAction(IRawValue[] args)
    {
        if (args.Length == 0)
        {
            return;
        }

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(Workflow.PayloadConverter.ToValue<object>(args[0])));
        var root = document.RootElement;
        _taskInfo["PerformedAction"] = ReadString(root, "Action", "action");
        _taskInfo["Comment"] = ReadString(root, "Comment", "comment");
        _taskInfo["IsCompleted"] = true;
    }

    private static string? ExtractRequestId(IRawValue[] args)
    {
        if (args.Length == 0)
        {
            return null;
        }

        var payload = Workflow.PayloadConverter.ToValue<object>(args[0]);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(payload));
        return ReadNestedRequestId(document.RootElement);
    }

    private static string? ReadNestedRequestId(JsonElement element)
    {
        var direct = ReadString(element, "RequestId", "requestId");
        if (!string.IsNullOrWhiteSpace(direct))
        {
            return direct;
        }

        if (element.ValueKind == JsonValueKind.Object &&
            (element.TryGetProperty("Payload", out var payload) || element.TryGetProperty("payload", out payload)))
        {
            return ReadString(payload, "RequestId", "requestId");
        }

        return null;
    }

    private static string? ReadString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }
}
