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

/// <summary>
/// A single log "stream" (group of logs that share a workflow_id).
/// </summary>
public class AdminLogStream
{
    public required string WorkflowId { get; set; }
    public string? WorkflowType { get; set; }
    public string? WorkflowRunId { get; set; }
    public required string Agent { get; set; }
    public string? Activation { get; set; }
    public string? ParticipantId { get; set; }
    public required DateTime LastLogAt { get; set; }
    public required DateTime FirstLogAt { get; set; }
    public required long LogCount { get; set; }
    public required LogLevel LastLogLevel { get; set; }
    public required string LastLogMessage { get; set; }
}

/// <summary>
/// Response model for paginated admin log streams.
/// </summary>
public class AdminLogStreamsResponse
{
    public required IEnumerable<AdminLogStream> Streams { get; set; }
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
        string[]? workflowIds,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize);

    Task<ServiceResult<AdminLogStreamsResponse>> GetLogStreamsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize);
}

/// <summary>
/// Service for retrieving logs for administrative purposes.
/// Provides flexible querying of workflow execution logs with comprehensive filtering,
/// plus a "streams" view that lists distinct workflow IDs sorted by recent activity.
/// </summary>
public class AdminLogsService : IAdminLogsService
{
    private const int MinPageSize = 1;
    private const int MaxPageSize = 100;

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
    /// Supports filtering by one or more workflow IDs (streams) for follow-up queries
    /// after listing streams via <see cref="GetLogStreamsAsync"/>.
    /// </summary>
    public async Task<ServiceResult<AdminLogsResponse>> GetLogsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string[]? workflowIds,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize)
    {
        var validation = ValidateCommonInputs(tenantId, page, pageSize, startDate, endDate);
        if (validation != null)
        {
            return ServiceResult<AdminLogsResponse>.BadRequest(validation);
        }

        try
        {
            _logger.LogInformation(
                "Retrieving admin logs - TenantId: {TenantId}, AgentName: {AgentName}, ActivationName: {ActivationName}, " +
                "ParticipantId: {ParticipantId}, WorkflowIds: {WorkflowIds}, WorkflowType: {WorkflowType}, " +
                "LogLevels: {LogLevels}, StartDate: {StartDate}, EndDate: {EndDate}, Page: {Page}, PageSize: {PageSize}",
                tenantId, agentName ?? "null", activationName ?? "null", participantId ?? "null",
                workflowIds != null ? string.Join(",", workflowIds) : "null",
                workflowType ?? "null",
                logLevels != null ? string.Join(",", logLevels.Select(l => l.ToString())) : "null",
                startDate?.ToString() ?? "null", endDate?.ToString() ?? "null", page, pageSize);

            var (logs, totalCount) = await _logRepository.GetAdminLogsAsync(
                tenantId,
                agentName,
                activationName,
                participantId,
                workflowIds,
                workflowType,
                logLevels,
                startDate,
                endDate,
                page,
                pageSize);

            var response = new AdminLogsResponse
            {
                Logs = logs,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = CalculateTotalPages(totalCount, pageSize)
            };

            _logger.LogInformation(
                "Admin logs retrieved successfully - TotalCount: {TotalCount}, Page: {Page}/{TotalPages}",
                totalCount, page, response.TotalPages);

            return ServiceResult<AdminLogsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve admin logs. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminLogsResponse>.InternalServerError("Failed to retrieve logs");
        }
    }

    /// <summary>
    /// Retrieves a paginated list of distinct log streams (unique workflow IDs) for a tenant,
    /// sorted by most recent log activity. This is intended as the first step of a two-step
    /// admin log query: list streams, then fetch logs filtered by selected workflow IDs.
    /// </summary>
    public async Task<ServiceResult<AdminLogStreamsResponse>> GetLogStreamsAsync(
        string tenantId,
        string? agentName,
        string? activationName,
        string? participantId,
        string? workflowType,
        LogLevel[]? logLevels,
        DateTime? startDate,
        DateTime? endDate,
        int page,
        int pageSize)
    {
        var validation = ValidateCommonInputs(tenantId, page, pageSize, startDate, endDate);
        if (validation != null)
        {
            return ServiceResult<AdminLogStreamsResponse>.BadRequest(validation);
        }

        try
        {
            _logger.LogInformation(
                "Retrieving admin log streams - TenantId: {TenantId}, AgentName: {AgentName}, ActivationName: {ActivationName}, " +
                "ParticipantId: {ParticipantId}, WorkflowType: {WorkflowType}, LogLevels: {LogLevels}, " +
                "StartDate: {StartDate}, EndDate: {EndDate}, Page: {Page}, PageSize: {PageSize}",
                tenantId, agentName ?? "null", activationName ?? "null", participantId ?? "null",
                workflowType ?? "null",
                logLevels != null ? string.Join(",", logLevels.Select(l => l.ToString())) : "null",
                startDate?.ToString() ?? "null", endDate?.ToString() ?? "null", page, pageSize);

            var (streams, totalCount) = await _logRepository.GetAdminLogStreamsAsync(
                tenantId,
                agentName,
                activationName,
                participantId,
                workflowType,
                logLevels,
                startDate,
                endDate,
                page,
                pageSize);

            var response = new AdminLogStreamsResponse
            {
                Streams = streams.Select(MapToAdminLogStream),
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = CalculateTotalPages(totalCount, pageSize)
            };

            _logger.LogInformation(
                "Admin log streams retrieved successfully - TotalCount: {TotalCount}, Page: {Page}/{TotalPages}",
                totalCount, page, response.TotalPages);

            return ServiceResult<AdminLogStreamsResponse>.Success(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve admin log streams. Error: {ErrorMessage}", ex.Message);
            return ServiceResult<AdminLogStreamsResponse>.InternalServerError("Failed to retrieve log streams");
        }
    }

    /// <summary>
    /// Validates inputs shared by all admin log queries. Returns an error message if invalid,
    /// or null if validation passes.
    /// </summary>
    private string? ValidateCommonInputs(
        string tenantId,
        int page,
        int pageSize,
        DateTime? startDate,
        DateTime? endDate)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            _logger.LogWarning("Attempt to retrieve admin logs with empty tenantId");
            return "TenantId cannot be empty";
        }

        if (page < 1)
        {
            _logger.LogWarning("Invalid page number: {Page}", page);
            return "Page must be greater than 0";
        }

        if (pageSize < MinPageSize || pageSize > MaxPageSize)
        {
            _logger.LogWarning("Invalid page size: {PageSize}", pageSize);
            return $"PageSize must be between {MinPageSize} and {MaxPageSize}";
        }

        if (startDate.HasValue && endDate.HasValue && startDate.Value > endDate.Value)
        {
            _logger.LogWarning("Invalid date range: startDate {StartDate} is after endDate {EndDate}",
                startDate.Value, endDate.Value);
            return "StartDate cannot be after EndDate";
        }

        return null;
    }

    private static int CalculateTotalPages(long totalCount, int pageSize)
    {
        return (int)Math.Ceiling((double)totalCount / pageSize);
    }

    private static AdminLogStream MapToAdminLogStream(LogStreamSummary s) => new()
    {
        WorkflowId = s.WorkflowId,
        WorkflowType = s.WorkflowType,
        WorkflowRunId = s.WorkflowRunId,
        Agent = s.Agent,
        Activation = s.Activation,
        ParticipantId = s.ParticipantId,
        LastLogAt = s.LastLogAt,
        FirstLogAt = s.FirstLogAt,
        LogCount = s.LogCount,
        LastLogLevel = s.LastLogLevel,
        LastLogMessage = s.LastLogMessage
    };
}
