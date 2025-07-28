using System.Text.Json;
using Shared.Utils.Temporal;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.History.V1;
using Temporalio.Client;
using Shared.Services;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

public record WorkflowActivityEvent
{
    public long ID { get; init; }
    public string? ActivityName { get; init; }
    public string? StartedTime { get; init; }
    public string? EndedTime { get; init; }
    public object[]? Inputs { get; init; }
    public string? Result { get; init; }
    public string? ActivityId { get; init; }
}

public interface IWorkflowEventsService
{
    IResult StreamWorkflowEvents(string? workflowId);
    Task<ServiceResult<List<WorkflowActivityEvent>>> GetWorkflowEvents(string? workflowId);
}

public class WorkflowEventsService : IWorkflowEventsService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<WorkflowEventsService> _logger;

    private readonly Dictionary<long, HistoryEvent> _scheduledEvents = new();
    private readonly Dictionary<long, HistoryEvent> _startedEvents = new();

    public WorkflowEventsService(
        ITemporalClientFactory clientFactory,
        ILogger<WorkflowEventsService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IResult StreamWorkflowEvents(string? workflowId)
    {
        if (string.IsNullOrEmpty(workflowId))
        {
            return Results.BadRequest("WorkflowId is required");
        }

        return Results.Stream(async stream =>
        {
            try
            {
                var client = await _clientFactory.GetClientAsync();
                var handle = client.GetWorkflowHandle(workflowId);
                var options = new WorkflowHistoryEventFetchOptions
                {
                    WaitNewEvent = true,
                    EventFilterType = HistoryEventFilterType.AllEvent
                };

                await foreach (var historyEvent in handle.FetchHistoryEventsAsync(options))
                {
                    // Convert the event to a WorkflowActivityEvent if it's an activity event
                    var activityEvent = ConvertToActivityEvent(historyEvent);
                    if (activityEvent != null)
                    {
                        _logger.LogInformation("Sending activity event: {ActivityEvent}", activityEvent);
                        await JsonSerializer.SerializeAsync(stream, activityEvent);
                        await stream.WriteAsync("\n"u8.ToArray());
                        await stream.FlushAsync();
                        if (historyEvent.EventType == EventType.WorkflowExecutionCompleted)
                        {
                            _logger.LogInformation("Workflow completed, clearing event caches");
                            _startedEvents.Clear();
                            _scheduledEvents.Clear();
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error streaming workflow events for workflow {WorkflowId}", workflowId);
            }
        }, "text/event-stream");
    }

    private WorkflowActivityEvent? ConvertToActivityEvent(HistoryEvent evt)
    {
        switch (evt.EventType)
        {
            case EventType.ActivityTaskScheduled:
               _scheduledEvents[evt.EventId] = evt;
               return null;
            case EventType.ActivityTaskStarted:
               _startedEvents[evt.EventId] = evt;
               return null;
            case EventType.WorkflowExecutionStarted:
                return new WorkflowActivityEvent
                {
                    ID = evt.EventId,
                    ActivityName = "Flow Started",
                    StartedTime = evt.EventTime?.ToDateTime().ToString("o"),
                    Inputs = evt.WorkflowExecutionStartedEventAttributes.Input?.Payloads_.Select(p => p.Data.ToStringUtf8()).ToArray()
                };
            case EventType.WorkflowExecutionCompleted:
                return new WorkflowActivityEvent
                {
                    ID = evt.EventId,
                    ActivityName = "Flow Completed",
                    StartedTime = evt.EventTime?.ToDateTime().ToString("o"),
                    Result = evt.WorkflowExecutionCompletedEventAttributes.Result?.Payloads_.FirstOrDefault()?.Data.ToStringUtf8()
                };
            case EventType.ActivityTaskCompleted:
                var completedAttrs = evt.ActivityTaskCompletedEventAttributes;
                var startedEvent = _startedEvents[completedAttrs.StartedEventId];
                var scheduledEvent = _scheduledEvents[completedAttrs.ScheduledEventId];
                
                var activityEvent = new WorkflowActivityEvent
                {
                    ID = evt.EventId,
                    ActivityId = scheduledEvent.ActivityTaskScheduledEventAttributes.ActivityId,
                    ActivityName = scheduledEvent.ActivityTaskScheduledEventAttributes.ActivityType.Name,
                    StartedTime = startedEvent.EventTime?.ToDateTime().ToString("o"),
                    EndedTime = evt.EventTime?.ToDateTime().ToString("o"),
                    Inputs = scheduledEvent.ActivityTaskScheduledEventAttributes.Input?.Payloads_.Select(p => p.Data.ToStringUtf8()).ToArray(),
                    Result = completedAttrs.Result?.Payloads_.FirstOrDefault()?.Data.ToStringUtf8()
                };
                return activityEvent;
            default:
                return null;
        }
    }

    /**
    curl -X GET "http://localhost:5257/api/workflows/ProspectingWorkflow-79b8f89d-2c00-43a7-a3aa-50292ffdc65d/events"
    */
    public async Task<ServiceResult<List<WorkflowActivityEvent>>> GetWorkflowEvents(string? workflowId)
    {
        if (string.IsNullOrEmpty(workflowId))
        {
            _logger.LogWarning("Attempted to get events with empty workflow ID");
            return ServiceResult<List<WorkflowActivityEvent>>.BadRequest("WorkflowId is required");
        }
        
        try
        {
            var client = await _clientFactory.GetClientAsync();
            var handle = client.GetWorkflowHandle(workflowId);
            
            var events = new List<HistoryEvent>();
            await foreach (var historyEvent in handle.FetchHistoryEventsAsync())
            {
                events.Add(historyEvent);
            }

            var activityEvents = ExtractActivityEvents(events);

            _logger.LogInformation("Successfully extracted {Count} activity events for workflow {WorkflowId}", 
                activityEvents.Count, workflowId);

            return ServiceResult<List<WorkflowActivityEvent>>.Success(activityEvents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching workflow events for workflow {WorkflowId}", workflowId);
            return ServiceResult<List<WorkflowActivityEvent>>.InternalServerError($"Failed to fetch workflow events: {ex.Message}");
        }
    }

    private List<WorkflowActivityEvent> ExtractActivityEvents(List<HistoryEvent> events)
    {
        var activityEvents = new List<WorkflowActivityEvent>();
        var scheduledEvents = new Dictionary<long, HistoryEvent>();
        var startedEvents = new Dictionary<long, HistoryEvent>();

        // First pass: collect scheduled, started, and workflow events
        foreach (var evt in events)
        {
            switch (evt.EventType)
            {
                case EventType.WorkflowExecutionStarted:
                    activityEvents.Add(new WorkflowActivityEvent
                    {
                        ActivityName = "Flow Started",
                        StartedTime = evt.EventTime?.ToDateTime().ToString("o"),
                        Inputs = evt.WorkflowExecutionStartedEventAttributes.Input?.Payloads_.Select(p => p.Data.ToStringUtf8()).ToArray()
                    });
                    break;
                case EventType.WorkflowExecutionCompleted:
                    activityEvents.Add(new WorkflowActivityEvent
                    {
                        ActivityName = "Flow Completed",
                        StartedTime = evt.EventTime?.ToDateTime().ToString("o"),
                        Result = evt.WorkflowExecutionCompletedEventAttributes.Result?.Payloads_.FirstOrDefault()?.Data.ToStringUtf8()
                    });
                    break;
                case EventType.ActivityTaskScheduled:
                    scheduledEvents[evt.EventId] = evt;
                    break;
                case EventType.ActivityTaskStarted:
                    //var startedAttrs = evt.ActivityTaskStartedEventAttributes;
                    startedEvents[evt.EventId] = evt;
                    break;
                case EventType.ActivityTaskCompleted:
                    var completedAttrs = evt.ActivityTaskCompletedEventAttributes;
                    var startedEvent = startedEvents[completedAttrs.StartedEventId];
                    var scheduledEvent = scheduledEvents[completedAttrs.ScheduledEventId];
                    
                    var activityEvent = new WorkflowActivityEvent
                    {
                        ActivityName = scheduledEvent.ActivityTaskScheduledEventAttributes.ActivityType.Name,
                        StartedTime = startedEvent.EventTime?.ToDateTime().ToString("o"),
                        EndedTime = evt.EventTime?.ToDateTime().ToString("o"),
                        Inputs = scheduledEvent.ActivityTaskScheduledEventAttributes.Input?.Payloads_.Select(p => p.Data.ToStringUtf8()).ToArray(),
                        Result = completedAttrs.Result?.Payloads_.FirstOrDefault()?.Data.ToStringUtf8()
                    };
                    activityEvents.Add(activityEvent);
                    break;
            }
        }

        return activityEvents;
    }
}
