using Shared.Models.Schedule;
using Shared.Utils.Temporal;
using Shared.Utils.Services;
using Shared.Auth;
using Shared.Services;
using Shared.Repositories;
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
/// Implementation of schedule service using Temporal.io with tenant isolation and permission checks
/// </summary>
public class ScheduleService : IScheduleService
{
    private readonly ITemporalClientFactory _clientFactory;
    private readonly ITenantContext _tenantContext;
    private readonly IPermissionsService _permissionsService;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<ScheduleService> _logger;
    
    public ScheduleService(
        ITemporalClientFactory clientFactory,
        ITenantContext tenantContext,
        IPermissionsService permissionsService,
        IAgentRepository agentRepository,
        ILogger<ScheduleService> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    public async Task<ServiceResult<List<ScheduleModel>>> GetSchedulesAsync(
        ScheduleFilterRequest request)
    {
        try
        {
            var client = await _clientFactory.GetClientAsync();
            var schedules = new List<ScheduleModel>();
            
            // Calculate pagination parameters
            var neededCount = request.PageSize;
            var skipCount = 0;
            if (!string.IsNullOrEmpty(request.PageToken) && int.TryParse(request.PageToken, out var pageIndex))
            {
                skipCount = pageIndex * request.PageSize;
            }
            
            var processedCount = 0;
            var skippedCount = 0;
            var totalProcessed = 0;
            
            _logger.LogInformation("Retrieving schedules with filters: Agent={Agent}, Workflow={Workflow}, Status={Status}, PageSize={PageSize}, Skip={Skip}", 
                request.AgentName, request.WorkflowType, request.Status, neededCount, skipCount);
            
            // Use real Temporal schedule API with early termination
            await foreach (var schedule in client.ListSchedulesAsync())
            {
                totalProcessed++;
                
                try
                {
                    var scheduleId = schedule.Id;
                    
                    // Early lightweight filtering by schedule ID before describing
                    // This avoids unnecessary API calls for schedules we can filter out early
                    if (!string.IsNullOrEmpty(request.AgentName) && 
                        !scheduleId.Contains(request.AgentName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;  // Skip without describing - saves API call
                    }
                    
                    if (!string.IsNullOrEmpty(request.WorkflowType) && 
                        !scheduleId.Contains(request.WorkflowType, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;  // Skip without describing - saves API call
                    }
                    
                    // Now describe to get full details (expensive operation)
                    var scheduleHandle = client.GetScheduleHandle(scheduleId);
                    var description = await scheduleHandle.DescribeAsync();
                    
                    var scheduleModel = MapToScheduleModel(scheduleId, description);
                    
                    // Security check: Tenant isolation
                    if (!string.IsNullOrEmpty(scheduleModel.TenantId) && 
                        scheduleModel.TenantId != _tenantContext.TenantId)
                    {
                        _logger.LogDebug("Skipping schedule {ScheduleId} - belongs to different tenant", scheduleId);
                        continue;
                    }
                    
                    // Security check: Agent permission
                    if (!string.IsNullOrEmpty(scheduleModel.AgentName))
                    {
                        var hasPermission = await HasScheduleAccessAsync(scheduleModel.AgentName);
                        if (!hasPermission)
                        {
                            _logger.LogDebug("Skipping schedule {ScheduleId} - user lacks permission for agent {AgentName}", 
                                scheduleId, scheduleModel.AgentName);
                            continue;
                        }
                    }
                    
                    // Apply detailed filters
                    if (!ShouldIncludeSchedule(scheduleModel, request))
                    {
                        continue;
                    }
                    
                    // Handle pagination by skipping
                    if (skippedCount < skipCount)
                    {
                        skippedCount++;
                        continue;
                    }
                    
                    schedules.Add(scheduleModel);
                    processedCount++;
                    
                    // Early termination - we have enough for this page
                    if (processedCount >= neededCount)
                    {
                        _logger.LogInformation("Collected {Count} schedules for page, stopping iteration early (processed {Total} total)", 
                            processedCount, totalProcessed);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process schedule {ScheduleId}", schedule.Id);
                    // Continue with other schedules instead of failing completely
                }
            }
            
            _logger.LogInformation("Retrieved {Count} schedules (processed {Processed} total, skipped {Skipped} for pagination)", 
                schedules.Count, totalProcessed, skippedCount);
            
            return ServiceResult<List<ScheduleModel>>.Success(schedules);
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
        // Extract tenant and agent metadata from schedule memo or action
        var tenantId = ExtractTenantIdFromMemo(description.Schedule.Action);
        var agentName = ExtractAgentNameFromMemo(description.Schedule.Action) ?? "Unknown";
        var workflowType = ExtractWorkflowTypeFromAction(description.Schedule.Action) ?? "Unknown";
        var metadata = ExtractMetadataFromMemo(description.Schedule.Action) ?? new Dictionary<string, object>();
        
        // Calculate next run time
        var nextRunTime = CalculateNextRunTime(description.Schedule.Spec);
        
        // Get last run information with better estimation
        var recentActions = description.Info?.RecentActions?.ToList() ?? new List<Temporalio.Client.Schedules.ScheduleActionResult>();
        DateTime? lastRunTime = null;
        
        if (recentActions.Any())
        {
            try
            {
                // Try to get the actual timestamp from the most recent action
                var mostRecentAction = recentActions.First();
                
                // If we have action result info, use it to estimate timing
                if (description.Info?.NumActions > 0)
                {
                    // Estimate based on schedule pattern and number of actions
                    var totalActions = (long)description.Info.NumActions;
                    var createdTime = description.Info?.CreatedAt ?? DateTime.UtcNow;
                    
                    if (totalActions > 1)
                    {
                        // Estimate average interval between runs
                        var timeSinceCreation = DateTime.UtcNow - createdTime;
                        var averageInterval = timeSinceCreation.TotalMilliseconds / (totalActions - 1);
                        lastRunTime = DateTime.UtcNow.AddMilliseconds(-averageInterval);
                    }
                    else
                    {
                        // Only one execution, estimate based on when it might have last run
                        var nextRun = CalculateNextRunTime(description.Schedule.Spec);
                        var estimatedInterval = nextRun - DateTime.UtcNow;
                        lastRunTime = DateTime.UtcNow.Subtract(estimatedInterval);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to calculate last run time for schedule {ScheduleId}", scheduleId);
                lastRunTime = null;
            }
        }
        
        return new ScheduleModel
        {
            Id = scheduleId,
            TenantId = tenantId,
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
    
    /// <summary>
    /// Checks if the current user has access to schedules for a specific agent
    /// </summary>
    private async Task<bool> HasScheduleAccessAsync(string agentName)
    {
        try
        {
            // Check if user has read permission for this agent
            var readPermissionResult = await _permissionsService.HasReadPermission(agentName);
            
            if (!readPermissionResult.IsSuccess)
            {
                _logger.LogWarning("Permission check failed for agent {AgentName}: {Error}", 
                    agentName, readPermissionResult.ErrorMessage);
                return false;
            }
            
            return readPermissionResult.Data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking schedule access for agent {AgentName}", agentName);
            return false;
        }
    }
    
    /// <summary>
    /// Extracts tenant ID from schedule memo
    /// </summary>
    private string? ExtractTenantIdFromMemo(Temporalio.Client.Schedules.ScheduleAction action)
    {
        try
        {
            if (action is Temporalio.Client.Schedules.ScheduleActionStartWorkflow startWorkflow)
            {
                if (startWorkflow.Options?.Memo?.ContainsKey("tenantId") == true)
                {
                    return startWorkflow.Options.Memo["tenantId"]?.ToString();
                }
                
                // Also check for "TenantId" (capitalized) as fallback
                if (startWorkflow.Options?.Memo?.ContainsKey("TenantId") == true)
                {
                    return startWorkflow.Options.Memo["TenantId"]?.ToString();
                }
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract tenant ID from schedule memo");
            return null;
        }
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
        // Map Temporal schedule state to our enum values
        
        if (state.Paused) 
            return ScheduleStatus.Suspended;
            
        if (state.LimitedActions) 
            return ScheduleStatus.Completed;
            
        // Default to Running for active schedules
        return ScheduleStatus.Running;
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
        var now = DateTime.UtcNow;
        
        try
        {
            // Check for intervals first
            if (spec.Intervals?.Any() == true)
            {
                var interval = spec.Intervals.First();
                if (interval.Every != TimeSpan.Zero)
                {
                    return now.Add(interval.Every);
                }
            }
            
            // Check for calendar specs (cron-like)
            if (spec.Calendars?.Any() == true)
            {
                var calendar = spec.Calendars.First();
                return CalculateNextCalendarRun(calendar, now);
            }
            
            // Check for cron expressions
            if (spec.CronExpressions?.Any() == true)
            {
                var cronExpr = spec.CronExpressions.First();
                return CalculateNextCronRun(cronExpr, now);
            }
            
            // Default fallback
            return now.AddHours(1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate next run time from schedule spec");
            return now.AddHours(1);
        }
    }
    
    private DateTime CalculateNextCalendarRun(Temporalio.Client.Schedules.ScheduleCalendarSpec calendar, DateTime from)
    {
        var nextRun = from.Date; // Start from today at midnight
        
        try
        {
            // If specific hours are set, find the next matching hour
            if (calendar.Hour?.Any() == true)
            {
                var currentHour = from.Hour;
                var hours = ExtractRangeValues(calendar.Hour);
                var nextHour = hours.Where(h => h > currentHour).OrderBy(h => h).FirstOrDefault();
                
                if (nextHour == 0) // No more hours today, go to tomorrow
                {
                    nextRun = nextRun.AddDays(1);
                    nextHour = hours.OrderBy(h => h).First();
                }
                
                nextRun = nextRun.AddHours(nextHour);
            }
            else
            {
                nextRun = nextRun.AddHours(from.Hour + 1); // Next hour
            }
            
            // Add minutes if specified
            if (calendar.Minute?.Any() == true)
            {
                var minutes = ExtractRangeValues(calendar.Minute);
                var minute = minutes.OrderBy(m => m).First();
                nextRun = new DateTime(nextRun.Year, nextRun.Month, nextRun.Day, nextRun.Hour, minute, 0, DateTimeKind.Utc);
            }
            
            return nextRun > from ? nextRun : nextRun.AddDays(1);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate calendar-based next run time");
            return from.AddHours(1);
        }
    }
    
    private DateTime CalculateNextCronRun(string cronExpression, DateTime from)
    {
        try
        {
            // Simple cron parsing for common patterns
            var parts = cronExpression.Split(' ');
            if (parts.Length >= 5)
            {
                // Basic minute/hour parsing
                var minute = parts[0] == "*" ? 0 : int.TryParse(parts[0], out var m) ? m : 0;
                var hour = parts[1] == "*" ? from.Hour + 1 : int.TryParse(parts[1], out var h) ? h : from.Hour + 1;
                
                var nextRun = new DateTime(from.Year, from.Month, from.Day, hour, minute, 0, DateTimeKind.Utc);
                
                // If the calculated time is in the past, add a day
                if (nextRun <= from)
                {
                    nextRun = nextRun.AddDays(1);
                }
                
                return nextRun;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse cron expression: {CronExpression}", cronExpression);
        }
        
        return from.AddHours(1);
    }
    
    private List<int> ExtractRangeValues(IEnumerable<Temporalio.Client.Schedules.ScheduleRange> ranges)
    {
        var values = new List<int>();
        
        foreach (var range in ranges)
        {
            try
            {
                // Try to extract start and end values from the range
                // This is a simplified implementation - the actual range structure may vary
                var rangeString = range.ToString();
                if (int.TryParse(rangeString, out var singleValue))
                {
                    values.Add(singleValue);
                }
                else
                {
                    // Handle range notation like "9-17" or step notation
                    if (rangeString.Contains("-"))
                    {
                        var parts = rangeString.Split('-');
                        if (parts.Length == 2 && int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end))
                        {
                            for (int i = start; i <= end; i++)
                            {
                                values.Add(i);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse schedule range: {Range}", range);
            }
        }
        
        return values.Count > 0 ? values : new List<int> { 0 }; // Default fallback
    }
    
    private List<DateTime> CalculateNextRunTimes(Temporalio.Client.Schedules.ScheduleSpec spec, int count)
    {
        var times = new List<DateTime>();
        var baseTime = DateTime.UtcNow;
        
        try
        {
            // Calculate the first run time
            var firstRun = CalculateNextRunTime(spec);
            times.Add(firstRun);
            
            // Calculate subsequent run times based on the schedule pattern
            if (spec.Intervals?.Any() == true)
            {
                var interval = spec.Intervals.First();
                var intervalSpan = interval.Every;
                
                for (int i = 1; i < count; i++)
                {
                    times.Add(times[i - 1].Add(intervalSpan));
                }
            }
            else if (spec.Calendars?.Any() == true)
            {
                var calendar = spec.Calendars.First();
                var currentTime = firstRun;
                
                for (int i = 1; i < count; i++)
                {
                    currentTime = CalculateNextCalendarRun(calendar, currentTime.AddMinutes(1));
                    times.Add(currentTime);
                }
            }
            else if (spec.CronExpressions?.Any() == true)
            {
                var cronExpr = spec.CronExpressions.First();
                var currentTime = firstRun;
                
                for (int i = 1; i < count; i++)
                {
                    currentTime = CalculateNextCronRun(cronExpr, currentTime.AddMinutes(1));
                    times.Add(currentTime);
                }
            }
            else
            {
                // Fallback: generate hourly times
                for (int i = 1; i < count; i++)
                {
                    times.Add(firstRun.AddHours(i));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate multiple run times, falling back to hourly schedule");
            
            // Fallback: generate hourly times from now
            for (int i = 0; i < count; i++)
            {
                times.Add(baseTime.AddHours(i + 1));
            }
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
            if (!(schedule.Description?.ToLowerInvariant().Contains(searchTerm) == true ||
                  schedule.Id.ToLowerInvariant().Contains(searchTerm) ||
                  schedule.AgentName.ToLowerInvariant().Contains(searchTerm)))
                return false;
        }
        
        return true;
    }
    
}