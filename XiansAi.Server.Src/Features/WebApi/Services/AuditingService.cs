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
            var logs = await _logRepository.GetByTenantIdAsync(_tenantContext.TenantId);
            
            var distinctParticipants = logs
                .Where(l => l.Agent == agent && !string.IsNullOrEmpty(l.ParticipantId))
                .Select(l => l.ParticipantId!)
                .Distinct()
                .OrderBy(p => p)
                .ToList();
                
            // Get total count
            var totalCount = distinctParticipants.Count;
            
            // Apply pagination
            var paginatedParticipants = distinctParticipants
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
                
            return (paginatedParticipants, totalCount);
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
            var logs = await _logRepository.GetByTenantIdAsync(_tenantContext.TenantId);
            
            var filteredLogs = logs.Where(l => l.Agent == agent);
            
            if (!string.IsNullOrEmpty(participantId))
            {
                filteredLogs = filteredLogs.Where(l => l.ParticipantId == participantId);
            }
            
            return filteredLogs
                .Where(l => !string.IsNullOrEmpty(l.WorkflowType))
                .Select(l => l.WorkflowType)
                .Distinct()
                .OrderBy(wt => wt)
                .ToList();
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
            var logs = await _logRepository.GetByTenantIdAsync(_tenantContext.TenantId);
            
            var filteredLogs = logs.Where(l => l.Agent == agent && l.WorkflowType == workflowType);
            
            if (!string.IsNullOrEmpty(participantId))
            {
                filteredLogs = filteredLogs.Where(l => l.ParticipantId == participantId);
            }
            
            return filteredLogs
                .Where(l => !string.IsNullOrEmpty(l.WorkflowId))
                .Select(l => l.WorkflowId)
                .Distinct()
                .OrderBy(wid => wid)
                .ToList();
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
            var allLogs = await _logRepository.GetByTenantIdAsync(_tenantContext.TenantId);
            
            // Apply filters
            var filteredLogs = allLogs.Where(l => l.Agent == agent);
            
            if (!string.IsNullOrEmpty(participantId))
            {
                filteredLogs = filteredLogs.Where(l => l.ParticipantId == participantId);
            }
            
            if (!string.IsNullOrEmpty(workflowType))
            {
                filteredLogs = filteredLogs.Where(l => l.WorkflowType == workflowType);
            }
            
            if (!string.IsNullOrEmpty(workflowId))
            {
                filteredLogs = filteredLogs.Where(l => l.WorkflowId == workflowId);
            }
            
            if (logLevel.HasValue)
            {
                filteredLogs = filteredLogs.Where(l => l.Level == logLevel.Value);
            }
            
            // Get total count
            var totalCount = filteredLogs.Count();
            
            // Apply pagination
            var paginatedLogs = filteredLogs
                .OrderByDescending(l => l.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
                
            return (paginatedLogs, totalCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving logs for agent {Agent}", agent);
            throw;
        }
    }
} 