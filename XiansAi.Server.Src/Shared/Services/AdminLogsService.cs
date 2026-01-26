using Features.WebApi.Models;
using Features.WebApi.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

/// <summary>
/// Response model for paginated admin logs.
/// </summary>
public class AdminLogsResponse
{
    public required IEnumerable<Log> Logs { get; set; }
    public required long TotalCount { get; set; }
    public required int Page { get; set; }
    public required int PageSize { get; set; }
    public required int TotalPages { get; set; }
}

public interface IAdminLogsService
{
    Task<ServiceResult<AdminLogsResponse>> GetLogsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string? workflowId,
        string? workflowType,
        LogLevel? logLevel,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize);
}

/// <summary>
/// Service for retrieving logs for administrative purposes.
/// Provides flexible querying of workflow execution logs with comprehensive filtering.
/// </summary>
public class AdminLogsService : IAdminLogsService
{
    private readonly ILogRepository _logRepository;
    private readonly ILogger<AdminLogsService> _logger;

    public AdminLogsService(
        ILogRepository logRepository,
        ILogger<AdminLogsService> logger)
    {
        _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Retrieves logs for a tenant with comprehensive filtering options.
    /// </summary>
    public async Task<ServiceResult<AdminLogsResponse>> GetLogsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string? workflowId,
        string? workflowType,
        LogLevel? logLevel,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize)
    {
        // Validate required parameters
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Attempt to retrieve logs with empty tenantId");
            return ServiceResult<AdminLogsResponse>.BadRequest("TenantId cannot be empty");
        }

        // Validate pagination parameters
        if (page < 1)
        {
            _logger.LogWarning("Invalid page number: {Page}", page);
            return ServiceResult<AdminLogsResponse>.BadRequest("Page must be greater than 0");
        }

        if (pageSize < 1 || pageSize > 100)
        {
            _logger.LogWarning("Invalid page size: {PageSize}", pageSize);
            return ServiceResult<AdminLogsResponse>.BadRequest("PageSize must be between 1 and 100");
        }

        // Validate date range if both are provided
        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            _logger.LogWarning("Invalid date range: startDate {StartDate} is after endDate {EndDate}",
                startDate.Value, endDate.Value);
            return ServiceResult<AdminLogsResponse>.BadRequest("StartDate cannot be after EndDate");
        }

        try
        {
            _logger.LogInformation(
                "Retrieving admin logs - TenantId: {TenantId}, AgentName: {AgentName}, ActivationName: {ActivationName}, " +
                "ParticipantId: {ParticipantId}, WorkflowId: {WorkflowId}, WorkflowType: {WorkflowType}, " +
                "LogLevel: {LogLevel}, StartDate: {StartDate}, EndDate: {EndDate}, Page: {Page}, PageSize: {PageSize}",
                tenantId, agentName ?? "null", activationName ?? "null", participantId ?? "null",
                workflowId ?? "null", workflowType ?? "null", logLevel?.ToString() ?? "null",
                startDate?.ToString() ?? "null", endDate?.ToString() ?? "null", page, pageSize);

            // Get logs from repository using the admin-specific method
            var (logs, totalCount) = await _logRepository.GetAdminLogsAsync(
                tenantId,
                agentName,
                activationName,
                participantId,
                workflowId,
                workflowType,
                logLevel,
                startDate,
                endDate,
                page,
                pageSize);

            // Calculate total pages
            var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

            var response = new AdminLogsResponse
            {
                Logs = logs,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = totalPages
            };

            _logger.LogInformation(
                "Admin logs retrieved successfully - TotalCount: {TotalCount}, Page: {Page}/{TotalPages}",
                totalCount, page, totalPages);

            return ServiceResult<AdminLogsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve admin logs. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminLogsResponse>.InternalServerError("Failed to retrieve logs");
        }
    }
}
