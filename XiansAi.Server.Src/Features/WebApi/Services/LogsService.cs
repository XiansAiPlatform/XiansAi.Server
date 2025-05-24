using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using Shared.Auth;
using Features.WebApi.Repositories;
using Shared.Data;
using Shared.Utils.Services;
using Features.WebApi.Models;

namespace Features.WebApi.Services;

public class LogsByWorkflowRequest
{
    public required string WorkflowRunId { get; set; }
    public int Skip { get; set; }
    public int Limit { get; set; }
    public int? LogLevel { get; set; }
}

public class LogsByDateRangeRequest
{
    public required DateTime StartDate { get; set; }
    public required DateTime EndDate { get; set; }
}

public interface ILogsService
{
    Task<ServiceResult<Log>> GetLogById(string id);
    Task<ServiceResult<List<Log>>> GetLogsByWorkflowRunId(LogsByWorkflowRequest request);
    Task<ServiceResult<List<Log>>> GetLogsByDateRange(LogsByDateRangeRequest request);
    Task<ServiceResult<bool>> DeleteLog(string id);
}

public class LogsService : ILogsService
{
    private readonly LogRepository _logRepository;
    private readonly ILogger<LogsService> _logger;

    public LogsService(
        IDatabaseService databaseService,
        ILogger<LogsService> logger
    )
    {
        _logRepository = new LogRepository(databaseService);
        _logger = logger;
    }

    public async Task<ServiceResult<Log>> GetLogById(string id)
    {
        try
        {
            var log = await _logRepository.GetByIdAsync(id);
            if (log is null)
            {
                _logger.LogWarning("Log with ID {Id} not found", id);
                return ServiceResult<Log>.NotFound("Log not found");
            }
            return ServiceResult<Log>.Success(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting log by id: {Id}", id);
            return ServiceResult<Log>.BadRequest("An error occurred while retrieving the log");
        }
    }

    public async Task<ServiceResult<List<Log>>> GetLogsByWorkflowRunId(LogsByWorkflowRequest request)
    {
        try
        {
            var logs = await _logRepository.GetByWorkflowRunIdAsync(
                request.WorkflowRunId, 
                request.Skip, 
                request.Limit, 
                request.LogLevel
            );
            _logger.LogInformation("Found {Count} logs for workflow {WorkflowRunId}", logs.Count, request.WorkflowRunId);
            return ServiceResult<List<Log>>.Success(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs by workflow run id: {WorkflowRunId}", request.WorkflowRunId);
            return ServiceResult<List<Log>>.BadRequest("An error occurred while retrieving the logs");
        }
    }

    public async Task<ServiceResult<List<Log>>> GetLogsByDateRange(LogsByDateRangeRequest request)
    {
        try
        {
            var logs = await _logRepository.GetByDateRangeAsync(request.StartDate, request.EndDate);
            _logger.LogInformation("Found {Count} logs between {StartDate} and {EndDate}", 
                logs.Count, request.StartDate, request.EndDate);
            return ServiceResult<List<Log>>.Success(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs by date range: {StartDate} to {EndDate}", 
                request.StartDate, request.EndDate);
            return ServiceResult<List<Log>>.BadRequest("An error occurred while retrieving the logs");
        }
    }

    public async Task<ServiceResult<bool>> DeleteLog(string id)
    {
        try
        {
            var result = await _logRepository.DeleteAsync(id);
            if (!result)
            {
                _logger.LogWarning("Log with ID {Id} not found for deletion", id);
                return ServiceResult<bool>.NotFound("Log not found");
            }
            _logger.LogInformation("Deleted log: {Id}", id);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting log: {Id}", id);
            return ServiceResult<bool>.BadRequest("An error occurred while deleting the log");
        }
    }
}
