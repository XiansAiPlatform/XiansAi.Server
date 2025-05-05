using Shared.Auth;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Features.WebApi.Models;
using MongoDB.Driver;

namespace Features.WebApi.Services;

public interface IAuditingService
{
    Task<(IEnumerable<string> participants, long totalCount)> GetParticipantsForAgentAsync(string agent, int page = 1, int pageSize = 20);
    Task<IEnumerable<string>> GetWorkflowTypesAsync(string agent, string? participantId = null);
    Task<IEnumerable<string>> GetWorkflowIdsForWorkflowTypeAsync(string agent, string workflowType, string? participantId = null);
    Task<(IEnumerable<Log> logs, long totalCount)> GetLogsAsync(
        string agent, 
        string? participantId, 
        string? workflowType, 
        string? workflowId = null,
        LogLevel? logLevel = null, 
        int page = 1, 
        int pageSize = 20);
} 

/// <summary>
/// Service for auditing operations on agents and workflows
/// </summary>
public class AuditingService : IAuditingService
{
    private readonly ILogger<AuditingService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly ILogRepository _logRepository;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditingService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    /// <param name="logRepository">Repository for log data access.</param>
    public AuditingService(
        ILogger<AuditingService> logger,
        ITenantContext tenantContext,
        ILogRepository logRepository)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _logRepository = logRepository ?? throw new ArgumentNullException(nameof(logRepository));
    }

    public async Task<(IEnumerable<string> participants, long totalCount)> GetParticipantsForAgentAsync(string agent, int page = 1, int pageSize = 20)
    {
        try
        {
            return await _logRepository.GetDistinctParticipantsForAgentAsync(
                _tenantContext.TenantId, 
                agent, 
                page, 
                pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving participants for agent {Agent}", agent);
            throw;
        }
    }

    /// <summary>
    /// Gets a list of workflow types for a specific agent and optional participant
    /// </summary>
    public async Task<IEnumerable<string>> GetWorkflowTypesAsync(string agent, string? participantId = null)
    {
        try
        {
            return await _logRepository.GetDistinctWorkflowTypesAsync(
                _tenantContext.TenantId,
                agent,
                participantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflow types for agent {Agent} and participant {ParticipantId}", agent, participantId);
            throw;
        }
    }

    /// <summary>
    /// Gets a list of workflowIds for a specific workflowType and agent with optional participant filter
    /// </summary>
    public async Task<IEnumerable<string>> GetWorkflowIdsForWorkflowTypeAsync(string agent, string workflowType, string? participantId = null)
    {
        try
        {
            return await _logRepository.GetDistinctWorkflowIdsForTypeAsync(
                _tenantContext.TenantId,
                agent,
                workflowType,
                participantId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflow IDs for agent {Agent}, workflow type {WorkflowType}, and participant {ParticipantId}", 
                agent, workflowType, participantId);
            throw;
        }
    }

    /// <summary>
    /// Gets logs filtered by agent, participant, workflow type, workflow ID, and log level with pagination
    /// </summary>
    public async Task<(IEnumerable<Log> logs, long totalCount)> GetLogsAsync(
        string agent, 
        string? participantId, 
        string? workflowType, 
        string? workflowId = null,
        LogLevel? logLevel = null, 
        int page = 1, 
        int pageSize = 20)
    {
        try
        {
            return await _logRepository.GetFilteredLogsAsync(
                _tenantContext.TenantId,
                agent,
                participantId,
                workflowType,
                workflowId,
                logLevel,
                page,
                pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving logs for agent {Agent}", agent);
            throw;
        }
    }
} 