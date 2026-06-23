using Shared.Models.Schedule;
using Shared.Utils.Temporal;
using Shared.Utils.Services;
using Shared.Auth;
using Shared.Services;
using Shared.Repositories;
using Temporalio.Client;
using System.Text.Json;
using Shared.Utils;

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
    
    /// <summary>
    /// Deletes a specific schedule by ID
    /// </summary>
    Task<ServiceResult<bool>> DeleteScheduleByIdAsync(string scheduleId);

    /// <summary>
    /// Pauses (suspends) a specific schedule by ID
    /// </summary>
    Task<ServiceResult<bool>> PauseScheduleAsync(string scheduleId, string? note = null);

    /// <summary>
    /// Resumes (unpauses) a specific schedule by ID
    /// </summary>
    Task<ServiceResult<bool>> ResumeScheduleAsync(string scheduleId, string? note = null);
    
    /// <summary>
    /// Deletes all schedules for a specific agent
    /// </summary>
    Task<ServiceResult<ScheduleDeleteResult>> DeleteAllSchedulesByAgentAsync(string agentName);
    
    /// <summary>
    /// Deletes all schedules for the current tenant
    /// </summary>
    Task<ServiceResult<ScheduleDeleteResult>> DeleteAllSchedulesAsync();
}

/// <summary>
/// Result model for schedule deletion operations
/// </summary>
public class ScheduleDeleteResult
{
    public int DeletedCount { get; set; }
    public List<string> DeletedScheduleIds { get; set; } = new();
    public List<string> FailedScheduleIds { get; set; } = new();
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
            
            // Build search attributes query for server-side filtering
            var queryParts = new List<string>();
            
            // Mandatory: Filter by tenant ID for tenant isolation
            if (!string.IsNullOrEmpty(_tenantContext.TenantId))
            {
                queryParts.Add($"tenantId = '{_tenantContext.TenantId}'");
            }
            
            // Optional: Filter by agent name if specified
            if (!string.IsNullOrEmpty(request.AgentName))
            {
                queryParts.Add($"agent = '{request.AgentName}'");
            }
            
            var query = queryParts.Count > 0 ? string.Join(" AND ", queryParts) : string.Empty;
            
            _logger.LogInformation("Retrieving schedules with filters: Query={Query}, Workflow={Workflow}, Status={Status}, PageSize={PageSize}, Skip={Skip}", 
                query, request.WorkflowType, request.Status, neededCount, skipCount);
            
            // Use Temporal schedule API with server-side filtering via search attributes
            var listOptions = !string.IsNullOrEmpty(query) 
                ? new Temporalio.Client.Schedules.ScheduleListOptions { Query = query }
                : null;
            
