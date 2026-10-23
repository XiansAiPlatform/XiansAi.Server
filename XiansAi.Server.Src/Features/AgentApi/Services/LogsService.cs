using MongoDB.Bson;
using Features.AgentApi.Models;
using Features.AgentApi.Repositories;
using Shared.Data;
using Shared.Auth;
using Shared.Utils;

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
    public string? TraceId { get; set; }
    public string? SpanId { get; set; }
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
                    ParticipantId = ResolveParticipant(request.ParticipantId),
                    TraceId = request.TraceId,
                    SpanId = request.SpanId,
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
                ParticipantId = ResolveParticipant(request.ParticipantId),
                TraceId = request.TraceId,
                SpanId = request.SpanId,
                CreatedAt = DateTime.UtcNow
            };

            await _logRepository.CreateAsync(log);
            _logger.LogDebug("Created log: {Log}", LogSanitizer.Sanitize(log.ToJson()));
            return Results.Ok(log);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating log: {Request}", request);
            return Results.BadRequest("An error occurred while creating the log");
        }
    }

    /// <summary>
    /// Participant shown on the log. A conversation participant supplied by the agent is kept.
    /// When that value is missing, or it is only the API-key or certificate owner, a distinct
    /// <see cref="ITenantContext.ParticipantId"/> (the <c>X-On-Behalf-Of</c> user) is used instead.
    /// The service identity is not invented onto a log that named nobody.
    /// </summary>
    private string? ResolveParticipant(string? requestedParticipantId)
    {
        var requested = string.IsNullOrWhiteSpace(requestedParticipantId)
            ? null
            : requestedParticipantId.Trim();
        var actor = string.IsNullOrWhiteSpace(_tenantContext.ParticipantId)
            ? null
            : _tenantContext.ParticipantId.Trim();
        var serviceIdentity = string.IsNullOrWhiteSpace(_tenantContext.LoggedInUser)
            ? null
            : _tenantContext.LoggedInUser.Trim();

        var requestedIsServiceIdentity = requested != null
            && serviceIdentity != null
            && string.Equals(requested, serviceIdentity, StringComparison.OrdinalIgnoreCase);
        var actorIsDistinct = actor != null
            && (serviceIdentity == null
                || !string.Equals(actor, serviceIdentity, StringComparison.OrdinalIgnoreCase));

        if (requested != null && !requestedIsServiceIdentity)
        {
            return requested;
        }

        if (actorIsDistinct)
        {
            return actor;
        }

        return requested;
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
                LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(requestTenantId), LogSanitizer.Sanitize(_tenantContext.TenantId));
            return (null, Results.BadRequest("TenantId does not match the current tenant. Only system administrators can log for other tenants."));
        }

        return (requestTenantId.Trim(), null);
    }
} 