using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Data;
using Microsoft.Extensions.Logging;

namespace Features.WebApi.Services;

public interface IAuditingService
{
    /// <summary>
    /// Retrieves all agent definitions
    /// </summary>
    /// <returns>A collection of agent definitions</returns>
    Task<ServiceResult<object>> GetAgentDefinitions();

    /// <summary>
    /// Retrieves workflow audit data with optional filtering
    /// </summary>
    /// <param name="agentName">Optional agent name filter</param>
    /// <param name="typeName">Optional workflow type filter</param>
    /// <returns>A collection of workflows with audit data</returns>
    Task<ServiceResult<object>> GetWorkflows(string? agentName, string? typeName);
} 

/// <summary>
/// Service for auditing operations on agents and workflows
/// </summary>
public class AuditingService : IAuditingService
{
    private readonly IFlowDefinitionRepository _definitionRepository;
    private readonly IWorkflowFinderService _workflowFinderService;
    private readonly ILogger<AuditingService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditingService"/> class.
    /// </summary>
    /// <param name="definitionRepository">Repository for flow definition operations.</param>
    /// <param name="workflowFinderService">Service for workflow finder operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    public AuditingService(
        IFlowDefinitionRepository definitionRepository,
        IWorkflowFinderService workflowFinderService,
        ILogger<AuditingService> logger,
        ITenantContext tenantContext)
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _workflowFinderService = workflowFinderService ?? throw new ArgumentNullException(nameof(workflowFinderService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<object>> GetAgentDefinitions()
    {
        try
        {
            _logger.LogInformation("Retrieving agent definitions for audit");
            var tenantId = _tenantContext.TenantId;
            
            var definitions = await _definitionRepository.GetLatestDefinitionsBasicDataAsync();
            
            // Add audit information such as when definitions were created, modified, etc.
            var auditedDefinitions = new
            {
                Definitions = definitions,
                AuditedAt = DateTime.UtcNow,
                TenantId = tenantId
            };
            
            return ServiceResult<object>.Success(auditedDefinitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent definitions for audit");
            return ServiceResult<object>.BadRequest("An error occurred while retrieving agent definitions.");
        }
    }

    /// <inheritdoc/>
    public async Task<ServiceResult<object>> GetWorkflows(string? agentName, string? typeName)
    {
        try
        {
            _logger.LogInformation("Retrieving workflow audit data with filters - Agent: {AgentName}, Type: {TypeName}", 
                agentName ?? "All", typeName ?? "All");
            
            var tenantId = _tenantContext.TenantId;
            
            var workflows = await _workflowFinderService.GetRunningWorkflowsByAgentAndType(agentName, typeName);
            
            // Add audit information such as execution history, status changes, etc.
            var auditedWorkflows = new
            {
                Workflows = workflows,
                AuditedAt = DateTime.UtcNow,
                TenantId = tenantId,
                Filters = new { AgentName = agentName, TypeName = typeName }
            };
            
            return ServiceResult<object>.Success(auditedWorkflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflow audit data");
            return ServiceResult<object>.BadRequest("An error occurred while retrieving workflow audit data.");
        }
    }
} 