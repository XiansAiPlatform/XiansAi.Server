using Shared.Auth;
using Shared.Repositories;
using Shared.Data.Models;
using Shared.Utils.Services;
using Features.WebApi.Models;
using Shared.Services;
using System.ComponentModel.DataAnnotations;

namespace Features.WebApi.Services;

public interface IAgentService
{
    Task<ServiceResult<Agent>> GetAgentByNameAsync(string agentName);
    Task<ServiceResult<List<string>>> GetAgentNames();
    Task<ServiceResult<List<AgentWithDefinitions>>> GetGroupedDefinitions(bool basicDataOnly = false);
    Task<ServiceResult<List<FlowDefinition>>> GetDefinitions(string agentName, bool basicDataOnly = false);
    Task<ServiceResult<List<WorkflowResponse>>> GetWorkflowInstances(string? agentName, string? typeName);
    Task<ServiceResult<AgentDeleteResult>> DeleteAgent(string agentName);
    Task<ServiceResult<bool>> IsSystemAgent(string agentName);
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
    /// <param name="agentRepository">Repository for agent operations (templates and instances).</param>
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


    public async Task<ServiceResult<Agent>> GetAgentByNameAsync(string agentName)
    {
        // Check agents collection (tenant-scoped instances or system-scoped templates)
        var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
        if (agent != null)
        {
            return ServiceResult<Agent>.Success(agent);
        }

        // Also check system-scoped agents (templates)
        var systemAgent = await _agentRepository.GetByNameInternalAsync(agentName, null);
        if (systemAgent != null && systemAgent.SystemScoped)
        {
            return ServiceResult<Agent>.Success(systemAgent);
        }

        return ServiceResult<Agent>.NotFound("Agent " + agentName + " not found");
    }

    public async Task<ServiceResult<bool>> IsSystemAgent(string agentName)
    {
        return ServiceResult<bool>.Success(await _agentRepository.IsSystemAgent(agentName));
    }

    public async Task<ServiceResult<List<string>>> GetAgentNames()
    {
        try
        {
            // Get agent names from agents collection (includes both templates and instances)
            var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);

            // Get unique agent names
            var agentNames = agents.Select(a => a.Name)
                .Distinct()
                .ToList();

            return ServiceResult<List<string>>.Success(agentNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent names");
            return ServiceResult<List<string>>.InternalServerError("An error occurred while retrieving agent names");
        }
    }

    public async Task<ServiceResult<List<AgentWithDefinitions>>> GetGroupedDefinitions(bool basicDataOnly = false)
    {
        try
        {
            _logger.LogInformation("Getting grouped definitions for user {UserId} in tenant {Tenant}", _tenantContext.LoggedInUser, _tenantContext.TenantId);
            
            // Get agents with definitions from agents collection (includes both templates and instances)
            var agentDefinitions = await _agentRepository.GetAgentsWithDefinitionsAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId, null, null, basicDataOnly: basicDataOnly);
            
            _logger.LogInformation("Returning {Count} agents with definitions", agentDefinitions.Count);
            
            return ServiceResult<List<AgentWithDefinitions>>.Success(agentDefinitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions");
            return ServiceResult<List<AgentWithDefinitions>>.InternalServerError("An error occurred while retrieving definitions");
        }
    }

    public async Task<ServiceResult<List<WorkflowResponse>>> GetWorkflowInstances(string? agentName, string? typeName)
    {
        try
        {
            string? validatedAgentName = null;
            string? validatedTypeName = null;
            // Check if user has read permission for the agent if agentName is provided
            if (!string.IsNullOrWhiteSpace(agentName))
            {
                 validatedAgentName = Agent.SanitizeAndValidateName(agentName);
                var readPermissionResult = await _permissionsService.HasReadPermission(validatedAgentName);
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
            if (!string.IsNullOrWhiteSpace(typeName))
            {
                 validatedTypeName = FlowDefinition.SanitizeAndValidateType(typeName);
            }

            // Call the refactored workflow service which now returns ServiceResult<List<WorkflowResponse>>
            var workflowResult = await _workflowFinderService.GetRunningWorkflowsByAgentAndType(validatedAgentName, validatedTypeName);

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
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving workflows: {Message}", ex.Message);
            return ServiceResult<List<WorkflowResponse>>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflows");
            return ServiceResult<List<WorkflowResponse>>.InternalServerError("An error occurred while retrieving workflows");
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
            var validatedgentName = Agent.SanitizeAndValidateName(agentName);

            // Check if user has owner permission using PermissionsService
            var ownerPermissionResult = await _permissionsService.HasOwnerPermission(validatedgentName);
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

            // Get agent from agents collection
            var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentName} not found for deletion", agentName);
                return ServiceResult<AgentDeleteResult>.NotFound("Agent not found");
            }

            // Don't allow deletion of system-scoped agents through this endpoint
            if (agent.SystemScoped)
            {
                _logger.LogWarning("Attempted to delete system-scoped agent {AgentName} through regular delete endpoint", agentName);
                return ServiceResult<AgentDeleteResult>.BadRequest("System-scoped agents cannot be deleted through this endpoint. Use the template deletion endpoint instead.");
            }

            // Delete the agent (this will also delete associated flow definitions)
            var agentDeleted = await _agentRepository.DeleteAsync(agent.Id, _tenantContext.LoggedInUser, _tenantContext.UserRoles);
            if (!agentDeleted)
            {
                _logger.LogError("Failed to delete agent {AgentName} with ID {AgentId}", agentName, agent.Id);
                return ServiceResult<AgentDeleteResult>.BadRequest("Failed to delete the agent");
            }

            _logger.LogInformation("Successfully deleted agent {AgentName} and its associated flow definitions", agentName);

            var result = new AgentDeleteResult
            {
                Message = "Agent and its associated flow definitions deleted successfully",
            };

            return ServiceResult<AgentDeleteResult>.Success(result);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while deleting agent: {Message}", ex.Message);
            return ServiceResult<AgentDeleteResult>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent {AgentName}", agentName);
            return ServiceResult<AgentDeleteResult>.InternalServerError("An error occurred while deleting the agent");
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
            var validatedAgentName = Agent.SanitizeAndValidateName(agentName);

            // Check if user has read permission using PermissionsService
            var readPermissionResult = await _permissionsService.HasReadPermission(validatedAgentName);
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
            var definitions = await _definitionRepository.GetByNameAsync(validatedAgentName, _tenantContext.TenantId);

            return ServiceResult<List<FlowDefinition>>.Success(definitions);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving definitions: {Message}", ex.Message);
            return ServiceResult<List<FlowDefinition>>.BadRequest($"Validation failed: {ex.Message}");
        }   
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions for agent {AgentName}", agentName);
            return ServiceResult<List<FlowDefinition>>.InternalServerError("An error occurred while retrieving agent definitions");
        }
    }
}