            await foreach (var schedule in client.ListSchedulesAsync(listOptions))
            {
                totalProcessed++;
                
                try
                {
                    var scheduleId = schedule.Id;
                    
                    // Now describe to get full details (expensive operation)
                    var scheduleHandle = client.GetScheduleHandle(scheduleId);
                    var description = await scheduleHandle.DescribeAsync();
                    
                    var scheduleModel = MapToScheduleModel(scheduleId, description);
                    
                    // Security check: Agent permission (still needed for authorization)
                    if (!string.IsNullOrEmpty(scheduleModel.AgentName))
                    {
                        var hasPermission = await HasScheduleAccessAsync(scheduleModel.AgentName);
                        if (!hasPermission)
                        {
                            _logger.LogDebug("Skipping schedule {ScheduleId} - user lacks permission for agent {AgentName}", 
                                LogSanitizer.Sanitize(scheduleId), LogSanitizer.Sanitize(scheduleModel.AgentName));
                            continue;
                        }
                    }
                    
                    // Apply additional client-side filters (workflow type, status, search term)
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
                catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("NotFound"))
                {
                    _logger.LogDebug("Schedule {ScheduleId} was found in listing but not found when describing - likely recently deleted or stale cache", LogSanitizer.Sanitize(schedule.Id));
                    // Continue with other schedules - this is normal when schedules are recently deleted
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to process schedule {ScheduleId}", LogSanitizer.Sanitize(schedule.Id));
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
            
            // Prefer the REAL next action times computed by Temporal; only fall back
            // to our own estimation if Temporal did not return any (e.g. paused).
            var nextRunTimes = description.Info?.NextActionTimes?.Take(count).ToList();
            if (nextRunTimes == null || nextRunTimes.Count == 0)
            {
                nextRunTimes = CalculateNextRunTimes(description.Schedule.Spec, count);
            }
            
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
            
            _logger.LogInformation("Retrieved {Count} upcoming runs for schedule {ScheduleId}", upcomingRuns.Count, LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<List<ScheduleRunModel>>.Success(upcomingRuns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get upcoming runs for schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
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
            
            // Use the REAL recent actions recorded by Temporal. Each action carries
            // its actual scheduled and started timestamps, so we must not fabricate
            // them. RecentActions is ordered oldest-first, so take the most recent.
            var recentActions = description.Info?.RecentActions?.ToList()
                ?? new List<Temporalio.Client.Schedules.ScheduleActionResult>();
            
            foreach (var action in recentActions.OrderByDescending(a => a.ScheduledAt).Take(count))
            {
                var startWorkflow = action.Action as Temporalio.Client.Schedules.ScheduleActionExecutionStartWorkflow;
                
                historyRuns.Add(new ScheduleRunModel
                {
                    RunId = startWorkflow?.FirstExecutionRunId
                        ?? $"{scheduleId}-history-{action.ScheduledAt:yyyyMMddHHmmss}",
                    ScheduleId = scheduleId,
                    ScheduledTime = action.ScheduledAt,
                    ActualRunTime = action.StartedAt,
                    // A recorded action means the workflow was started. Temporal's
                    // schedule info does not include the workflow's final outcome.
                    Status = ScheduleRunStatus.Completed,
                    WorkflowRunId = startWorkflow?.FirstExecutionRunId
                });
            }
            
            _logger.LogInformation("Retrieved {Count} history runs for schedule {ScheduleId}", historyRuns.Count, LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<List<ScheduleRunModel>>.Success(historyRuns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get schedule history for schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
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
            
            _logger.LogInformation("Retrieved schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<ScheduleModel>.Success(schedule);
        }
        catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("NotFound"))
        {
            _logger.LogWarning("Schedule {ScheduleId} not found", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<ScheduleModel>.NotFound($"Schedule {scheduleId} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
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
        
        // Use Temporal's real next action time; fall back to estimation only when
        // Temporal didn't provide one (e.g. paused/exhausted schedules).
        var temporalNextTimes = description.Info?.NextActionTimes;
        var nextRunTime = temporalNextTimes != null && temporalNextTimes.Count > 0
            ? temporalNextTimes.First()
            : CalculateNextRunTime(description.Schedule.Spec);
        
        // Last run time comes from the most recent recorded action's actual start
        // time — never an estimate.
        var recentActions = description.Info?.RecentActions?.ToList()
            ?? new List<Temporalio.Client.Schedules.ScheduleActionResult>();
        DateTime? lastRunTime = recentActions.Count > 0
            ? recentActions.Max(a => a.StartedAt)
            : null;
        
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
                    LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(readPermissionResult.ErrorMessage));
                return false;
            }
            
            return readPermissionResult.Data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking schedule access for agent {AgentName}", LogSanitizer.Sanitize(agentName));
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
                if (startWorkflow.Options?.Memo?.TryGetValue("tenantId", out var memoValue) == true)
                {
                    if (memoValue is Temporalio.Converters.IEncodedRawValue encodedValue)
                    {
                        return encodedValue?.Payload?.Data?.ToStringUtf8()?.Replace("\"", "");
                    }
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
                if (startWorkflow.Options?.Memo?.TryGetValue("agent", out var memoValue) == true)
                {
                    if (memoValue is Temporalio.Converters.IEncodedRawValue encodedValue)
                    {
                        return encodedValue?.Payload?.Data?.ToStringUtf8()?.Replace("\"", "");
                    }
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
                if (startWorkflow.Options?.Memo?.TryGetValue("description", out var memoValue) == true)
                {
                    if (memoValue is Temporalio.Converters.IEncodedRawValue encodedValue)
                    {
                        return encodedValue?.Payload?.Data?.ToStringUtf8()?.Replace("\"", "");
                    }
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
                        if (kvp.Key != "agent" && kvp.Key != "description" && kvp.Key != "tenantId")
                        {
                            if (kvp.Value is Temporalio.Converters.IEncodedRawValue encodedValue)
                            {
                                var decodedValue = encodedValue?.Payload?.Data?.ToStringUtf8()?.Replace("\"", "") ?? "";
                                metadata[kvp.Key] = decodedValue;
                            }
                            else
                            {
                                metadata[kvp.Key] = kvp.Value ?? "";
                            }
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
        // Produce a compact, human/machine-friendly representation of the spec.
        // The raw ScheduleSpec.ToString() only emits collection type names
        // (e.g. "Calendars = System.Collections.Generic.List`1[...]"), which is
        // useless for display, so we format the meaningful parts ourselves.
        try
        {
            var parts = new List<string>();

            if (spec.CronExpressions?.Any() == true)
            {
                parts.AddRange(spec.CronExpressions.Where(c => !string.IsNullOrWhiteSpace(c)));
            }

            if (spec.Intervals?.Any() == true)
            {
                foreach (var interval in spec.Intervals)
                {
                    var desc = $"Every {FormatTimeSpan(interval.Every)}";
                    if (interval.Offset is { } offset && offset != TimeSpan.Zero)
                    {
                        desc += $" (offset {FormatTimeSpan(offset)})";
                    }
                    parts.Add(desc);
                }
            }

            if (spec.Calendars?.Any() == true)
            {
                parts.AddRange(spec.Calendars.Select(FormatCalendarSpec));
            }

            var result = string.Join("; ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
            return string.IsNullOrWhiteSpace(result) ? "Unknown schedule" : result;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to format schedule spec");
            return "Unknown schedule";
        }
    }

    /// <summary>
    /// Formats a <see cref="TimeSpan"/> as a compact duration like "1d 2h 30m".
    /// </summary>
    private static string FormatTimeSpan(TimeSpan ts)
    {
        if (ts <= TimeSpan.Zero) return "0s";

        var segments = new List<string>();
        if (ts.Days > 0) segments.Add($"{ts.Days}d");
        if (ts.Hours > 0) segments.Add($"{ts.Hours}h");
        if (ts.Minutes > 0) segments.Add($"{ts.Minutes}m");
        if (ts.Seconds > 0) segments.Add($"{ts.Seconds}s");
        return segments.Count > 0 ? string.Join(" ", segments) : "0s";
    }

    /// <summary>
    /// Formats a calendar spec as a standard 5-field cron expression
    /// ("minute hour day-of-month month day-of-week") so the client can render
    /// it in a friendly way.
    /// </summary>
    private static string FormatCalendarSpec(Temporalio.Client.Schedules.ScheduleCalendarSpec calendar)
    {
        var minute = FormatRanges(calendar.Minute, "0");
        var hour = FormatRanges(calendar.Hour, "0");
        var dayOfMonth = FormatRanges(calendar.DayOfMonth, "*");
        var month = FormatRanges(calendar.Month, "*");
        var dayOfWeek = FormatRanges(calendar.DayOfWeek, "*");
        return $"{minute} {hour} {dayOfMonth} {month} {dayOfWeek}";
    }

    /// <summary>
    /// Formats a collection of <see cref="Temporalio.Client.Schedules.ScheduleRange"/>
    /// values into a cron field. Falls back to <paramref name="defaultValue"/> when empty.
    /// </summary>
    private static string FormatRanges(
        IReadOnlyCollection<Temporalio.Client.Schedules.ScheduleRange>? ranges,
        string defaultValue)
    {
        if (ranges == null || ranges.Count == 0) return defaultValue;
        return string.Join(",", ranges.Select(FormatRange));
    }

    private static string FormatRange(Temporalio.Client.Schedules.ScheduleRange range)
    {
        var isSingle = range.End <= range.Start;
        var basePart = isSingle ? range.Start.ToString() : $"{range.Start}-{range.End}";
        return range.Step > 1 ? $"{basePart}/{range.Step}" : basePart;
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
            _logger.LogWarning(ex, "Failed to parse cron expression: {CronExpression}", LogSanitizer.Sanitize(cronExpression));
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
    
    public async Task<ServiceResult<bool>> DeleteScheduleByIdAsync(string scheduleId)
    {
        if (string.IsNullOrWhiteSpace(scheduleId))
        {
            return ServiceResult<bool>.BadRequest("Schedule ID is required");
        }
        
        try
        {
            var client = await _clientFactory.GetClientAsync();
            
            // First, get the schedule to verify permissions and tenant
            var scheduleHandle = client.GetScheduleHandle(scheduleId);

            // Security check: Tenant isolation and agent write permission
            var authResult = await AuthorizeScheduleWriteAsync(scheduleHandle, scheduleId, "delete");
            if (!authResult.IsSuccess)
            {
                return ServiceResult<bool>.Failure(authResult.ErrorMessage!, authResult.StatusCode);
            }
            
            // Delete the schedule
            await scheduleHandle.DeleteAsync();
            
            _logger.LogInformation("Successfully deleted schedule {ScheduleId} for agent {AgentName}", 
                LogSanitizer.Sanitize(scheduleId), LogSanitizer.Sanitize(authResult.Data!.AgentName));
            
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("NotFound"))
        {
            _logger.LogWarning("Schedule {ScheduleId} not found", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<bool>.NotFound($"Schedule {scheduleId} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<bool>.InternalServerError($"Failed to delete schedule: {ex.Message}");
        }
    }

    public async Task<ServiceResult<bool>> PauseScheduleAsync(string scheduleId, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(scheduleId))
        {
            return ServiceResult<bool>.BadRequest("Schedule ID is required");
        }

        try
        {
            var client = await _clientFactory.GetClientAsync();
            var scheduleHandle = client.GetScheduleHandle(scheduleId);

            // Security check: Tenant isolation and agent write permission
            var authResult = await AuthorizeScheduleWriteAsync(scheduleHandle, scheduleId, "pause");
            if (!authResult.IsSuccess)
            {
                return ServiceResult<bool>.Failure(authResult.ErrorMessage!, authResult.StatusCode);
            }

            await scheduleHandle.PauseAsync(string.IsNullOrWhiteSpace(note) ? "Paused via API" : note);

            _logger.LogInformation("Successfully paused schedule {ScheduleId} for agent {AgentName}",
                LogSanitizer.Sanitize(scheduleId), LogSanitizer.Sanitize(authResult.Data!.AgentName));

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("NotFound"))
        {
            _logger.LogWarning("Schedule {ScheduleId} not found", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<bool>.NotFound($"Schedule {scheduleId} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<bool>.InternalServerError($"Failed to pause schedule: {ex.Message}");
        }
    }

    public async Task<ServiceResult<bool>> ResumeScheduleAsync(string scheduleId, string? note = null)
    {
        if (string.IsNullOrWhiteSpace(scheduleId))
        {
            return ServiceResult<bool>.BadRequest("Schedule ID is required");
        }

        try
        {
            var client = await _clientFactory.GetClientAsync();
            var scheduleHandle = client.GetScheduleHandle(scheduleId);

            // Security check: Tenant isolation and agent write permission
            var authResult = await AuthorizeScheduleWriteAsync(scheduleHandle, scheduleId, "resume");
            if (!authResult.IsSuccess)
            {
                return ServiceResult<bool>.Failure(authResult.ErrorMessage!, authResult.StatusCode);
            }

            await scheduleHandle.UnpauseAsync(string.IsNullOrWhiteSpace(note) ? "Resumed via API" : note);

            _logger.LogInformation("Successfully resumed schedule {ScheduleId} for agent {AgentName}",
                LogSanitizer.Sanitize(scheduleId), LogSanitizer.Sanitize(authResult.Data!.AgentName));

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex) when (ex.Message.Contains("not found") || ex.Message.Contains("NotFound"))
        {
            _logger.LogWarning("Schedule {ScheduleId} not found", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<bool>.NotFound($"Schedule {scheduleId} not found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
            return ServiceResult<bool>.InternalServerError($"Failed to resume schedule: {ex.Message}");
        }
    }

    /// <summary>
    /// Verifies the current caller may modify the given schedule: the schedule must belong to the
    /// caller's tenant and the caller must have write permission for the schedule's agent.
    /// Returns the mapped schedule on success, or a failed result describing the error.
    /// </summary>
    /// <param name="scheduleHandle">Handle used to describe the schedule.</param>
    /// <param name="scheduleId">Schedule ID (used for logging).</param>
    /// <param name="operation">Human-readable operation name used in error messages and logs (e.g. "pause").</param>
    private async Task<ServiceResult<ScheduleModel>> AuthorizeScheduleWriteAsync(
        Temporalio.Client.Schedules.ScheduleHandle scheduleHandle,
        string scheduleId,
        string operation)
    {
        var description = await scheduleHandle.DescribeAsync();
        var scheduleModel = MapToScheduleModel(scheduleId, description);

        // Security check: Tenant isolation
        if (!string.IsNullOrEmpty(scheduleModel.TenantId) &&
            scheduleModel.TenantId != _tenantContext.TenantId)
        {
            _logger.LogWarning("Attempted to {Operation} schedule {ScheduleId} from different tenant. Schedule tenant: {ScheduleTenant}, Current tenant: {CurrentTenant}",
                operation, LogSanitizer.Sanitize(scheduleId), LogSanitizer.Sanitize(scheduleModel.TenantId), LogSanitizer.Sanitize(_tenantContext.TenantId));
            return ServiceResult<ScheduleModel>.Forbidden($"You do not have permission to {operation} this schedule");
        }

        // Security check: Agent write permission
        if (!string.IsNullOrEmpty(scheduleModel.AgentName))
        {
            var hasWritePermission = await _permissionsService.HasWritePermission(scheduleModel.AgentName);
            if (!hasWritePermission.IsSuccess || !hasWritePermission.Data)
            {
                _logger.LogWarning("User lacks write permission for agent {AgentName} when attempting to {Operation} schedule {ScheduleId}",
                    LogSanitizer.Sanitize(scheduleModel.AgentName), operation, LogSanitizer.Sanitize(scheduleId));
                return ServiceResult<ScheduleModel>.Forbidden($"You do not have permission to {operation} schedules for agent {scheduleModel.AgentName}");
            }
        }

        return ServiceResult<ScheduleModel>.Success(scheduleModel);
    }
    
    public async Task<ServiceResult<ScheduleDeleteResult>> DeleteAllSchedulesByAgentAsync(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            return ServiceResult<ScheduleDeleteResult>.BadRequest("Agent name is required");
        }
        
        try
        {
            // Security check: Verify user has write permission for this agent
            var hasWritePermission = await _permissionsService.HasWritePermission(agentName);
            if (!hasWritePermission.IsSuccess || !hasWritePermission.Data)
            {
                _logger.LogWarning("User lacks write permission for agent {AgentName}", LogSanitizer.Sanitize(agentName));
                return ServiceResult<ScheduleDeleteResult>.Forbidden($"You do not have permission to delete schedules for agent {agentName}");
            }
            
            // Verify agent exists in current tenant
            var agent = await _agentRepository.GetByNameAsync(
                agentName, 
                _tenantContext.TenantId, 
                _tenantContext.LoggedInUser, 
                _tenantContext.UserRoles.ToArray());
            
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {TenantId}", LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(_tenantContext.TenantId));
                return ServiceResult<ScheduleDeleteResult>.NotFound($"Agent {agentName} not found");
            }
            
            var client = await _clientFactory.GetClientAsync();
            var result = new ScheduleDeleteResult();
            
            // Build query to find all schedules for this agent in the current tenant
            var queryParts = new List<string>();
            
            // Mandatory: Filter by tenant ID for tenant isolation
            if (!string.IsNullOrEmpty(_tenantContext.TenantId))
            {
                queryParts.Add($"tenantId = '{_tenantContext.TenantId}'");
            }
            
            // Filter by agent name
            queryParts.Add($"agent = '{agentName}'");
            
            var query = string.Join(" AND ", queryParts);
            
            _logger.LogInformation("Deleting all schedules for agent {AgentName} with query: {Query}", LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(query));
            
            var listOptions = !string.IsNullOrEmpty(query) 
                ? new Temporalio.Client.Schedules.ScheduleListOptions { Query = query }
                : null;
            
            // Collect all schedule IDs first
            var scheduleIds = new List<string>();
            await foreach (var schedule in client.ListSchedulesAsync(listOptions))
            {
                scheduleIds.Add(schedule.Id);
            }
            
            _logger.LogInformation("Found {Count} schedules to delete for agent {AgentName}", scheduleIds.Count, LogSanitizer.Sanitize(agentName));
            
            // Delete each schedule
            foreach (var scheduleId in scheduleIds)
            {
                try
                {
                    var scheduleHandle = client.GetScheduleHandle(scheduleId);
                    await scheduleHandle.DeleteAsync();
                    
                    result.DeletedScheduleIds.Add(scheduleId);
                    result.DeletedCount++;
                    
                    _logger.LogInformation("Successfully deleted schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
                    result.FailedScheduleIds.Add(scheduleId);
                }
            }
            
            _logger.LogInformation("Deleted {DeletedCount} schedules for agent {AgentName}. Failed: {FailedCount}", 
                result.DeletedCount, LogSanitizer.Sanitize(agentName), result.FailedScheduleIds.Count);
            
            return ServiceResult<ScheduleDeleteResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete schedules for agent {AgentName}", LogSanitizer.Sanitize(agentName));
            return ServiceResult<ScheduleDeleteResult>.InternalServerError($"Failed to delete schedules: {ex.Message}");
        }
    }
    
    public async Task<ServiceResult<ScheduleDeleteResult>> DeleteAllSchedulesAsync()
    {
        try
        {
            var client = await _clientFactory.GetClientAsync();
            var result = new ScheduleDeleteResult();

            var queryParts = new List<string>();

            if (!string.IsNullOrEmpty(_tenantContext.TenantId))
            {
                queryParts.Add($"tenantId = '{_tenantContext.TenantId}'");
            }

            var query = queryParts.Count > 0 ? string.Join(" AND ", queryParts) : string.Empty;

            _logger.LogInformation("Deleting all schedules for tenant {TenantId}", LogSanitizer.Sanitize(_tenantContext.TenantId));

            var listOptions = !string.IsNullOrEmpty(query)
                ? new Temporalio.Client.Schedules.ScheduleListOptions { Query = query }
                : null;

            var scheduleIds = new List<string>();
            await foreach (var schedule in client.ListSchedulesAsync(listOptions))
            {
                scheduleIds.Add(schedule.Id);
            }

            _logger.LogInformation("Found {Count} schedules to delete for tenant {TenantId}", scheduleIds.Count, LogSanitizer.Sanitize(_tenantContext.TenantId));

            foreach (var scheduleId in scheduleIds)
            {
                try
                {
                    var scheduleHandle = client.GetScheduleHandle(scheduleId);
                    await scheduleHandle.DeleteAsync();

                    result.DeletedScheduleIds.Add(scheduleId);
                    result.DeletedCount++;

                    _logger.LogInformation("Successfully deleted schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete schedule {ScheduleId}", LogSanitizer.Sanitize(scheduleId));
                    result.FailedScheduleIds.Add(scheduleId);
                }
            }

            _logger.LogInformation("Deleted {DeletedCount} schedules for tenant {TenantId}. Failed: {FailedCount}",
                result.DeletedCount, LogSanitizer.Sanitize(_tenantContext.TenantId), result.FailedScheduleIds.Count);

            return ServiceResult<ScheduleDeleteResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete all schedules for tenant {TenantId}", LogSanitizer.Sanitize(_tenantContext.TenantId));
            return ServiceResult<ScheduleDeleteResult>.InternalServerError($"Failed to delete all schedules: {ex.Message}");
        }
    }
    
}