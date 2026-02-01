using Shared.Data.Models.Usage;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Service interface for admin metrics operations.
/// </summary>
public interface IAdminMetricsService
{
    /// <summary>
    /// Get aggregated metrics statistics for an agent.
    /// </summary>
    Task<ServiceResult<AdminMetricsStatsResponse>> GetMetricsStatsAsync(
        AdminMetricsStatsRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get time-series metrics data for charting and trend analysis.
    /// </summary>
    Task<ServiceResult<AdminMetricsTimeSeriesResponse>> GetMetricsTimeSeriesAsync(
        AdminMetricsTimeSeriesRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover available metric categories and types.
    /// </summary>
    Task<ServiceResult<AdminMetricsCategoriesResponse>> GetMetricsCategoriesAsync(
        AdminMetricsCategoriesRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for admin metrics operations.
/// Provides aggregated statistics, time-series data, and metric discovery.
/// All aggregations are performed at the database level for optimal performance.
/// </summary>
public class AdminMetricsService : IAdminMetricsService
{
    private readonly IUsageEventRepository _repository;
    private readonly ILogger<AdminMetricsService> _logger;

    // Validation constants
    private const int MaxDateRangeDays = 365;
    private static readonly string[] ValidGroupByValues = { "day", "week", "month" };
    private static readonly string[] ValidAggregationValues = { "sum", "avg", "min", "max", "count" };

    public AdminMetricsService(
        IUsageEventRepository repository,
        ILogger<AdminMetricsService> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ServiceResult<AdminMetricsStatsResponse>> GetMetricsStatsAsync(
        AdminMetricsStatsRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validationResult = ValidateStatsRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Getting metrics stats - TenantId: {TenantId}, AgentName: {AgentName}, Range: {StartDate} to {EndDate}",
                request.TenantId, request.AgentName, request.StartDate, request.EndDate);

            var response = await _repository.GetAdminMetricsStatsAsync(request, cancellationToken);

            _logger.LogInformation(
                "Metrics stats retrieved successfully - Records: {TotalRecords}, Categories: {Categories}, Types: {Types}",
                response.Summary.TotalMetricRecords, response.Summary.UniqueCategories, response.Summary.UniqueTypes);

            return ServiceResult<AdminMetricsStatsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve metrics stats. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminMetricsStatsResponse>.InternalServerError("Failed to retrieve metrics statistics");
        }
    }

    public async Task<ServiceResult<AdminMetricsTimeSeriesResponse>> GetMetricsTimeSeriesAsync(
        AdminMetricsTimeSeriesRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validationResult = ValidateTimeSeriesRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Getting metrics timeseries - TenantId: {TenantId}, AgentName: {AgentName}, Category: {Category}, Type: {Type}, GroupBy: {GroupBy}",
                request.TenantId, request.AgentName, request.Category, request.Type, request.GroupBy);

            var response = await _repository.GetAdminMetricsTimeSeriesAsync(request, cancellationToken);

            _logger.LogInformation(
                "Metrics timeseries retrieved successfully - DataPoints: {DataPoints}, TotalValue: {TotalValue}",
                response.Summary.DataPointCount, response.Summary.TotalValue);

            return ServiceResult<AdminMetricsTimeSeriesResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve metrics timeseries. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminMetricsTimeSeriesResponse>.InternalServerError("Failed to retrieve metrics timeseries");
        }
    }

    public async Task<ServiceResult<AdminMetricsCategoriesResponse>> GetMetricsCategoriesAsync(
        AdminMetricsCategoriesRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validationResult = ValidateCategoriesRequest(request);
        if (!validationResult.IsSuccess)
        {
            return validationResult;
        }

        try
        {
            _logger.LogInformation(
                "Getting metrics categories - TenantId: {TenantId}, AgentName: {AgentName}",
                request.TenantId, request.AgentName ?? "all");

            var response = await _repository.GetAdminMetricsCategoriesAsync(request, cancellationToken);

            _logger.LogInformation(
                "Metrics categories retrieved successfully - Categories: {Categories}, Types: {Types}",
                response.Summary.TotalCategories, response.Summary.TotalTypes);

            return ServiceResult<AdminMetricsCategoriesResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve metrics categories. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminMetricsCategoriesResponse>.InternalServerError("Failed to retrieve metrics categories");
        }
    }

    // Validation methods

    private ServiceResult<AdminMetricsStatsResponse> ValidateStatsRequest(AdminMetricsStatsRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogWarning("Metrics stats request with empty tenantId");
            return ServiceResult<AdminMetricsStatsResponse>.BadRequest("TenantId is required");
        }

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            _logger.LogWarning("Metrics stats request with empty agentName");
            return ServiceResult<AdminMetricsStatsResponse>.BadRequest("AgentName is required");
        }

