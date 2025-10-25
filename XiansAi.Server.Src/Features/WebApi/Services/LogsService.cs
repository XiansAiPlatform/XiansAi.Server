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
    Task<ServiceResult<List<Log>>> GetLogsByWorkflowRunId(LogsByWorkflowRequest request);
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
            return ServiceResult<List<Log>>.InternalServerError("An error occurred while retrieving the logs");
        }
    }


}
