using MongoDB.Bson;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using Shared.Auth;

namespace Features.AgentApi.Services.Lib;

public class LogRequest
{
    public required string Message { get; set; }
    public required LogLevel Level { get; set; }
    public required string WorkflowRunId { get; set; }
    public required string WorkflowId { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
}

public interface ILogsService
{
    Task<IResult> CreateLogs(LogRequest[] requests);
    Task<IResult> CreateLog(LogRequest request);
}

public class LogsService : ILogsService
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
} 