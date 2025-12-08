using Microsoft.Extensions.Logging;
using Shared.Data.Models.Usage;
using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Service for retrieving and processing usage statistics.
/// </summary>
public interface IUsageStatisticsService
{
    Task<UsageStatisticsResponse> GetUsageStatisticsAsync(
        UsageStatisticsRequest request, 
        CancellationToken cancellationToken = default);
    
    Task<List<UserListItem>> GetUsersWithUsageAsync(
        string tenantId, 
        CancellationToken cancellationToken = default);
}

public class UsageStatisticsService : IUsageStatisticsService
{
    private readonly ITokenUsageEventRepository _repository;
    private readonly ILogger<UsageStatisticsService> _logger;

    public UsageStatisticsService(
        ITokenUsageEventRepository repository,
        ILogger<UsageStatisticsService> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<UsageStatisticsResponse> GetUsageStatisticsAsync(
        UsageStatisticsRequest request, 
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        _logger.LogInformation(
            "Retrieving {Type} statistics for tenant={TenantId}, user={UserId}, range={StartDate} to {EndDate}, groupBy={GroupBy}",
            request.Type, request.TenantId, request.UserId ?? "all", request.StartDate, request.EndDate, request.GroupBy);

        var stats = await _repository.GetUsageStatisticsAsync(
            request.TenantId,
            request.UserId,
            request.AgentName,
            request.Type,
            request.StartDate,
            request.EndDate,
            request.GroupBy,
            cancellationToken);

        _logger.LogInformation(
            "Retrieved {Type} statistics: primaryCount={PrimaryCount}, requests={RequestCount}, users={UserCount}",
            request.Type, stats.TotalMetrics.PrimaryCount, stats.TotalMetrics.RequestCount, stats.UserBreakdown.Count);

        return stats;
    }

    public async Task<List<UserListItem>> GetUsersWithUsageAsync(
        string tenantId, 
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID is required", nameof(tenantId));
        }

        _logger.LogInformation("Retrieving users with usage for tenant={TenantId}", tenantId);

        var users = await _repository.GetUsersWithUsageAsync(tenantId, cancellationToken);

        _logger.LogInformation("Retrieved {UserCount} users with usage", users.Count);

        return users;
    }

    private static void ValidateRequest(UsageStatisticsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            throw new ArgumentException("Tenant ID is required", nameof(request.TenantId));
        }

        if (request.StartDate >= request.EndDate)
        {
            throw new ArgumentException("End date must be after start date", nameof(request.EndDate));
        }

        var dateRange = (request.EndDate - request.StartDate).Days;
        if (dateRange > 90)
        {
            throw new ArgumentException("Date range cannot exceed 90 days", nameof(request.EndDate));
        }

        var validGroupBy = new[] { "hour", "day", "week", "month" };
        if (!validGroupBy.Contains(request.GroupBy.ToLower()))
        {
            throw new ArgumentException("GroupBy must be 'hour', 'day', 'week', or 'month'", nameof(request.GroupBy));
        }
    }

}

