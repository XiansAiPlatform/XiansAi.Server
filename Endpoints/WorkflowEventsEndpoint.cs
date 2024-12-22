using System.Threading.Channels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.OpenApi.Extensions;
using Temporalio.Api.Enums.V1;
using Temporalio.Api.History.V1;
using Temporalio.Client;


public record WorkflowActivityEvent
{
    public string? ActivityName { get; init; }
    public string? ScheduledTime { get; init; }
    public string? StartedTime { get; init; }
    public string? EndedTime { get; init; }
    public object[]? Inputs { get; init; }
    public string? Result { get; init; }
    public long CompletedEventId { get; init; }
}
public class WorkflowEventsEndpoint
{
    private readonly ITemporalClientService _clientService;
    private readonly ILogger<WorkflowEventsEndpoint> _logger;

    public WorkflowEventsEndpoint(
        ITemporalClientService clientService,
        ILogger<WorkflowEventsEndpoint> logger)
    {
        _clientService = clientService ?? throw new ArgumentNullException(nameof(clientService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /**
    curl -X GET "http://localhost:5257/api/workflows/ProspectingWorkflow-79b8f89d-2c00-43a7-a3aa-50292ffdc65d/events"
    */
    public async Task<IResult> GetWorkflowEvents(HttpContext context)
    {
        var workflowId = context.Request.RouteValues["workflowId"] as string;
        _logger.LogInformation("Getting workflow events for workflow ID: {WorkflowId}", workflowId);
        if (string.IsNullOrEmpty(workflowId))
        {
            _logger.LogWarning("Attempted to get events with empty workflow ID");
            return Results.BadRequest("WorkflowId is required");
        }
        try
        {
            var client = await _clientService.GetClientAsync();
            var handle = client.GetWorkflowHandle(workflowId);
            
            var events = new List<HistoryEvent>();
            await foreach (var historyEvent in handle.FetchHistoryEventsAsync())
            {
                events.Add(historyEvent);
            }
            _logger.LogInformation("Successfully fetched {Count} events for workflow {WorkflowId}", 
                events.Count, workflowId);

            var activityEvents = ExtractActivityEvents(events);

            _logger.LogInformation("Successfully extracted {Count} activity events for workflow {WorkflowId}", 
                activityEvents.Count, workflowId);

            return Results.Ok(activityEvents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching workflow events for workflow {WorkflowId}", workflowId);
            return Results.Problem(
                title: "Failed to fetch workflow events",
                detail: ex.Message,
                statusCode: StatusCodes.Status500InternalServerError
            );
        }
    }

    private List<WorkflowActivityEvent> ExtractActivityEvents(List<HistoryEvent> events)
    {
        var activityEvents = new List<WorkflowActivityEvent>();
        var scheduledEvents = new Dictionary<long, HistoryEvent>();
        var startedEvents = new Dictionary<long, HistoryEvent>();

        // First pass: collect scheduled and started events
        foreach (var evt in events)
        {
            switch (evt.EventType)
            {
                case EventType.ActivityTaskScheduled:
                    scheduledEvents[evt.EventId] = evt;
                    break;
                case EventType.ActivityTaskStarted:
                    var startedAttrs = evt.ActivityTaskStartedEventAttributes;
                    startedEvents[startedAttrs.ScheduledEventId] = evt;
                    break;
                case EventType.ActivityTaskCompleted:
                    var completedAttrs = evt.ActivityTaskCompletedEventAttributes;
                    var startedEvent = startedEvents[completedAttrs.ScheduledEventId];
                    var scheduledEvent = scheduledEvents[completedAttrs.ScheduledEventId];
                    
                    var activityEvent = new WorkflowActivityEvent
                    {
                        ActivityName = scheduledEvent.ActivityTaskScheduledEventAttributes.ActivityType.Name,
                        ScheduledTime = scheduledEvent.EventTime?.ToDateTime().ToString("o"),
                        StartedTime = startedEvent.EventTime?.ToDateTime().ToString("o"),
                        EndedTime = evt.EventTime?.ToDateTime().ToString("o"),
                        CompletedEventId = completedAttrs.ScheduledEventId,
                        Inputs = scheduledEvent.ActivityTaskScheduledEventAttributes.Input.Payloads_.Select(p => p.Data.ToStringUtf8()).ToArray(),
                        Result = completedAttrs.Result.Payloads_.Select(p => p.Data.ToStringUtf8()).FirstOrDefault(),
                    };
                    activityEvents.Add(activityEvent);
                    break;
            }
        }

        return activityEvents;
    }
}
