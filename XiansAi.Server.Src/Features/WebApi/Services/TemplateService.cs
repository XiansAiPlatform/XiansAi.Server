using MongoDB.Bson;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Features.WebApi.Services;

public interface ITemplateService
{
    Task<ServiceResult<List<AgentWithDefinitions>>> GetSystemScopedAgentDefinitions(bool basicDataOnly = false);
    Task<ServiceResult<Agent>> DeployTemplate(string agentName);
    Task<ServiceResult<bool>> DeleteSystemScopedAgent(string agentName);
}

/// <summary>
/// Service for managing template agent definitions with system_scoped attribute.
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly ILogger<TemplateService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateService"/> class.
    /// </summary>
    /// <param name="agentRepository">Repository for agent operations.</param>
    /// <param name="flowDefinitionRepository">Repository for flow definition operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    public TemplateService(
        IAgentRepository agentRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        ILogger<TemplateService> logger,
        ITenantContext tenantContext
    )
    {
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Retrieves all agent definitions that have system_scoped attribute set to true.
    /// System-scoped agents are available to any logged-in user with a valid tenant without permission checks.
    /// </summary>
    /// <param name="basicDataOnly">Whether to return basic data only.</param>
    /// <returns>A service result containing the list of system-scoped agent definitions.</returns>
    public async Task<ServiceResult<List<AgentWithDefinitions>>> GetSystemScopedAgentDefinitions(bool basicDataOnly = false)
    {
        try
        {
            _logger.LogInformation("Retrieving system-scoped agent definitions for user {UserId} in tenant {TenantId} (no permission checks)", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);

            // Get system-scoped agents without permission checks
            // System-scoped agents are available to any logged-in user with a valid tenant
            var systemScopedAgents = await _agentRepository.GetSystemScopedAgentsWithDefinitionsAsync(basicDataOnly);

            _logger.LogInformation("Found {Count} system-scoped agents for user {UserId}", 
                systemScopedAgents.Count, _tenantContext.LoggedInUser);

            return ServiceResult<List<AgentWithDefinitions>>.Success(systemScopedAgents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system-scoped agent definitions for user {UserId} in tenant {TenantId}", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);
            return ServiceResult<List<AgentWithDefinitions>>.InternalServerError(
                "An error occurred while retrieving system-scoped agent definitions");
        }
    }

    /// <summary>
    /// Deploys a system-scoped agent template to the user's tenant by creating a replica of the agent and its flow definitions.
    /// </summary>
    /// <param name="agentName">The name of the system-scoped agent to deploy.</param>
    /// <returns>A service result containing the newly created agent.</returns>
    public async Task<ServiceResult<Agent>> DeployTemplate(string agentName)
    {
        try
        {
            _logger.LogInformation("Deploying template agent {AgentName} for user {UserId} in tenant {TenantId}", 
                agentName, _tenantContext.LoggedInUser, _tenantContext.TenantId);

            // Validate input
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<Agent>.BadRequest("Agent name is required");
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.TenantId))
            {
                return ServiceResult<Agent>.BadRequest("Valid tenant context is required");
            }

            if (string.IsNullOrWhiteSpace(_tenantContext.LoggedInUser))
            {
                return ServiceResult<Agent>.BadRequest("Valid user context is required");
            }

            // Find the system-scoped agent by name
            var systemScopedAgents = await _agentRepository.GetSystemScopedAgentsWithDefinitionsAsync(false);
            var templateAgent = systemScopedAgents.FirstOrDefault(a => a.Agent.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

            if (templateAgent == null)
            {
                _logger.LogWarning("System-scoped agent {AgentName} not found", agentName);
                return ServiceResult<Agent>.NotFound($"System-scoped agent '{agentName}' not found");
            }

            // Check if agent already exists in user's tenant
            var existingAgent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
            if (existingAgent != null)
            {
                _logger.LogWarning("Agent {AgentName} already exists in tenant {TenantId}", agentName, _tenantContext.TenantId);
                return ServiceResult<Agent>.Conflict($"Agent '{agentName}' already exists in your tenant. Delete it first if you want to redeploy.");
            }

            // Create a replica of the agent for the user's tenant
            var newAgent = new Agent
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = templateAgent.Agent.Name,
                Tenant = _tenantContext.TenantId,
                CreatedBy = _tenantContext.LoggedInUser,
                CreatedAt = DateTime.UtcNow,
                SystemScoped = false, // User tenant agents are not system scoped
                OnboardingJson = templateAgent.Agent.OnboardingJson,
                OwnerAccess = new List<string>(),
                ReadAccess = new List<string>(),
                WriteAccess = new List<string>()
            };

            // Grant owner access to the current user
            newAgent.GrantOwnerAccess(_tenantContext.LoggedInUser);

            // Create the agent
            await _agentRepository.CreateAsync(newAgent);
            _logger.LogInformation("Created agent replica {AgentName} with ID {AgentId} for user {UserId} in tenant {TenantId}", 
                agentName, newAgent.Id, _tenantContext.LoggedInUser, _tenantContext.TenantId);

            // Clone all flow definitions from the template
            var clonedDefinitionsCount = 0;
            foreach (var templateDefinition in templateAgent.Definitions)
            {
                var newDefinition = new FlowDefinition
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    WorkflowType = templateDefinition.WorkflowType,
                    Agent = agentName, // Keep the same agent name
                    Name = templateDefinition.Name,
                    Hash = templateDefinition.Hash, // Keep the same hash for consistency
                    Source = templateDefinition.Source,
                    Markdown = templateDefinition.Markdown,
                    ActivityDefinitions = CloneActivityDefinitions(templateDefinition.ActivityDefinitions),
                    ParameterDefinitions = CloneParameterDefinitions(templateDefinition.ParameterDefinitions),
                    CreatedBy = _tenantContext.LoggedInUser,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Tenant = _tenantContext.TenantId,
                    SystemScoped = false // User tenant definitions are not system scoped
                };

                await _flowDefinitionRepository.CreateAsync(newDefinition);
                clonedDefinitionsCount++;
                
                _logger.LogDebug("Cloned flow definition {WorkflowType} for agent {AgentName}", 
                    templateDefinition.WorkflowType, agentName);
            }

            _logger.LogInformation("Successfully deployed template agent {AgentName} with {DefinitionsCount} flow definitions for user {UserId} in tenant {TenantId}", 
                agentName, clonedDefinitionsCount, _tenantContext.LoggedInUser, _tenantContext.TenantId);

            return ServiceResult<Agent>.Success(newAgent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying template agent {AgentName} for user {UserId} in tenant {TenantId}", 
                agentName, _tenantContext.LoggedInUser, _tenantContext.TenantId);
            return ServiceResult<Agent>.InternalServerError(
                "An error occurred while deploying the template");
        }
    }

    /// <summary>
    /// Creates deep copies of activity definitions to avoid reference sharing.
    /// </summary>
    private List<ActivityDefinition> CloneActivityDefinitions(List<ActivityDefinition> original)
    {
        return original.Select(activity => new ActivityDefinition
        {
            ActivityName = activity.ActivityName,
            AgentToolNames = activity.AgentToolNames?.ToList(), // Create new list if not null
            KnowledgeIds = activity.KnowledgeIds.ToList(), // Create new list
            ParameterDefinitions = CloneParameterDefinitions(activity.ParameterDefinitions)
        }).ToList();
    }

    /// <summary>
    /// Creates deep copies of parameter definitions to avoid reference sharing.
    /// </summary>
    private List<ParameterDefinition> CloneParameterDefinitions(List<ParameterDefinition> original)
    {
        return original.Select(param => new ParameterDefinition
        {
            Name = param.Name,
            Type = param.Type
        }).ToList();
    }

    /// <summary>
    /// Deletes a system-scoped agent and its associated flow definitions.
    /// This operation is only available to system administrators.
    /// </summary>
    /// <param name="agentName">The name of the system-scoped agent to delete.</param>
    /// <returns>A service result indicating success or failure.</returns>
    public async Task<ServiceResult<bool>> DeleteSystemScopedAgent(string agentName)
    {
        try
        {
            _logger.LogInformation("Attempting to delete system-scoped agent {AgentName} by user {UserId}", 
                agentName, _tenantContext.LoggedInUser);

            // Validate input
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<bool>.BadRequest("Agent name is required");
            }

            // Find the system-scoped agent by name (null tenant for system-scoped agents)
            var agent = await _agentRepository.GetByNameInternalAsync(agentName, null!);
            
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentName} not found", agentName);
                return ServiceResult<bool>.NotFound($"Agent '{agentName}' not found");
            }

            // Verify that the agent is system-scoped
            if (!agent.SystemScoped)
            {
                _logger.LogWarning("Agent {AgentName} is not system-scoped", agentName);
                return ServiceResult<bool>.BadRequest($"Agent '{agentName}' is not a system-scoped agent. Only system-scoped agents can be deleted through this endpoint.");
            }

            // Delete all flow definitions associated with this agent
            var deletedDefinitionsCount = await _flowDefinitionRepository.DeleteByAgentAsync(agentName, null!);
            _logger.LogInformation("Deleted {Count} flow definitions for system-scoped agent {AgentName}", 
                deletedDefinitionsCount, agentName);

            // Delete the agent (permission checks in DeleteAsync will pass for SysAdmin)
            var success = await _agentRepository.DeleteAsync(agent.Id, _tenantContext.LoggedInUser, _tenantContext.UserRoles.ToArray());
            
            if (!success)
            {
                _logger.LogWarning("Failed to delete system-scoped agent {AgentName}", agentName);
                return ServiceResult<bool>.InternalServerError($"Failed to delete system-scoped agent '{agentName}'");
            }

            _logger.LogInformation("Successfully deleted system-scoped agent {AgentName} with {DefinitionsCount} flow definitions", 
                agentName, deletedDefinitionsCount);

            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting system-scoped agent {AgentName}", agentName);
            return ServiceResult<bool>.InternalServerError(
                "An error occurred while deleting the system-scoped agent");
        }
    }
}
