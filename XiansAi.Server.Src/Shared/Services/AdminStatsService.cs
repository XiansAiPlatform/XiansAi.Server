using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Response model for task statistics.
/// </summary>
public class TaskStatsData
{
    public required int Pending { get; set; }
    public required int Completed { get; set; }
    public required int TimedOut { get; set; }
    public required int Cancelled { get; set; }
    public required int Total { get; set; }
}

/// <summary>
/// Response model for messaging statistics.
/// </summary>
public class MessagingStatsData
{
    public required int ActiveUsers { get; set; }
    public required int TotalMessages { get; set; }
}

/// <summary>
/// Response model for aggregated admin statistics.
/// </summary>
public class AdminStatsResponse
{
    public required TaskStatsData Tasks { get; set; }
    public required MessagingStatsData Messages { get; set; }
}

public interface IAdminStatsService
{
    Task<ServiceResult<AdminStatsResponse>> GetStatsAsync(
        string tenantId,
        DateTime? startDate,
        DateTime? endDate,
        string? participantId);
}

/// <summary>
/// Service for retrieving aggregated statistics across different admin domains.
/// Combines task statistics from Temporal and messaging statistics from MongoDB.
/// </summary>
public class AdminStatsService : IAdminStatsService
{
    private readonly IAdminTaskService _taskService;
    private readonly IConversationRepository _conversationRepository;
    private readonly ILogger<AdminStatsService> _logger;

    public AdminStatsService(
        IAdminTaskService taskService,
        IConversationRepository conversationRepository,
        ILogger<AdminStatsService> logger)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves aggregated statistics for a tenant within a date range.
    /// </summary>
    public async Task<ServiceResult<AdminStatsResponse>> GetStatsAsync(
        string tenantId,
        DateTime? startDate,
        DateTime? endDate,
        string? participantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Attempt to retrieve stats with empty tenantId");
            return ServiceResult<AdminStatsResponse>.BadRequest("TenantId cannot be empty");
        }

        if (!startDate.HasValue || !endDate.HasValue)
        {
            _logger.LogWarning("Attempt to retrieve stats without date range");
            return ServiceResult<AdminStatsResponse>.BadRequest("StartDate and EndDate are required");
        }

        if (startDate.Value > endDate.Value)
        {
            _logger.LogWarning("Invalid date range: startDate {StartDate} is after endDate {EndDate}", 
                startDate.Value, endDate.Value);
            return ServiceResult<AdminStatsResponse>.BadRequest("StartDate cannot be after EndDate");
        }

        try
        {
            _logger.LogInformation(
                "Retrieving admin stats - TenantId: {TenantId}, StartDate: {StartDate}, EndDate: {EndDate}, ParticipantId: {ParticipantId}",
                tenantId, startDate.Value, endDate.Value, participantId ?? "null");

            // Get task statistics from Temporal
            var taskStatsResult = await _taskService.GetTaskStatistics(tenantId, startDate, endDate, participantId);
            
            if (!taskStatsResult.IsSuccess)
            {
                _logger.LogError("Failed to retrieve task statistics: {Error}", taskStatsResult.ErrorMessage);
                return ServiceResult<AdminStatsResponse>.InternalServerError("Failed to retrieve task statistics");
            }

            // Get messaging statistics from MongoDB
            var messagingStats = await GetMessagingStatsAsync(tenantId, startDate.Value, endDate.Value, participantId);

            // Build aggregated response
            var response = new AdminStatsResponse
            {
                Tasks = new TaskStatsData
                {
                    Pending = taskStatsResult.Data!.Pending,
                    Completed = taskStatsResult.Data.Completed,
                    TimedOut = taskStatsResult.Data.TimedOut,
                    Cancelled = taskStatsResult.Data.Cancelled,
                    Total = taskStatsResult.Data.Total
                },
                Messages = messagingStats
            };

            _logger.LogInformation(
                "Admin stats retrieved successfully - Tasks: {TotalTasks}, ActiveUsers: {ActiveUsers}, TotalMessages: {TotalMessages}",
                response.Tasks.Total, response.Messages.ActiveUsers, response.Messages.TotalMessages);

            return ServiceResult<AdminStatsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve admin stats. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminStatsResponse>.InternalServerError("Failed to retrieve admin statistics");
        }
    }

    /// <summary>
    /// Retrieves messaging statistics from the conversation repository.
    /// </summary>
    private async Task<MessagingStatsData> GetMessagingStatsAsync(
        string tenantId,
        DateTime startDate,
        DateTime endDate,
        string? participantId)
    {
        try
        {
            var (totalMessages, activeUsers) = await _conversationRepository.GetMessagingStatsAsync(
                tenantId, 
                startDate, 
                endDate, 
                participantId);

            return new MessagingStatsData
            {
                ActiveUsers = activeUsers,
                TotalMessages = totalMessages
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve messaging statistics. Error: {ErrorMessage}", ex.Message);
            
            // Return zero stats on error rather than failing the entire request
            return new MessagingStatsData
            {
                ActiveUsers = 0,
                TotalMessages = 0
            };
        }
    }
}
