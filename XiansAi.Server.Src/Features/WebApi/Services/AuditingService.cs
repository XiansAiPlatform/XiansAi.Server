using Shared.Auth;
using MongoDB.Driver;
using Features.WebApi.Repositories;
using Features.WebApi.Models;

namespace Features.WebApi.Services;

public interface IAuditingService
{
    Task<(IEnumerable<string?> participants, long totalCount)> GetParticipantsForAgentAsync(string agent, int page = 1, int pageSize = 20);
    Task<IEnumerable<string>> GetWorkflowTypesAsync(string agent, string? participantId = null);
    Task<IEnumerable<string>> GetWorkflowIdsForWorkflowTypeAsync(string agent, string workflowType, string? participantId = null);
    Task<(IEnumerable<Log> logs, long totalCount)> GetLogsAsync(
        string agent, 
        string? participantId, 
        string? workflowType, 
        string? workflowId = null,
        LogLevel? logLevel = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1, 
        int pageSize = 20);
    Task<Dictionary<string, IEnumerable<string>>> GetWorkflowTypesByAgentsAsync(IEnumerable<string> agents);
    Task<List<AgentCriticalGroup>> GetGroupedCriticalLogsAsync(IEnumerable<string> agents, DateTime? startTime = null, DateTime? endTime = null);
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

    public async Task<(IEnumerable<string?> participants, long totalCount)> GetParticipantsForAgentAsync(string agent, int page = 1, int pageSize = 20)
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
        DateTime? startTime = null,
        DateTime? endTime = null,
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
                startTime,
                endTime,
                page,
                pageSize);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving logs for agent {Agent}", agent);
            throw;
        }
    }
    

    /// <summary>
    /// Gets the workflow types for a list of agents
    /// </summary>
    public async Task<Dictionary<string, IEnumerable<string>>> GetWorkflowTypesByAgentsAsync(IEnumerable<string> agents)
    {
        try
        {
            var result = new Dictionary<string, IEnumerable<string>>();
            
            foreach (var agent in agents)
            {
                var workflowTypes = await _logRepository.GetDistinctWorkflowTypesAsync(
                    _tenantContext.TenantId,
                    agent);
                    
                result.Add(agent, workflowTypes);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflow types for multiple agents");
            throw;
        }
    }
    
    /// <summary>
    /// Gets error logs grouped by agent, workflow type, workflow, and workflow run
    /// </summary>
    public async Task<List<AgentCriticalGroup>> GetGroupedCriticalLogsAsync(
        IEnumerable<string> agents, 
        DateTime? startTime = null, 
        DateTime? endTime = null)
    {
        try
        {
            var result = new List<AgentCriticalGroup>();
            
            // Get workflow types by agent
            var agentToWorkflowTypes = await GetWorkflowTypesByAgentsAsync(agents);
            
            foreach (var agentEntry in agentToWorkflowTypes)
            {
                var agentName = agentEntry.Key;
                var workflowTypes = agentEntry.Value;
                
                if (!workflowTypes.Any())
                {
                    continue;
                }
                
                // Get error logs for all workflow types of this agent
                var criticalLogs = await _logRepository.GetCriticalLogsByWorkflowTypesAsync(
                    _tenantContext.TenantId,
                    workflowTypes,
                    startTime,
                    endTime);
                

                if (!criticalLogs.Any())
                {
                    continue;
                }
                
                // Group by workflow type
                var agentGroup = new AgentCriticalGroup { AgentName = agentName };
                
                var workflowTypeGroups = criticalLogs
                    .GroupBy(log => log.WorkflowType)
                    .Select(typeGroup => new WorkflowTypeCriticalGroup
                    {
                        WorkflowTypeName = typeGroup.Key,
                        Workflows = typeGroup
                            .GroupBy(log => log.WorkflowId)
                            .Select(workflowGroup => new WorkflowCriticalGroup
                            {
                                WorkflowId = workflowGroup.Key,
                                WorkflowRuns = workflowGroup
                                    .GroupBy(log => log.WorkflowRunId)
                                    .Select(runGroup => new WorkflowRunCriticalGroup
                                    {
                                        WorkflowRunId = runGroup.Key,
                                        CriticalLogs = runGroup.OrderByDescending(log => log.CreatedAt).ToList()
                                    })
                                    .ToList()
                            })
                            .ToList()
                    })
                    .ToList();
                
                agentGroup.WorkflowTypes = workflowTypeGroups;
                result.Add(agentGroup);
            }
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving grouped error logs");
            throw;
        }
    }
} 