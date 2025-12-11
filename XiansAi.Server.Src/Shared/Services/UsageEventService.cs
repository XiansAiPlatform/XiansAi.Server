using Microsoft.Extensions.Logging;
using Shared.Configuration;
using Shared.Data.Models.Usage;
using Shared.Repositories;

namespace Shared.Services;

/// <summary>
/// Service for recording usage events and retrieving usage statistics.
/// </summary>
public interface IUsageEventService
{
    /// <summary>
    /// Records a usage event.
    /// </summary>
    Task RecordAsync(UsageEventRecord record, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Retrieves usage events statistics for the specified request.
    /// </summary>
    Task<UsageEventsResponse> GetUsageEventsAsync(
        UsageEventsRequest request, 
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a list of users who have usage records.
    /// </summary>
    Task<List<UserListItem>> GetUsersWithUsageAsync(
        string tenantId, 
        CancellationToken cancellationToken = default);
}

public class UsageEventService : IUsageEventService
{
    private readonly IUsageEventRepository _repository;
    private readonly UsageEventsOptions _options;
    private readonly ILogger<UsageEventService> _logger;

    public UsageEventService(
        IUsageEventRepository repository,
        UsageEventsOptions options,
        ILogger<UsageEventService> logger)
    {
        _repository = repository;
        _options = options;
        _logger = logger;
    }

    public async Task RecordAsync(UsageEventRecord record, CancellationToken cancellationToken = default)
    {
        if (!_options.RecordUsageEvents)
        {
            return;
        }

        if (record.TotalTokens == 0)
        {
            return;
        }

        _logger.LogInformation(
            "Recording token usage: tenant={TenantId}, user={UserId}, totalTokens={TotalTokens}, prompt={PromptTokens}, completion={CompletionTokens}, responseTimeMs={ResponseTimeMs}",
            record.TenantId,
            record.UserId,
            record.TotalTokens,
            record.PromptTokens,
            record.CompletionTokens,
            record.ResponseTimeMs);

        var usageEvent = new UsageEvent
        {
            TenantId = record.TenantId,
            UserId = record.UserId,
            Model = record.Model,
            PromptTokens = record.PromptTokens,
            CompletionTokens = record.CompletionTokens,
            TotalTokens = record.TotalTokens,
            MessageCount = record.MessageCount,
            WorkflowId = record.WorkflowId,
            RequestId = record.RequestId,
            Source = record.Source,
            Metadata = record.Metadata,
            ResponseTimeMs = record.ResponseTimeMs,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.InsertAsync(usageEvent, cancellationToken);
    }

    public async Task<UsageEventsResponse> GetUsageEventsAsync(
        UsageEventsRequest request, 
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        _logger.LogInformation(
            "Retrieving {Type} statistics for tenant={TenantId}, user={UserId}, range={StartDate} to {EndDate}, groupBy={GroupBy}",
            request.Type, request.TenantId, request.UserId ?? "all", request.StartDate, request.EndDate, request.GroupBy);

        var stats = await _repository.GetUsageEventsAsync(
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

    private static void ValidateRequest(UsageEventsRequest request)
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

