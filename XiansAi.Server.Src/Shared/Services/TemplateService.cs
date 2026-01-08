using MongoDB.Bson;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;

namespace Shared.Services;

public interface ITemplateService
{
    Task<ServiceResult<List<AgentWithDefinitions>>> GetSystemScopedAgentDefinitions(bool basicDataOnly = false);
    Task<ServiceResult<Agent>> DeployTemplate(string agentName);
    Task<ServiceResult<Agent>> DeployTemplateToTenant(string agentName, string tenantId, string userId, string? userEmail = null);
    Task<ServiceResult<bool>> DeleteSystemScopedAgent(string agentName);
}

/// <summary>
/// Service for managing template agent definitions with system_scoped attribute.
/// This service is in Shared.Services so it can be used by both WebApi and AdminApi.
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IUserRepository _userRepository;
    private readonly ILogger<TemplateService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateService"/> class.
    /// </summary>
    /// <param name="agentRepository">Repository for agent operations.</param>
    /// <param name="flowDefinitionRepository">Repository for flow definition operations.</param>
    /// <param name="userRepository">Repository for user operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    public TemplateService(
        IAgentRepository agentRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        IUserRepository userRepository,
        ILogger<TemplateService> logger,
        ITenantContext tenantContext
    )
    {
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
    }

    /// <summary>
    /// Retrieves all agent templates from both collections:
    /// 1. Legacy system-scoped agents from agents collection (SystemScoped = true)
    /// 2. New agent templates from agent_templates collection
    /// System-scoped agents are available to any logged-in user with a valid tenant without permission checks.
    /// </summary>
    /// <param name="basicDataOnly">Whether to return basic data only.</param>
    /// <returns>A service result containing the list of system-scoped agent definitions.</returns>
    public async Task<ServiceResult<List<AgentWithDefinitions>>> GetSystemScopedAgentDefinitions(bool basicDataOnly = false)
    {
        try
        {
            _logger.LogInformation("Retrieving agent templates from both collections for user {UserId} in tenant {TenantId} (no permission checks)", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);

            // Get all system-scoped agents (templates) from agents collection
            var systemScopedAgents = await _agentRepository.GetSystemScopedAgentsWithDefinitionsAsync(basicDataOnly);

            _logger.LogInformation("Found {TotalCount} system-scoped agent templates for user {UserId}", 
                systemScopedAgents.Count, _tenantContext.LoggedInUser);

            return ServiceResult<List<AgentWithDefinitions>>.Success(systemScopedAgents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving agent templates for user {UserId} in tenant {TenantId}", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);
            return ServiceResult<List<AgentWithDefinitions>>.InternalServerError(
                "An error occurred while retrieving agent templates");
        }
    }
    
    /// <summary>
    /// Gets flow definitions for a template by name.
    /// Flow definitions can reference either Agent.Name or AgentTemplate.Name.
    /// </summary>
    private async Task<List<FlowDefinition>> GetFlowDefinitionsForTemplate(string templateName, bool basicDataOnly)
    {
        // Flow definitions reference templates by name in the Agent field
        // Query flow definitions where Agent = templateName and SystemScoped = true
        // Pass null tenant to get system-scoped definitions
        var definitions = await _flowDefinitionRepository.GetByNameAsync(templateName, null);
        
        // Note: For basicDataOnly, we still return full definitions but the caller can filter fields if needed
        // The repository methods handle projection at the database level
        return definitions;
    }
    

    /// <summary>
    /// Deploys a system-scoped agent template to the current user's tenant (from tenant context).
    /// </summary>
    /// <param name="agentName">The name of the system-scoped agent to deploy.</param>
    /// <returns>A service result containing the newly created agent instance.</returns>
    public async Task<ServiceResult<Agent>> DeployTemplate(string agentName)
    {
        return await DeployTemplateToTenant(agentName, _tenantContext.TenantId, _tenantContext.LoggedInUser, null);
    }

    /// <summary>
    /// Deploys a system-scoped agent template to a specific tenant by creating an agent instance and its flow definitions.
    /// Uses the agents collection for backward compatibility.
    /// </summary>
    /// <param name="agentName">The name of the system-scoped agent to deploy.</param>
    /// <param name="tenantId">The tenant ID to deploy to.</param>
    /// <param name="userId">The user ID performing the deployment.</param>
    /// <param name="userEmail">Optional user email. If not provided, will be retrieved from user repository.</param>
    /// <returns>A service result containing the newly created agent instance.</returns>
    public async Task<ServiceResult<Agent>> DeployTemplateToTenant(string agentName, string tenantId, string userId, string? userEmail = null)
    {
        try
        {
            _logger.LogInformation("Deploying template agent {AgentName} for user {UserId} in tenant {TenantId}", 
                agentName, userId, tenantId);

            // Validate input
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<Agent>.BadRequest("Agent name is required");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<Agent>.BadRequest("Valid tenant ID is required");
            }

            if (string.IsNullOrWhiteSpace(userId))
            {
                return ServiceResult<Agent>.BadRequest("Valid user ID is required");
            }

            // Get user email if not provided
            if (string.IsNullOrWhiteSpace(userEmail))
            {
                var user = await _userRepository.GetByUserIdAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User {UserId} not found", userId);
                    return ServiceResult<Agent>.NotFound($"User '{userId}' not found");
                }

                if (string.IsNullOrWhiteSpace(user.Email))
                {
                    _logger.LogWarning("User {UserId} does not have an email address", userId);
                    return ServiceResult<Agent>.BadRequest("User email is required for agent deployment");
                }

                userEmail = user.Email;
            }

            // Find the template by name - check both collections
            Agent? templateAgent = null;
            List<FlowDefinition>? templateDefinitions = null;
            string? templateId = null;
            
            // Check agents collection for system-scoped agent (template)
            var systemAgent = await _agentRepository.GetByNameInternalAsync(agentName, null);
            if (systemAgent != null && systemAgent.SystemScoped)
            {
                templateAgent = systemAgent;
                templateId = systemAgent.Id;
                templateDefinitions = await GetFlowDefinitionsForTemplate(agentName, false);
            }
            else
            {
                // Fall back to agents collection (legacy system-scoped templates)
                var systemScopedAgents = await _agentRepository.GetSystemScopedAgentsWithDefinitionsAsync(false);
                var foundTemplate = systemScopedAgents.FirstOrDefault(a => a.Agent.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));
                
                if (foundTemplate != null)
                {
                    templateAgent = foundTemplate.Agent;
                    templateId = foundTemplate.Agent.Id; // Store Agent ID
                    templateDefinitions = foundTemplate.Definitions;
                }
            }

            if (templateAgent == null || templateDefinitions == null)
            {
                _logger.LogWarning("Agent template {AgentName} not found in either collection", agentName);
                return ServiceResult<Agent>.NotFound($"Agent template '{agentName}' not found");
            }

            // Check if agent instance already exists in tenant (using agents collection)
            var existingAgent = await _agentRepository.GetByNameInternalAsync(agentName, tenantId);
            if (existingAgent != null && !existingAgent.SystemScoped)
            {
                _logger.LogWarning("Agent instance {AgentName} already exists in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<Agent>.Conflict($"Agent '{agentName}' already exists in tenant. Delete it first if you want to redeploy.");
            }

            // Create a new agent instance in the agents collection (for backward compatibility)
            // Use userId as the identifier (can be userId, email, username, etc. depending on system)
            var adminIdentifier = userEmail ?? userId; // Prefer email if available, fallback to userId
            var newAgent = new Agent
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = templateAgent.Name,
                Tenant = tenantId,
                SystemScoped = false, // This is a tenant-scoped instance, not a template
                AgentTemplateId = templateId, // Reference to the template (AgentTemplate ID or Agent ID)
                CreatedBy = userId,
                CreatedAt = DateTime.UtcNow,
                OwnerAccess = new List<string>(),
                ReadAccess = new List<string>(),
                WriteAccess = new List<string>(),
                OnboardingJson = templateAgent.OnboardingJson,
                Metadata = templateAgent.Metadata != null ? new Dictionary<string, object>(templateAgent.Metadata) : new Dictionary<string, object>()
            };

            // Store instance-specific data in metadata
            newAgent.SetAdminId(adminIdentifier); // User who deploys becomes the admin
            newAgent.SetDeployedAt(DateTime.UtcNow);
            newAgent.SetDeployedById(adminIdentifier); // User identifier who deployed
            newAgent.SetReportingTargets(new List<ReportingTo>());

            // Grant owner access to the current user
            newAgent.GrantOwnerAccess(userId);

            // Create the agent in agents collection
            await _agentRepository.CreateAsync(newAgent);
            _logger.LogInformation("Created agent instance {AgentName} with ID {AgentId} for user {UserId} (admin: {AdminId}) in tenant {TenantId}", 
                agentName, newAgent.Id, userId, adminIdentifier, tenantId);

            // Clone all flow definitions from the template
            var clonedDefinitionsCount = 0;
            foreach (var templateDefinition in templateDefinitions)
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
                    CreatedBy = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Tenant = tenantId,
                    SystemScoped = false // User tenant definitions are not system scoped
                };

                await _flowDefinitionRepository.CreateAsync(newDefinition);
                clonedDefinitionsCount++;
                
                _logger.LogDebug("Cloned flow definition {WorkflowType} for agent {AgentName}", 
                    templateDefinition.WorkflowType, agentName);
            }

            _logger.LogInformation("Successfully deployed template agent {AgentName} with {DefinitionsCount} flow definitions for user {UserId} (admin: {AdminId}) in tenant {TenantId}", 
                agentName, clonedDefinitionsCount, userId, adminIdentifier, tenantId);

            return ServiceResult<Agent>.Success(newAgent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying template agent {AgentName} for user {UserId} in tenant {TenantId}", 
                agentName, userId, tenantId);
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

