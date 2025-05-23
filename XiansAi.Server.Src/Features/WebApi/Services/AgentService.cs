using Shared.Auth;
using Shared.Repositories;
using Shared.Data;

namespace Features.WebApi.Services;

public interface IAgentService
{
    Task<List<string>> GetAgentNames();
    Task<IResult> GetGroupedDefinitions(bool basicDataOnly = false);
    Task<IResult> GetDefinitions(string agentName, bool basicDataOnly = false);
    Task<IResult> GetWorkflowInstances(string? agentName, string? typeName);
    Task<IResult> DeleteAgent(string agentName);
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

    /// <summary>
    /// Initializes a new instance of the <see cref="AgentService"/> class.
    /// </summary>
    /// <param name="definitionRepository">Repository for flow definition operations.</param>
    /// <param name="agentRepository">Repository for agent operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    /// <param name="workflowFinderService">Service for workflow finder operations.</param>
    public AgentService(
        IFlowDefinitionRepository definitionRepository,
        IAgentRepository agentRepository,
        ILogger<AgentService> logger,
        ITenantContext tenantContext,
        IWorkflowFinderService workflowFinderService
    )
    {
        _definitionRepository = definitionRepository ?? throw new ArgumentNullException(nameof(definitionRepository));
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _workflowFinderService = workflowFinderService ?? throw new ArgumentNullException(nameof(workflowFinderService));
    }

    public async Task<List<string>> GetAgentNames()
    {
        var agents = await _agentRepository.GetAgentsWithPermissionAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId);
        return agents.Select(a => a.Name).ToList();
    }

    public async Task<IResult> GetGroupedDefinitions(bool basicDataOnly = false)
    {
        try
        {
            var definitions = await _agentRepository.GetAgentsWithDefinitionsAsync(_tenantContext.LoggedInUser, _tenantContext.TenantId, null, null, basicDataOnly: basicDataOnly);
            return Results.Ok(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions");
            return Results.Problem("An error occurred while retrieving definitions.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> GetWorkflowInstances(string? agentName, string? typeName)
    {
        try
        {
            var workflows = await _workflowFinderService.GetRunningWorkflowsByAgentAndType(agentName, typeName);
            return Results.Ok(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving workflows");
            return Results.Problem("An error occurred while retrieving workflows.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> DeleteAgent(string agentName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                _logger.LogWarning("Invalid agent name provided for deletion");
                return Results.BadRequest("Agent name is required");
            }

            // Get the agent to check if it exists and verify permissions
            var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentName} not found for deletion", agentName);
                return Results.NotFound("Agent not found");
            }

            // Check if user has owner permission
            if (!agent.Permissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner))
            {
                _logger.LogWarning("User {UserId} attempted to delete agent {AgentName} without owner permission", 
                    _tenantContext.LoggedInUser, agentName);
                return Results.Problem("You must have owner permission to delete this agent", statusCode: StatusCodes.Status403Forbidden);
            }

            // Delete all flow definitions for this agent
            var deletedDefinitionsCount = await _definitionRepository.DeleteByAgentAsync(agentName);
            _logger.LogInformation("Deleted {Count} flow definitions for agent {AgentName}", deletedDefinitionsCount, agentName);

            // Delete the agent itself
            var agentDeleted = await _agentRepository.DeleteAsync(agent.Id, _tenantContext.LoggedInUser, _tenantContext.UserRoles);
            if (!agentDeleted)
            {
                _logger.LogError("Failed to delete agent {AgentName} with ID {AgentId}", agentName, agent.Id);
                return Results.Problem("Failed to delete the agent", statusCode: StatusCodes.Status500InternalServerError);
            }

            _logger.LogInformation("Successfully deleted agent {AgentName} and {Count} associated flow definitions", 
                agentName, deletedDefinitionsCount);
            
            return Results.Ok(new { 
                message = "Agent deleted successfully",
                deletedFlowDefinitions = deletedDefinitionsCount 
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting agent {AgentName}", agentName);
            return Results.Problem("An error occurred while deleting the agent.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }

    public async Task<IResult> GetDefinitions(string agentName, bool basicDataOnly = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(agentName))
            {
                _logger.LogWarning("Invalid agent name provided for getting definitions");
                return Results.BadRequest("Agent name is required");
            }

            // Get the agent to check if it exists and verify permissions
            var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentName} not found", agentName);
                return Results.NotFound("Agent not found");
            }

            // Check if user has permission to access this agent
            if (!agent.Permissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Read))
            {
                _logger.LogWarning("User {UserId} attempted to access agent {AgentName} without read permission", 
                    _tenantContext.LoggedInUser, agentName);
                return Results.Problem("You do not have permission to access this agent", statusCode: StatusCodes.Status403Forbidden);
            }

            // Get definitions for the specific agent directly from the definition repository
            var definitions = await _definitionRepository.GetByNameAsync(agentName);

            return Results.Ok(definitions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving definitions for agent {AgentName}", agentName);
            return Results.Problem("An error occurred while retrieving agent definitions.", statusCode: StatusCodes.Status500InternalServerError);
        }
    }
} 