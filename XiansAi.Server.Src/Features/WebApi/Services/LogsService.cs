using MongoDB.Bson;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using Shared.Auth;

namespace Features.WebApi.Services;

public class LogRequest
{
    public required string Message { get; set; }
    public required XiansAi.Server.Features.WebApi.Models.LogLevel Level { get; set; }
    public required string WorkflowRunId { get; set; }
    public required string WorkflowId { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

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

public class LogsService
{
    private readonly LogRepository _logRepository;
    private readonly ILogger<LogsService> _logger;
    private readonly ITenantContext _tenantContext;

    public LogsService(
        IDatabaseService databaseService,
        ILogger<LogsService> logger,
        ITenantContext tenantContext
    )
    {
        _logRepository = new LogRepository(databaseService);
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<IResult> GetLogById(string id)
    {
        try
        {
            var log = await _logRepository.GetByIdAsync(id);
            if (log is null)
            {
                _logger.LogWarning("Log with ID {Id} not found", id);
                return Results.NotFound();
            }
            return Results.Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting log by id: {Id}", id);
            return Results.Problem("An error occurred while retrieving the log");
        }
    }

    public async Task<IResult> GetLogsByWorkflowRunId(LogsByWorkflowRequest request)
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
            return Results.Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs by workflow run id: {WorkflowRunId}", request.WorkflowRunId);
            return Results.Problem("An error occurred while retrieving the logs");
        }
    }

    public async Task<IResult> GetLogsByDateRange(LogsByDateRangeRequest request)
    {
        try
        {
            var logs = await _logRepository.GetByDateRangeAsync(request.StartDate, request.EndDate);
            _logger.LogInformation("Found {Count} logs between {StartDate} and {EndDate}", 
                logs.Count, request.StartDate, request.EndDate);
            return Results.Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting logs by date range: {StartDate} to {EndDate}", 
                request.StartDate, request.EndDate);
            return Results.Problem("An error occurred while retrieving the logs");
        }
    }

    public async Task<IResult> CreateLogs(LogRequest[] requests)
    {
        try
        {
            var logs = new List<Log>();
            
            foreach (var request in requests)
            {
                var log = new Log
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    TenantId = _tenantContext.TenantId,
                    Message = request.Message,
                    Level = request.Level,
                    WorkflowId = request.WorkflowId ?? throw new ArgumentNullException(nameof(request.WorkflowId), "WorkflowId is required"),
                    WorkflowRunId = request.WorkflowRunId,
                    Properties = request.Properties,
                    CreatedAt = DateTime.UtcNow
                };
                logs.Add(log);
            }

            // Optimize by using bulk insert if available in repository
            foreach (var log in logs)
            {
                await _logRepository.CreateAsync(log);
            }

            _logger.LogInformation("Created {Count} logs", logs.Count);
            return Results.Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating logs, count: {Count}", requests.Length);
            return Results.Problem("An error occurred while creating the logs");
        }
    }

    public async Task<IResult> CreateLog(LogRequest request)
    {
        try
        {
            var log = new Log
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = _tenantContext.TenantId,
                Message = request.Message,
                Level = request.Level,
                WorkflowId = request.WorkflowId ?? throw new ArgumentNullException(nameof(request.WorkflowId), "WorkflowId is required"),
                WorkflowRunId = request.WorkflowRunId,
                Properties = request.Properties,
                CreatedAt = DateTime.UtcNow
            };

            await _logRepository.CreateAsync(log);
            _logger.LogInformation("Created log: {Log}", log.ToJson());
            return Results.Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating log: {Request}", request);
            return Results.Problem("An error occurred while creating the log");
        }
    }

    public async Task<IResult> DeleteLog(string id)
    {
        try
        {
            var result = await _logRepository.DeleteAsync(id);
            if (!result)
            {
                _logger.LogWarning("Log with ID {Id} not found for deletion", id);
                return Results.NotFound();
            }
            _logger.LogInformation("Deleted log: {Id}", id);
            return Results.Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting log: {Id}", id);
            return Results.Problem("An error occurred while deleting the log");
        }
    }

    public async Task<IResult> SearchLogs(string searchTerm)
    {
        try
        {
            var logs = await _logRepository.SearchAsync(searchTerm);
            _logger.LogInformation("Found {Count} logs matching search term: {SearchTerm}", 
                logs.Count, searchTerm);
            return Results.Ok(logs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching logs with term: {SearchTerm}", searchTerm);
            return Results.Problem("An error occurred while searching the logs");
        }
    }
}
