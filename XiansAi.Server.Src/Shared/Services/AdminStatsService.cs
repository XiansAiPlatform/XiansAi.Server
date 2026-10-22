using Shared.Repositories;
using Shared.Utils.Services;
using Shared.Utils;

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

/// <summary>
/// Tenant-wide totals for the per-user usage report.
/// </summary>
public class UsageTotals
{
    public required int Users { get; set; }
    public required int Agents { get; set; }
    public required int Tasks { get; set; }
}

/// <summary>
/// Usage for one user (the creator of the agent activations).
/// </summary>
public class UserUsage
{
    public required string UserId { get; set; }
    public required string DisplayName { get; set; }
    public required int AgentCount { get; set; }
    public required int TasksCreated { get; set; }
}

/// <summary>
/// Response model for per-user usage statistics.
/// </summary>
public class UserUsageStatsResponse
{
    public required string TenantId { get; set; }
    public required string From { get; set; }
    public required string To { get; set; }
    public required UsageTotals Totals { get; set; }
    public required List<UserUsage> Users { get; set; }
}

public interface IAdminStatsService
{
    Task<ServiceResult<AdminStatsResponse>> GetStatsAsync(
        string tenantId,
        DateTime? startDate,
        DateTime? endDate,
        string? participantId);

    Task<ServiceResult<UserUsageStatsResponse>> GetUserStatsAsync(
        string tenantId,
        DateTime? startDate,
        DateTime? endDate);
}

/// <summary>
/// Service for retrieving aggregated statistics across different admin domains.
/// Combines task statistics from Temporal and messaging statistics from MongoDB.
/// </summary>
public class AdminStatsService : IAdminStatsService
{
    private readonly IAdminTaskService _taskService;
    private readonly IConversationRepository _conversationRepository;
    private readonly IActivationRepository _activationRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<AdminStatsService> _logger;

    public const string UnknownUserId = "unknown";

