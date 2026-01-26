using Shared.Auth;
using MongoDB.Driver;
using Features.WebApi.Repositories;
using Features.WebApi.Models;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

public interface IAuditingService
{
    Task<ServiceResult<(IEnumerable<string?> participants, long totalCount)>> GetParticipantsForAgentAsync(string agent, int page = 1, int pageSize = 20);
    Task<ServiceResult<IEnumerable<string>>> GetWorkflowTypesAsync(string agent, string? participantId = null);
    Task<ServiceResult<IEnumerable<string>>> GetWorkflowIdsForWorkflowTypeAsync(string agent, string workflowType, string? participantId = null);
    Task<ServiceResult<(IEnumerable<Log> logs, long totalCount)>> GetLogsAsync(
        string agent, 
        string? participantId, 
        string? workflowType, 
        string? workflowId = null,
        LogLevel? logLevel = null,
        DateTime? startTime = null,
        DateTime? endTime = null,
        int page = 1, 
        int pageSize = 20);
    Task<ServiceResult<Dictionary<string, IEnumerable<string>>>> GetWorkflowTypesByAgentsAsync(IEnumerable<string> agents);
    Task<ServiceResult<List<AgentCriticalGroup>>> GetGroupedCriticalLogsAsync(IEnumerable<string> agents, DateTime? startTime = null, DateTime? endTime = null);
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

