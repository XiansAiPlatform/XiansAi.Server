using Shared.Models.Schedule;
using Shared.Utils.Temporal;
using Shared.Utils.Services;
using Temporalio.Client;
using System.Text.Json;

namespace Features.UserApi.Services;

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
            
            // For now, return mock data until Temporal schedule API is properly integrated
            // In a real implementation, you would use: client.ListSchedulesAsync()
            schedules = CreateMockSchedules();
            
            // Apply filters
            var filteredSchedules = schedules.Where(schedule => ShouldIncludeSchedule(schedule, request)).ToList();
            
            // Apply pagination
            var paginatedSchedules = ApplyPagination(filteredSchedules, request);
            
            _logger.LogInformation("Retrieved {Count} schedules", paginatedSchedules.Count);
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
            var upcomingRuns = new List<ScheduleRunModel>();
            
            // Generate mock upcoming runs for demo purposes
            var baseTime = DateTime.UtcNow;
            for (int i = 0; i < count; i++)
            {
                upcomingRuns.Add(new ScheduleRunModel
                {
                    RunId = $"{scheduleId}-upcoming-{i}",
                    ScheduleId = scheduleId,
                    ScheduledTime = baseTime.AddHours(i + 1),
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
            var historyRuns = new List<ScheduleRunModel>();
            
            // Generate mock history runs for demo purposes
            var baseTime = DateTime.UtcNow;
            for (int i = 0; i < Math.Min(count, 5); i++)
            {
                historyRuns.Add(new ScheduleRunModel
                {
                    RunId = $"{scheduleId}-history-{i}",
                    ScheduleId = scheduleId,
                    ScheduledTime = baseTime.AddHours(-i - 1),
                    ActualRunTime = baseTime.AddHours(-i - 1).AddMinutes(2),
                    Status = ScheduleRunStatus.Completed,
                    WorkflowRunId = $"workflow-{scheduleId}-{i}"
                });
            }
            
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
            
            // Create a mock schedule for demo purposes
            var mockSchedules = CreateMockSchedules();
            var schedule = mockSchedules.FirstOrDefault(s => s.Id == scheduleId);
            
            if (schedule == null)
            {
                _logger.LogWarning("Schedule {ScheduleId} not found", scheduleId);
                return ServiceResult<ScheduleModel>.NotFound($"Schedule {scheduleId} not found");
            }
            
            _logger.LogInformation("Retrieved schedule {ScheduleId}", scheduleId);
            return ServiceResult<ScheduleModel>.Success(schedule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get schedule {ScheduleId}", scheduleId);
            return ServiceResult<ScheduleModel>.InternalServerError($"Failed to retrieve schedule: {ex.Message}");
        }
    }
    
    private List<ScheduleModel> CreateMockSchedules()
    {
        return new List<ScheduleModel>
        {
            new ScheduleModel
            {
                Id = "daily-data-processor",
                AgentName = "DataProcessingAgent",
                WorkflowType = "ProcessDataWorkflow",
                ScheduleSpec = "0 0 * * *",
                NextRunTime = DateTime.UtcNow.AddDays(1).Date,
                CreatedAt = DateTime.UtcNow.AddDays(-30),
                Status = ScheduleStatus.Active,
                Description = "Daily data processing schedule",
                LastRunTime = DateTime.UtcNow.AddDays(-1).Date,
                ExecutionCount = 30,
                Metadata = new Dictionary<string, object>
                {
                    ["CreatedBy"] = "SchedulerHub",
                    ["Environment"] = "production"
                }
            },
            new ScheduleModel
            {
                Id = "hourly-health-check",
                AgentName = "MonitoringAgent",
                WorkflowType = "HealthCheckWorkflow",
                ScheduleSpec = "0 * * * *",
                NextRunTime = DateTime.UtcNow.AddHours(1),
                CreatedAt = DateTime.UtcNow.AddDays(-7),
                Status = ScheduleStatus.Active,
                Description = "Hourly system health check",
                LastRunTime = DateTime.UtcNow.AddHours(-1),
                ExecutionCount = 168,
                Metadata = new Dictionary<string, object>
                {
                    ["CreatedBy"] = "SchedulerHub",
                    ["Priority"] = "high"
                }
            },
            new ScheduleModel
            {
                Id = "weekly-report-gen",
                AgentName = "ReportingAgent",
                WorkflowType = "GenerateReportWorkflow",
                ScheduleSpec = "0 0 * * 1",
                NextRunTime = DateTime.UtcNow.AddDays(7 - (int)DateTime.UtcNow.DayOfWeek + 1),
                CreatedAt = DateTime.UtcNow.AddDays(-60),
                Status = ScheduleStatus.Paused,
                Description = "Weekly report generation - currently paused",
                LastRunTime = DateTime.UtcNow.AddDays(-7),
                ExecutionCount = 8,
                Metadata = new Dictionary<string, object>
                {
                    ["CreatedBy"] = "SchedulerHub",
                    ["Department"] = "Analytics"
                }
            }
        };
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
                !schedule.Id.ToLowerInvariant().Contains(searchTerm))
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