    public AdminStatsService(
        IAdminTaskService taskService,
        IConversationRepository conversationRepository,
        IActivationRepository activationRepository,
        IUserRepository userRepository,
        ILogger<AdminStatsService> logger)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _conversationRepository = conversationRepository ?? throw new ArgumentNullException(nameof(conversationRepository));
        _activationRepository = activationRepository ?? throw new ArgumentNullException(nameof(activationRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
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
                _logger.LogError("Failed to retrieve task statistics: {Error}", LogSanitizer.Sanitize(taskStatsResult.ErrorMessage));
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
            _logger.LogError(ex, "Failed to retrieve admin stats. Error: {ErrorMessage}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<AdminStatsResponse>.InternalServerError("Failed to retrieve admin statistics");
        }
    }

    /// <summary>
    /// Retrieves per-user agent and task counts for a tenant. A user is the creator of the agent activation.
    /// Both dates are inclusive (whole days).
    /// </summary>
    public async Task<ServiceResult<UserUsageStatsResponse>> GetUserStatsAsync(
        string tenantId,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return ServiceResult<UserUsageStatsResponse>.BadRequest("TenantId cannot be empty");
        }

        if (!startDate.HasValue || !endDate.HasValue)
        {
            return ServiceResult<UserUsageStatsResponse>.BadRequest("StartDate and EndDate are required");
        }

        var from = startDate.Value.Date;
        var to = endDate.Value.Date;
        if (from > to)
        {
            return ServiceResult<UserUsageStatsResponse>.BadRequest("StartDate cannot be after EndDate");
        }

        if (to >= DateTime.MaxValue.Date)
        {
            return ServiceResult<UserUsageStatsResponse>.BadRequest("EndDate is out of range");
        }

        // Bounds the Temporal task scan, which lists every task in the range.
        if (to > from.AddYears(1))
        {
            return ServiceResult<UserUsageStatsResponse>.BadRequest("Date range cannot exceed 1 year");
        }
        var toExclusive = to.AddDays(1);

        try
        {
            _logger.LogInformation(
                "Retrieving user stats - TenantId: {TenantId}, StartDate: {StartDate}, EndDate: {EndDate}",
                LogSanitizer.Sanitize(tenantId), from, to);

            var taskCountsResult = await _taskService.GetTaskCountsByActivation(tenantId, from, toExclusive);
            if (!taskCountsResult.IsSuccess)
            {
                _logger.LogError("Failed to retrieve task counts: {Error}", LogSanitizer.Sanitize(taskCountsResult.ErrorMessage));
                return ServiceResult<UserUsageStatsResponse>.InternalServerError("Failed to retrieve task statistics");
            }

            var activations = await _activationRepository.GetByTenantIdAsync(tenantId);
            var creatorByActivation = new Dictionary<(string, string), string>();
            foreach (var activation in activations)
            {
                creatorByActivation[(activation.AgentName, activation.Name)] = activation.CreatedBy;
            }

            var agentCounts = activations
                .Where(a => a.CreatedAt >= from && a.CreatedAt < toExclusive)
                .GroupBy(a => a.CreatedBy)
                .ToDictionary(g => g.Key, g => g.Count());

            var taskCounts = new Dictionary<string, int>();
            foreach (var (key, count) in taskCountsResult.Data!)
            {
                var userId = creatorByActivation.GetValueOrDefault(key) ?? UnknownUserId;
                taskCounts[userId] = taskCounts.GetValueOrDefault(userId) + count;
            }

            // Every tenant member is listed, including those with no activity in the range.
            var members = (await _userRepository.GetByTenantIdAsync(tenantId))
                .GroupBy(u => u.UserId)
                .ToDictionary(g => g.Key, g => g.First());

            var users = (await Task.WhenAll(members.Keys.Union(agentCounts.Keys).Union(taskCounts.Keys).Select(async userId => new UserUsage
                {
                    UserId = userId,
                    DisplayName = members.TryGetValue(userId, out var member) && !string.IsNullOrWhiteSpace(member.Name)
                        ? member.Name
                        : await GetDisplayNameAsync(userId),
                    AgentCount = agentCounts.GetValueOrDefault(userId),
                    TasksCreated = taskCounts.GetValueOrDefault(userId)
                })))
                .OrderByDescending(u => u.TasksCreated)
                .ThenBy(u => u.DisplayName)
                .ToList();

            var response = new UserUsageStatsResponse
            {
                TenantId = tenantId,
                From = from.ToString("yyyy-MM-dd"),
                To = to.ToString("yyyy-MM-dd"),
                Totals = new UsageTotals
                {
                    // Tenant members only; creators who are no longer members (or "unknown") are listed but not counted.
                    Users = members.Count,
                    Agents = users.Sum(u => u.AgentCount),
                    Tasks = users.Sum(u => u.TasksCreated)
                },
                Users = users
            };

            return ServiceResult<UserUsageStatsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve user stats. Error: {ErrorMessage}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<UserUsageStatsResponse>.InternalServerError("Failed to retrieve user statistics");
        }
    }

    /// <summary>
    /// Resolves a user's display name, falling back to the userId when the user is unknown.
    /// </summary>
    private async Task<string> GetDisplayNameAsync(string userId)
    {
        if (userId == UnknownUserId)
        {
            return "Unknown";
        }

        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            return string.IsNullOrWhiteSpace(user?.Name) ? userId : user!.Name;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to look up user {UserId}", LogSanitizer.Sanitize(userId));
            return userId;
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
            _logger.LogError(ex, "Failed to retrieve messaging statistics. Error: {ErrorMessage}", LogSanitizer.Sanitize(ex.Message));
            
            // Return zero stats on error rather than failing the entire request
            return new MessagingStatsData
            {
                ActiveUsers = 0,
                TotalMessages = 0
            };
        }
    }
}