    public async Task<ServiceResult<(IEnumerable<string?> participants, long totalCount)>> GetParticipantsForAgentAsync(string agent, int page = 1, int pageSize = 20)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agent))
            {
                _logger.LogWarning("Invalid agent name provided for getting participants");
                return ServiceResult<(IEnumerable<string?> participants, long totalCount)>.BadRequest("Agent name is required");
            }

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var result = await _logRepository.GetDistinctParticipantsForAgentAsync(
                _tenantContext.TenantId, 
                agent, 
                page, 
                pageSize);

            return ServiceResult<(IEnumerable<string?> participants, long totalCount)>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving participants for agent {Agent}", agent);
            return ServiceResult<(IEnumerable<string?> participants, long totalCount)>.InternalServerError("An error occurred while retrieving participants");
        }
    }

    /// <summary>
    /// Gets a list of workflow types for a specific agent and optional participant
    /// </summary>
    public async Task<ServiceResult<IEnumerable<string>>> GetWorkflowTypesAsync(string agent, string? participantId = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agent))
            {
                _logger.LogWarning("Invalid agent name provided for getting workflow types");
                return ServiceResult<IEnumerable<string>>.BadRequest("Agent name is required");
            }

            var result = await _logRepository.GetDistinctWorkflowTypesAsync(
                _tenantContext.TenantId,
                agent,
                participantId);

            return ServiceResult<IEnumerable<string>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflow types for agent {Agent} and participant {ParticipantId}", agent, participantId);
            return ServiceResult<IEnumerable<string>>.InternalServerError("An error occurred while retrieving workflow types");
        }
    }

    /// <summary>
    /// Gets a list of workflowIds for a specific workflowType and agent with optional participant filter
    /// </summary>
    public async Task<ServiceResult<IEnumerable<string>>> GetWorkflowIdsForWorkflowTypeAsync(string agent, string workflowType, string? participantId = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agent))
            {
                _logger.LogWarning("Invalid agent name provided for getting workflow IDs");
                return ServiceResult<IEnumerable<string>>.BadRequest("Agent name is required");
            }

            if (string.IsNullOrWhiteSpace(workflowType))
            {
                _logger.LogWarning("Invalid workflow type provided for getting workflow IDs");
                return ServiceResult<IEnumerable<string>>.BadRequest("Workflow type is required");
            }

            var result = await _logRepository.GetDistinctWorkflowIdsForTypeAsync(
                _tenantContext.TenantId,
                agent,
                workflowType,
                participantId);

            return ServiceResult<IEnumerable<string>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflow IDs for agent {Agent}, workflow type {WorkflowType}, and participant {ParticipantId}", 
                agent, workflowType, participantId);
            return ServiceResult<IEnumerable<string>>.InternalServerError("An error occurred while retrieving workflow IDs");
        }
    }

    /// <summary>
    /// Gets logs filtered by agent, participant, workflow type, workflow ID, and log level with pagination
    /// </summary>
    public async Task<ServiceResult<(IEnumerable<Log> logs, long totalCount)>> GetLogsAsync(
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
            if (string.IsNullOrWhiteSpace(agent))
            {
                _logger.LogWarning("Invalid agent name provided for getting logs");
                return ServiceResult<(IEnumerable<Log> logs, long totalCount)>.BadRequest("Agent name is required");
            }

            if (page < 1)
            {
                page = 1;
            }

            if (pageSize < 1 || pageSize > 100)
            {
                pageSize = 20;
            }

            var result = await _logRepository.GetFilteredLogsAsync(
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

            return ServiceResult<(IEnumerable<Log> logs, long totalCount)>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving logs for agent {Agent}", agent);
            return ServiceResult<(IEnumerable<Log> logs, long totalCount)>.InternalServerError("An error occurred while retrieving logs");
        }
    }
    

    /// <summary>
    /// Gets the workflow types for a list of agents
    /// </summary>
    public async Task<ServiceResult<Dictionary<string, IEnumerable<string>>>> GetWorkflowTypesByAgentsAsync(IEnumerable<string> agents)
    {
        try
        {
            if (agents == null || !agents.Any())
            {
                _logger.LogWarning("No agents provided for getting workflow types");
                return ServiceResult<Dictionary<string, IEnumerable<string>>>.BadRequest("At least one agent is required");
            }

            var result = new Dictionary<string, IEnumerable<string>>();
            
            foreach (var agent in agents)
            {
                var workflowTypesResult = await GetWorkflowTypesAsync(agent);
                if (workflowTypesResult.IsSuccess && workflowTypesResult.Data != null)
                {
                    result.TryAdd(agent, workflowTypesResult.Data);
                }
                else
                {
                    // Add empty list for agents that failed or have no workflow types
                    result.Add(agent, new List<string>());
                }
            }
            
            return ServiceResult<Dictionary<string, IEnumerable<string>>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflow types for multiple agents");
            return ServiceResult<Dictionary<string, IEnumerable<string>>>.InternalServerError("An error occurred while retrieving workflow types");
        }
    }
    
    /// <summary>
    /// Gets error logs grouped by agent, workflow type, workflow, and workflow run
    /// </summary>
    public async Task<ServiceResult<List<AgentCriticalGroup>>> GetGroupedCriticalLogsAsync(
        IEnumerable<string> agents, 
        DateTime? startTime = null, 
        DateTime? endTime = null)
    {
        try
        {
            if (agents == null || !agents.Any())
            {
                _logger.LogWarning("No agents provided for getting critical logs");
                return ServiceResult<List<AgentCriticalGroup>>.BadRequest("At least one agent is required");
            }

            var result = new List<AgentCriticalGroup>();
            
            // Get workflow types by agent
            var agentToWorkflowTypesResult = await GetWorkflowTypesByAgentsAsync(agents);
            if (!agentToWorkflowTypesResult.IsSuccess || agentToWorkflowTypesResult.Data == null)
            {
                _logger.LogWarning("Failed to get workflow types for agents");
                return ServiceResult<List<AgentCriticalGroup>>.BadRequest("Failed to retrieve workflow types for agents");
            }

            var agentToWorkflowTypes = agentToWorkflowTypesResult.Data;
            
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
                        WorkflowTypeName = typeGroup.Key ?? "unknown",
                        Workflows = typeGroup
                            .GroupBy(log => log.WorkflowId)
                            .Select(workflowGroup => new WorkflowCriticalGroup
                            {
                                WorkflowId = workflowGroup.Key,
                                WorkflowRuns = workflowGroup
                                    .GroupBy(log => log.WorkflowRunId)
                                    .Select(runGroup => new WorkflowRunCriticalGroup
                                    {
                                        WorkflowRunId = runGroup.Key ?? "unknown",
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
            
            return ServiceResult<List<AgentCriticalGroup>>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving grouped error logs");
            return ServiceResult<List<AgentCriticalGroup>>.InternalServerError("An error occurred while retrieving grouped error logs");
        }
    }
} 