        if (request.StartDate >= request.EndDate)
        {
            _logger.LogWarning(
                "Invalid date range: startDate {StartDate} is after or equal to endDate {EndDate}",
                request.StartDate, request.EndDate);
            return ServiceResult<AdminMetricsStatsResponse>.BadRequest("EndDate must be after StartDate");
        }

        var dateRange = (request.EndDate - request.StartDate).Days;
        if (dateRange > MaxDateRangeDays)
        {
            _logger.LogWarning(
                "Date range exceeds maximum: {DateRange} days (max: {MaxDays})",
                dateRange, MaxDateRangeDays);
            return ServiceResult<AdminMetricsStatsResponse>.BadRequest(
                $"Date range cannot exceed {MaxDateRangeDays} days");
        }

        return ServiceResult<AdminMetricsStatsResponse>.Success(null!);
    }

    private ServiceResult<AdminMetricsTimeSeriesResponse> ValidateTimeSeriesRequest(AdminMetricsTimeSeriesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogWarning("Metrics timeseries request with empty tenantId");
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest("TenantId is required");
        }

        if (string.IsNullOrWhiteSpace(request.AgentName))
        {
            _logger.LogWarning("Metrics timeseries request with empty agentName");
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest("AgentName is required");
        }

        if (string.IsNullOrWhiteSpace(request.Category))
        {
            _logger.LogWarning("Metrics timeseries request with empty category");
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest("Category is required");
        }

        if (string.IsNullOrWhiteSpace(request.Type))
        {
            _logger.LogWarning("Metrics timeseries request with empty type");
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest("Type is required");
        }

        if (request.StartDate >= request.EndDate)
        {
            _logger.LogWarning(
                "Invalid date range: startDate {StartDate} is after or equal to endDate {EndDate}",
                request.StartDate, request.EndDate);
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest("EndDate must be after StartDate");
        }

        var dateRange = (request.EndDate - request.StartDate).Days;
        if (dateRange > MaxDateRangeDays)
        {
            _logger.LogWarning(
                "Date range exceeds maximum: {DateRange} days (max: {MaxDays})",
                dateRange, MaxDateRangeDays);
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest(
                $"Date range cannot exceed {MaxDateRangeDays} days");
        }

        if (!ValidGroupByValues.Contains(request.GroupBy.ToLower()))
        {
            _logger.LogWarning("Invalid groupBy value: {GroupBy}", request.GroupBy);
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest(
                $"GroupBy must be one of: {string.Join(", ", ValidGroupByValues)}");
        }

        if (!ValidAggregationValues.Contains(request.Aggregation.ToLower()))
        {
            _logger.LogWarning("Invalid aggregation value: {Aggregation}", request.Aggregation);
            return ServiceResult<AdminMetricsTimeSeriesResponse>.BadRequest(
                $"Aggregation must be one of: {string.Join(", ", ValidAggregationValues)}");
        }

        return ServiceResult<AdminMetricsTimeSeriesResponse>.Success(null!);
    }

    private ServiceResult<AdminMetricsCategoriesResponse> ValidateCategoriesRequest(AdminMetricsCategoriesRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.TenantId))
        {
            _logger.LogWarning("Metrics categories request with empty tenantId");
            return ServiceResult<AdminMetricsCategoriesResponse>.BadRequest("TenantId is required");
        }

        if (request.StartDate.HasValue && request.EndDate.HasValue)
        {
            if (request.StartDate.Value >= request.EndDate.Value)
            {
                _logger.LogWarning(
                    "Invalid date range: startDate {StartDate} is after or equal to endDate {EndDate}",
                    request.StartDate.Value, request.EndDate.Value);
                return ServiceResult<AdminMetricsCategoriesResponse>.BadRequest("EndDate must be after StartDate");
            }

            var dateRange = (request.EndDate.Value - request.StartDate.Value).Days;
            if (dateRange > MaxDateRangeDays)
            {
                _logger.LogWarning(
                    "Date range exceeds maximum: {DateRange} days (max: {MaxDays})",
                    dateRange, MaxDateRangeDays);
                return ServiceResult<AdminMetricsCategoriesResponse>.BadRequest(
                    $"Date range cannot exceed {MaxDateRangeDays} days");
            }
        }

        return ServiceResult<AdminMetricsCategoriesResponse>.Success(null!);
    }
}
