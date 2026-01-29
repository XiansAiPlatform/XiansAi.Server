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
    /// Records usage event with flexible metrics.
    /// </summary>
    Task RecordAsync(UsageReportRequest request, string tenantId, string participantId, CancellationToken cancellationToken = default);
    
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
    
    /// <summary>
    /// Gets available metrics for discovery (dynamic dashboard).
    /// </summary>
    Task<AvailableMetricsResponse> GetAvailableMetricsAsync(
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

    public async Task RecordAsync(
        UsageReportRequest request, 
        string tenantId, 
        string participantId, 
        CancellationToken cancellationToken = default)
    {
        if (!_options.RecordUsageEvents)
        {
            return;
        }

        if (request.Metrics == null || request.Metrics.Count == 0)
        {
            _logger.LogWarning("No metrics provided in flexible usage report");
            return;
        }

        _logger.LogInformation(
            "Recording flattened usage metrics: tenant={TenantId}, participant={ParticipantId}, agent={AgentName}, activation={ActivationName}, metricsCount={MetricsCount}",
            tenantId,
            participantId,
            request.AgentName,
            request.ActivationName,
            request.Metrics.Count);

        // Use agent name from request if provided, otherwise extract from workflow_id
        var agentName = request.AgentName ?? ExtractAgentName(request.WorkflowId);
        var now = DateTime.UtcNow;

        // Create multiple UsageMetric records (one per metric) - FLATTENED DESIGN
        var usageMetrics = request.Metrics.Select(m => new UsageMetric
        {
            TenantId = tenantId,
            ParticipantId = participantId,
            AgentName = agentName,
            ActivationName = request.ActivationName,
            WorkflowId = request.WorkflowId,
            RequestId = request.RequestId,
            WorkflowType = request.WorkflowType,
            Model = request.Model,
            Category = m.Category,
            Type = m.Type,
            Value = m.Value,
            Unit = m.Unit ?? "count",
            Metadata = request.Metadata,
            CreatedAt = now
        }).ToList();

        // Batch insert all metrics at once
        await _repository.InsertBatchAsync(usageMetrics, cancellationToken);
        
        _logger.LogInformation(
            "Inserted {Count} flattened usage metric records",
            usageMetrics.Count);
    }

    private static string? ExtractAgentName(string? workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
            return null;

        var parts = workflowId.Split(':');
        if (parts.Length >= 3)
        {
            // Format: tenant:AgentName:FlowName
            return parts[1].Trim();
        }
        else if (parts.Length >= 2)
        {
            // Format: AgentName:FlowName (A2A context)
            return parts[0].Trim();
        }
        
        return null;
    }

    public async Task<UsageEventsResponse> GetUsageEventsAsync(
        UsageEventsRequest request, 
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        _logger.LogInformation(
            "Retrieving statistics for tenant={TenantId}, category={Category}, type={MetricType}, participant={ParticipantId}, range={StartDate} to {EndDate}, groupBy={GroupBy}",
            request.TenantId, request.Category ?? "all", request.MetricType ?? "all", request.ParticipantId ?? "all", request.StartDate, request.EndDate, request.GroupBy);

        var stats = await _repository.GetUsageEventsAsync(
            request.TenantId,
            request.ParticipantId,
            request.AgentName,
            request.Category,
            request.MetricType,
            request.StartDate,
            request.EndDate,
            request.GroupBy,
            cancellationToken);

        _logger.LogInformation(
            "Retrieved statistics: category={Category}, type={MetricType}, totalValue={TotalValue}, requests={RequestCount}, users={UserCount}",
            stats.Category, stats.MetricType, stats.TotalValue, stats.TotalMetrics.RequestCount, stats.UserBreakdown.Count);

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

    public async Task<AvailableMetricsResponse> GetAvailableMetricsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            throw new ArgumentException("Tenant ID is required", nameof(tenantId));
        }

        _logger.LogInformation("Retrieving available metrics for tenant={TenantId}", tenantId);

        var metrics = await _repository.GetAvailableMetricsAsync(tenantId, cancellationToken);

        _logger.LogInformation("Retrieved {CategoryCount} categories with available metrics", metrics.Categories.Count);

        return metrics;
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

