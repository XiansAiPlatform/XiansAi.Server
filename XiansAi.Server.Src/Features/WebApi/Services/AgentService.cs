using Shared.Auth;
using Shared.Repositories;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils.Services;
using Features.WebApi.Models;

namespace Features.WebApi.Services;

public interface IAgentService
{
    Task<ServiceResult<List<string>>> GetAgentNames();
    Task<ServiceResult<List<AgentWithDefinitions>>> GetGroupedDefinitions(bool basicDataOnly = false);
    Task<ServiceResult<List<FlowDefinition>>> GetDefinitions(string agentName, bool basicDataOnly = false);
    Task<ServiceResult<List<WorkflowResponse>>> GetWorkflowInstances(string? agentName, string? typeName);
    Task<ServiceResult<AgentDeleteResult>> DeleteAgent(string agentName);
} 
/// <summary>
/// Service for managing agent definitions and workflows.
/// </summary>
public class AgentService : IAgentService
{
    private readonly IFlowDefinitionRepository _definitionRepository;
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<AgentService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IWorkflowFinderService _workflowFinderService;
    private readonly IPermissionsService _permissionsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentService"/> class.
    /// </summary>
    /// <param name="definitionRepository">Repository for flow definition operations.</param>
    /// <param name="agentRepository">Repository for agent operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    /// <param name="workflowFinderService">Service for workflow finder operations.</param>
    /// <param name="permissionsService">Service for permission operations.</param>
    public AgentService(
        IFlowDefinitionRepository definitionRepository,
        IAgentRepository agentRepository,
        ILogger<AgentService> logger,
        ITenantContext tenantContext,
        IWorkflowFinderService workflowFinderService,
        IPermissionsService permissionsService
    )
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _workflowFinderService = workflowFinderService ?? throw new ArgumentNullException(nameof(workflowFinderService));
        _permissionsService = permissionsService ?? throw new ArgumentNullException(nameof(permissionsService));
    }

    public async Task<ServiceResult<List<string>>> GetAgentNames()
    {
        try
        {
            var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);
            var agentNames = agents.Select(a => a.Name).ToList();
            return ServiceResult<List<string>>.Success(agentNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent names");
            return ServiceResult<List<string>>.InternalServerError("An error occurred while retrieving agent names. Error: " + ex.Message);
        }
    }

    public async Task<ServiceResult<List<AgentWithDefinitions>>> GetGroupedDefinitions(bool basicDataOnly = false)
    {
        try
        {
            var definitions = await _agentRepository.GetAgentsWithDefinitionsAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId, null, null, basicDataOnly: basicDataOnly);
            return ServiceResult<List<AgentWithDefinitions>>.Success(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions");
            return ServiceResult<List<AgentWithDefinitions>>.InternalServerError("An error occurred while retrieving definitions. Error: " + ex.Message);
        }
    }

    public async Task<ServiceResult<List<WorkflowResponse>>> GetWorkflowInstances(string? agentName, string? typeName)
    {
        try
        {
            // Check if user has read permission for the agent if agentName is provided
            if (!string.IsNullOrWhiteSpace(agentName))
            {
                var readPermissionResult = await _permissionsService.HasReadPermission(agentName);
                if (!readPermissionResult.IsSuccess)
                {
                    if (readPermissionResult.StatusCode == StatusCode.NotFound)
                    {
                        return ServiceResult<List<WorkflowResponse>>.NotFound("Agent not found");
                    }
                    return ServiceResult<List<WorkflowResponse>>.BadRequest(readPermissionResult.ErrorMessage ?? "Failed to check permissions");
                }

                if (!readPermissionResult.Data)
                {
                    _logger.LogWarning("User {UserId} attempted to access workflows for agent {AgentName} without read permission", 
                        _tenantContext.LoggedInUser, agentName);
                    return ServiceResult<List<WorkflowResponse>>.Forbidden("You do not have permission to access workflows for this agent");
                }
            }

            // Call the refactored workflow service which now returns ServiceResult<List<WorkflowResponse>>
            var workflowResult = await _workflowFinderService.GetRunningWorkflowsByAgentAndType(agentName, typeName);
            
            if (workflowResult.IsSuccess)
            {
                return ServiceResult<List<WorkflowResponse>>.Success(workflowResult.Data ?? new List<Features.WebApi.Models.WorkflowResponse>());
            }
            else
            {
                _logger.LogWarning("Workflow service returned error for agent {AgentName} and type {TypeName}: {Error}", 
                    agentName, typeName, workflowResult.ErrorMessage);
                return ServiceResult<List<WorkflowResponse>>.BadRequest(workflowResult.ErrorMessage ?? "Failed to retrieve workflows");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflows");
            return ServiceResult<List<WorkflowResponse>>.InternalServerError("An error occurred while retrieving workflows. Error: " + ex.Message);
        }
    }

    public async Task<ServiceResult<AgentDeleteResult>> DeleteAgent(string agentName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                _logger.LogWarning("Invalid agent name provided for deletion");
                return ServiceResult<AgentDeleteResult>.BadRequest("Agent name is required");
            }

            // Check if user has owner permission using PermissionsService
            var ownerPermissionResult = await _permissionsService.HasOwnerPermission(agentName);
            if (!ownerPermissionResult.IsSuccess)
            {
                if (ownerPermissionResult.StatusCode == StatusCode.NotFound)
                {
                    return ServiceResult<AgentDeleteResult>.NotFound("Agent not found");
                }
                return ServiceResult<AgentDeleteResult>.BadRequest(ownerPermissionResult.ErrorMessage ?? "Failed to check permissions");
            }

            if (!ownerPermissionResult.Data)
            {
                _logger.LogWarning("User {UserId} attempted to delete agent {AgentName} without owner permission", 
                    _tenantContext.LoggedInUser, agentName);
                return ServiceResult<AgentDeleteResult>.Forbidden("You must have owner permission to delete this agent");
            }

            // Get the agent to retrieve its ID for deletion
            var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentName} not found for deletion", agentName);
                return ServiceResult<AgentDeleteResult>.NotFound("Agent not found");
            }

            // Delete all flow definitions for this agent
            var deletedDefinitionsCount = (int)await _definitionRepository.DeleteByAgentAsync(agentName);
            _logger.LogInformation("Deleted {Count} flow definitions for agent {AgentName}", deletedDefinitionsCount, agentName);

            // Delete the agent itself
            var agentDeleted = await _agentRepository.DeleteAsync(agent.Id, _tenantContext.LoggedInUser, _tenantContext.UserRoles);
            if (!agentDeleted)
            {
                _logger.LogError("Failed to delete agent {AgentName} with ID {AgentId}", agentName, agent.Id);
                return ServiceResult<AgentDeleteResult>.BadRequest("Failed to delete the agent");
            }

            _logger.LogInformation("Successfully deleted agent {AgentName} and {Count} associated flow definitions", 
                agentName, deletedDefinitionsCount);
            
            var result = new AgentDeleteResult
            {
                Message = "Agent deleted successfully",
                DeletedFlowDefinitions = deletedDefinitionsCount
            };
            
            return ServiceResult<AgentDeleteResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent {AgentName}", agentName);
            return ServiceResult<AgentDeleteResult>.InternalServerError("An error occurred while deleting the agent. Error: " + ex.Message);
        }
    }

    public async Task<ServiceResult<List<FlowDefinition>>> GetDefinitions(string agentName, bool basicDataOnly = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                _logger.LogWarning("Invalid agent name provided for getting definitions");
                return ServiceResult<List<FlowDefinition>>.BadRequest("Agent name is required");
            }

            // Check if user has read permission using PermissionsService
            var readPermissionResult = await _permissionsService.HasReadPermission(agentName);
            if (!readPermissionResult.IsSuccess)
            {
                if (readPermissionResult.StatusCode == StatusCode.NotFound)
                {
                    return ServiceResult<List<FlowDefinition>>.NotFound("Agent not found");
                }
                return ServiceResult<List<FlowDefinition>>.BadRequest(readPermissionResult.ErrorMessage ?? "Failed to check permissions");
            }

            if (!readPermissionResult.Data)
            {
                _logger.LogWarning("User {UserId} attempted to access agent {AgentName} without read permission", 
                    _tenantContext.LoggedInUser, agentName);
                return ServiceResult<List<FlowDefinition>>.Forbidden("You do not have permission to access this agent");
            }

            // Get definitions for the specific agent directly from the definition repository
            var definitions = await _definitionRepository.GetByNameAsync(agentName);

            return ServiceResult<List<FlowDefinition>>.Success(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions for agent {AgentName}", agentName);
            return ServiceResult<List<FlowDefinition>>.InternalServerError("An error occurred while retrieving agent definitions. Error: " + ex.Message);
        }
    }
} 