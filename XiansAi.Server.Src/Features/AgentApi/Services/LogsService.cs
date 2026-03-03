using MongoDB.Bson;
using Features.AgentApi.Models;
using Features.AgentApi.Repositories;
using Shared.Data;
using Shared.Auth;

namespace Features.AgentApi.Services.Lib;

public class LogRequest
{
    public required string Message { get; set; }
    public required LogLevel Level { get; set; }
    public string? WorkflowRunId { get; set; }
    public required string WorkflowId { get; set; }
    public Dictionary<string, object>? Properties { get; set; }
    public string? WorkflowType { get; set; }
    public required string Agent { get; set; }
    public string? Activation { get; set; }
    public string? Exception { get; set; }
    public string? ParticipantId { get; set; }
    public string? TenantId { get; set; }
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
            if (requests == null || requests.Length == 0)
            {
                return Results.BadRequest("At least one log request is required");
            }

            var logs = new List<Log>();
            
            foreach (var request in requests)
            {
                if (string.IsNullOrEmpty(request.WorkflowId))
                {
                    return Results.BadRequest("WorkflowId is required");
                }

                var (tenantId, tenantError) = ResolveTenantId(request.TenantId);
                if (tenantError != null)
                {
                    return tenantError;
                }

                var log = new Log
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    TenantId = tenantId!,
                    Message = request.Message,
                    Level = request.Level,
                    WorkflowId = request.WorkflowId,
                    WorkflowRunId = request.WorkflowRunId,
                    Properties = request.Properties,
                    WorkflowType = request.WorkflowType,
                    Agent = request.Agent,
                    Activation = request.Activation,
                    Exception = request.Exception,
                    ParticipantId = request.ParticipantId,
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
            _logger.LogError(ex, "Error creating logs, count: {Count}", requests?.Length ?? 0);
            return Results.BadRequest("An error occurred while creating the logs");
        }
    }

    public async Task<IResult> CreateLog(LogRequest request)
    {
        try
        {
            if (request == null)
            {
                return Results.BadRequest("Log request is required");
            }

            if (string.IsNullOrEmpty(request.WorkflowId))
            {
                return Results.BadRequest("WorkflowId is required");
            }

            var (tenantId, tenantError) = ResolveTenantId(request.TenantId);
            if (tenantError != null)
            {
                return tenantError;
            }

            var log = new Log
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = tenantId!,
                Message = request.Message,
                Level = request.Level,
                WorkflowId = request.WorkflowId,
                WorkflowRunId = request.WorkflowRunId,
                Properties = request.Properties,
                WorkflowType = request.WorkflowType,
                Agent = request.Agent,
                Activation = request.Activation,
                Exception = request.Exception,
                ParticipantId = request.ParticipantId,
                CreatedAt = DateTime.UtcNow
            };

            await _logRepository.CreateAsync(log);
            _logger.LogDebug("Created log: {Log}", log.ToJson());
            return Results.Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating log: {Request}", request);
            return Results.BadRequest("An error occurred while creating the log");
        }
    }

    /// <summary>
    /// Resolves the tenant ID for log storage. System admins can specify any tenant;
    /// non-admins must use the current tenant or omit TenantId.
    /// </summary>
    private (string? TenantId, IResult? Error) ResolveTenantId(string? requestTenantId)
    {
        if (string.IsNullOrWhiteSpace(requestTenantId))
        {
            return (_tenantContext.TenantId, null);
        }

        var isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
        if (isSysAdmin)
        {
            return (requestTenantId.Trim(), null);
        }

        if (!requestTenantId.Trim().Equals(_tenantContext.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning("Non-admin user {UserId} attempted to log for tenant {RequestedTenantId} but current tenant is {CurrentTenantId}",
                _tenantContext.LoggedInUser, requestTenantId, _tenantContext.TenantId);
            return (null, Results.BadRequest("TenantId does not match the current tenant. Only system administrators can log for other tenants."));
        }

        return (requestTenantId.Trim(), null);
    }
} 