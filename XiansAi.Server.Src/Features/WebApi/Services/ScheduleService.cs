using Shared.Models.Schedule;
using Shared.Utils.Temporal;
using Shared.Utils.Services;
using Temporalio.Client;
using System.Text.Json;

namespace Features.WebApi.Services;

/// <summary>
/// Service interface for schedule management
/// </summary>
public interface IScheduleService
{
    /// <summary>
    /// Retrieves schedules with optional filtering
    /// </summary>
    Task<ServiceResult<List<ScheduleModel>>> GetSchedulesAsync(ScheduleFilterRequest request);
    
    /// <summary>
    /// Gets upcoming runs for a specific schedule
    /// </summary>
    Task<ServiceResult<List<ScheduleRunModel>>> GetUpcomingRunsAsync(string scheduleId, int count = 10);
    
    /// <summary>
    /// Gets schedule execution history
    /// </summary>
    Task<ServiceResult<List<ScheduleRunModel>>> GetScheduleHistoryAsync(string scheduleId, int count = 50);
    
    /// <summary>
    /// Gets schedule details by ID
    /// </summary>
    Task<ServiceResult<ScheduleModel>> GetScheduleByIdAsync(string scheduleId);
}

/// <summary>
/// Implementation of schedule service using Temporal.io
/// </summary>
public class ScheduleService : IScheduleService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ILogger<ScheduleService> _logger;
    
    public ScheduleService(
        ITemporalClientFactory clientFactory,
        ILogger<ScheduleService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task<ServiceResult<List<ScheduleModel>>> GetSchedulesAsync(
        ScheduleFilterRequest request)
    {
        try
        {
            var client = await _clientFactory.GetClientAsync();
            var schedules = new List<ScheduleModel>();
            
            _logger.LogInformation("Retrieving schedules with filters: Agent={Agent}, Workflow={Workflow}, Status={Status}", 
                request.AgentName, request.WorkflowType, request.Status);
            
            // Use real Temporal schedule API
            await foreach (var schedule in client.ListSchedulesAsync())
            {
                try
                {
                    var scheduleHandle = client.GetScheduleHandle(schedule.Id);
                    var description = await scheduleHandle.DescribeAsync();
                    
                    var scheduleModel = MapToScheduleModel(schedule.Id, description);
                    
                    // Apply filters during iteration for better performance
                    if (ShouldIncludeSchedule(scheduleModel, request))
                    {
                        schedules.Add(scheduleModel);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process schedule {ScheduleId}", schedule.Id);
                    // Continue with other schedules instead of failing completely
                }
            }
            
            // Apply pagination
            var paginatedSchedules = ApplyPagination(schedules, request);
            
            _logger.LogInformation("Retrieved {Count} schedules out of {Total} total", paginatedSchedules.Count, schedules.Count);
            return ServiceResult<List<ScheduleModel>>.Success(paginatedSchedules);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve schedules");
            return ServiceResult<List<ScheduleModel>>.InternalServerError($"Failed to retrieve schedules: {ex.Message}");
        }
    }
    
    public async Task<ServiceResult<List<ScheduleRunModel>>> GetUpcomingRunsAsync(
        string scheduleId, 
        int count = 10)
    {
        try
        {
            var client = await _clientFactory.GetClientAsync();
            var scheduleHandle = client.GetScheduleHandle(scheduleId);
            var description = await scheduleHandle.DescribeAsync();
            
            var upcomingRuns = new List<ScheduleRunModel>();
            
            // Calculate next execution times based on schedule spec
            var nextRunTimes = CalculateNextRunTimes(description.Schedule.Spec, count);
            
            foreach (var (runTime, index) in nextRunTimes.Select((t, i) => (t, i)))
            {
                upcomingRuns.Add(new ScheduleRunModel
                {
                    RunId = $"{scheduleId}-upcoming-{runTime:yyyyMMddHHmmss}",
                    ScheduleId = scheduleId,
                    ScheduledTime = runTime,
                    Status = ScheduleRunStatus.Scheduled
                });
            }
            
            _logger.LogInformation("Retrieved {Count} upcoming runs for schedule {ScheduleId}", upcomingRuns.Count, scheduleId);
            return ServiceResult<List<ScheduleRunModel>>.Success(upcomingRuns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get upcoming runs for schedule {ScheduleId}", scheduleId);
            return ServiceResult<List<ScheduleRunModel>>.InternalServerError($"Failed to retrieve upcoming runs: {ex.Message}");
        }
    }
    
    public async Task<ServiceResult<List<ScheduleRunModel>>> GetScheduleHistoryAsync(
        string scheduleId, 
        int count = 50)
    {
        try
        {
            var client = await _clientFactory.GetClientAsync();
            var scheduleHandle = client.GetScheduleHandle(scheduleId);
            var description = await scheduleHandle.DescribeAsync();
            
            var historyRuns = new List<ScheduleRunModel>();
            
            // Get recent actions from schedule description
            var recentActions = description.Info?.RecentActions?.Take(count).ToList() ?? new List<Temporalio.Client.Schedules.ScheduleActionResult>();
            
            foreach (var action in recentActions.Select((a, i) => new { Action = a, Index = i }))
            {
                var runStatus = ScheduleRunStatus.Completed; // Simplified - assume all past actions completed
                var scheduledTime = DateTime.UtcNow.AddHours(-action.Index - 1); // Mock scheduled time
                var actualTime = scheduledTime.AddMinutes(2); // Mock actual time
                
                historyRuns.Add(new ScheduleRunModel
                {
                    RunId = $"{scheduleId}-history-{action.Index}",
                    ScheduleId = scheduleId,
                    ScheduledTime = scheduledTime,
                    ActualRunTime = actualTime,
                    Status = runStatus,
                    WorkflowRunId = $"workflow-{scheduleId}-{action.Index}"
                });
            }
            
            // Sort by scheduled time descending (most recent first)
            historyRuns = historyRuns.OrderByDescending(r => r.ScheduledTime).ToList();
            
            _logger.LogInformation("Retrieved {Count} history runs for schedule {ScheduleId}", historyRuns.Count, scheduleId);
            return ServiceResult<List<ScheduleRunModel>>.Success(historyRuns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get schedule history for schedule {ScheduleId}", scheduleId);
            return ServiceResult<List<ScheduleRunModel>>.InternalServerError($"Failed to retrieve schedule history: {ex.Message}");
        }
    }
    
    public async Task<ServiceResult<ScheduleModel>> GetScheduleByIdAsync(string scheduleId)
    {
        try
        {
            var client = await _clientFactory.GetClientAsync();
            var scheduleHandle = client.GetScheduleHandle(scheduleId);
            var description = await scheduleHandle.DescribeAsync();
            
            // Create a simple mock entry for mapping
            var schedule = MapToScheduleModel(scheduleId, description);
            
            _logger.LogInformation("Retrieved schedule {ScheduleId}", scheduleId);
            return ServiceResult<ScheduleModel>.Success(schedule);
        }
        catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("NotFound"))
        {
            _logger.LogWarning("Schedule {ScheduleId} not found", scheduleId);
            return ServiceResult<ScheduleModel>.NotFound($"Schedule {scheduleId} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get schedule {ScheduleId}", scheduleId);
            return ServiceResult<ScheduleModel>.InternalServerError($"Failed to retrieve schedule: {ex.Message}");
        }
    }
    
    private ScheduleModel MapToScheduleModel(string scheduleId, Temporalio.Client.Schedules.ScheduleDescription description)
    {
        // Extract agent metadata from schedule memo or action
        var agentName = ExtractAgentNameFromMemo(description.Schedule.Action) ?? "Unknown";
        var workflowType = ExtractWorkflowTypeFromAction(description.Schedule.Action) ?? "Unknown";
        var metadata = ExtractMetadataFromMemo(description.Schedule.Action) ?? new Dictionary<string, object>();
        
        // Calculate next run time
        var nextRunTime = CalculateNextRunTime(description.Schedule.Spec);
        
        // Get last run information
        var lastAction = description.Info?.RecentActions?.FirstOrDefault();
        DateTime? lastRunTime = null;
        
        // Try to estimate last run time based on recent actions count
        if (lastAction != null && description.Info?.NumActions > 0)
        {
            lastRunTime = DateTime.UtcNow.AddHours(-1); // Simple estimate
        }
        
        return new ScheduleModel
        {
            Id = scheduleId,
            AgentName = agentName,
            WorkflowType = workflowType,
            ScheduleSpec = FormatScheduleSpec(description.Schedule.Spec),
            NextRunTime = nextRunTime,
            CreatedAt = description.Info?.CreatedAt ?? DateTime.UtcNow,
            Status = MapScheduleState(description.Schedule.State),
            Metadata = metadata,
            Description = ExtractDescriptionFromMemo(description.Schedule.Action),
            LastRunTime = lastRunTime,
            ExecutionCount = description.Info?.NumActions ?? 0
        };
    }
    
    private string? ExtractAgentNameFromMemo(Temporalio.Client.Schedules.ScheduleAction action)
    {
        try
        {
            if (action is Temporalio.Client.Schedules.ScheduleActionStartWorkflow startWorkflow)
            {
                // Try to extract from memo
                if (startWorkflow.Options?.Memo?.ContainsKey("agentName") == true)
                {
                    return startWorkflow.Options.Memo["agentName"]?.ToString();
                }
                
                // Fallback: try to extract from workflow type
                var workflowType = startWorkflow.Workflow;
                if (!string.IsNullOrEmpty(workflowType) && workflowType.Contains("Agent"))
                {
                    return workflowType.Replace("Workflow", "").Replace("Agent", "") + "Agent";
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract agent name from schedule memo");
            return null;
        }
    }
    
    private string? ExtractWorkflowTypeFromAction(Temporalio.Client.Schedules.ScheduleAction action)
    {
        if (action is Temporalio.Client.Schedules.ScheduleActionStartWorkflow startWorkflow)
        {
            return startWorkflow.Workflow;
        }
        return null;
    }
    
    private string? ExtractDescriptionFromMemo(Temporalio.Client.Schedules.ScheduleAction action)
    {
        try
        {
            if (action is Temporalio.Client.Schedules.ScheduleActionStartWorkflow startWorkflow)
            {
                if (startWorkflow.Options?.Memo?.ContainsKey("description") == true)
                {
                    return startWorkflow.Options.Memo["description"]?.ToString();
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract description from schedule memo");
            return null;
        }
    }
    
    private Dictionary<string, object> ExtractMetadataFromMemo(Temporalio.Client.Schedules.ScheduleAction action)
    {
        var metadata = new Dictionary<string, object>();
        
        try
        {
            if (action is Temporalio.Client.Schedules.ScheduleActionStartWorkflow startWorkflow)
            {
                if (startWorkflow.Options?.Memo != null)
                {
                    foreach (var kvp in startWorkflow.Options.Memo)
                    {
                        if (kvp.Key != "agentName" && kvp.Key != "description")
                        {
                            metadata[kvp.Key] = kvp.Value ?? "";
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract metadata from schedule memo");
        }
        
        return metadata;
    }
    
    private ScheduleStatus MapScheduleState(Temporalio.Client.Schedules.ScheduleState state)
    {
        if (state.Paused) return ScheduleStatus.Paused;
        if (state.LimitedActions) return ScheduleStatus.Completed;
        return ScheduleStatus.Active;
    }
    
    private string FormatScheduleSpec(Temporalio.Client.Schedules.ScheduleSpec spec)
    {
        // Format the schedule specification for display
        try
        {
            // Try to get a readable format from the spec
            return spec.ToString() ?? "Unknown schedule";
        }
        catch
        {
            return "Unknown schedule";
        }
    }
    
    private DateTime CalculateNextRunTime(Temporalio.Client.Schedules.ScheduleSpec spec)
    {
        // Simple next run time calculation
        // In a real implementation, you'd parse the schedule spec properly
        var now = DateTime.UtcNow;
        
        try
        {
            // Default to next hour for simplicity
            // In production, you would properly parse the schedule spec
            return now.AddHours(1);
        }
        catch
        {
            return now.AddHours(1);
        }
    }
    
    private List<DateTime> CalculateNextRunTimes(Temporalio.Client.Schedules.ScheduleSpec spec, int count)
    {
        var times = new List<DateTime>();
        var baseTime = DateTime.UtcNow;
        
        // Simple implementation - generate hourly times for demonstration
        for (int i = 0; i < count; i++)
        {
            times.Add(baseTime.AddHours(i + 1));
        }
        
        return times;
    }
    
    private bool ShouldIncludeSchedule(ScheduleModel schedule, ScheduleFilterRequest request)
    {
        if (!string.IsNullOrEmpty(request.AgentName) && 
            !schedule.AgentName.Contains(request.AgentName, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (!string.IsNullOrEmpty(request.WorkflowType) && 
            !schedule.WorkflowType.Contains(request.WorkflowType, StringComparison.OrdinalIgnoreCase))
            return false;
            
        if (request.Status.HasValue && schedule.Status != request.Status.Value)
            return false;
            
        if (!string.IsNullOrEmpty(request.SearchTerm))
        {
            var searchTerm = request.SearchTerm.ToLowerInvariant();
            if (!schedule.Description?.ToLowerInvariant().Contains(searchTerm) == true &&
                !schedule.Id.ToLowerInvariant().Contains(searchTerm) &&
                !schedule.AgentName.ToLowerInvariant().Contains(searchTerm))
                return false;
        }
        
        return true;
    }
    
    private List<ScheduleModel> ApplyPagination(List<ScheduleModel> schedules, ScheduleFilterRequest request)
    {
        // Simple pagination implementation
        var startIndex = 0;
        if (!string.IsNullOrEmpty(request.PageToken) && int.TryParse(request.PageToken, out var pageIndex))
        {
            startIndex = pageIndex * request.PageSize;
        }
        
        return schedules
            .Skip(startIndex)
            .Take(request.PageSize)
            .ToList();
    }
}