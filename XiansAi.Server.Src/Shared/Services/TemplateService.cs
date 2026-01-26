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
    Task<ServiceResult<bool>> DeleteSystemScopedAgent(string agentName);
    Task<ServiceResult<Agent>> DeployTemplateToTenant(string agentName, string tenantId, string createdBy, string? onboardingJson = null);
    Task<ServiceResult<Agent>> GetSystemScopedAgentByIdAsync(string templateObjectId);
    Task<ServiceResult<Agent>> UpdateSystemScopedAgentAsync(string templateObjectId, string? description, string? onboardingJson, List<string>? ownerAccess, List<string>? readAccess, List<string>? writeAccess);
}

/// <summary>
/// Service for managing template agent definitions with system_scoped attribute.
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IKnowledgeRepository _knowledgeRepository;
    private readonly ILogger<TemplateService> _logger;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Initializes a new instance of the <see cref="TemplateService"/> class.
    /// </summary>
    /// <param name="agentRepository">Repository for agent operations.</param>
    /// <param name="flowDefinitionRepository">Repository for flow definition operations.</param>
    /// <param name="knowledgeRepository">Repository for knowledge operations.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    /// <param name="tenantContext">Context for the current tenant and user information.</param>
    public TemplateService(
        IAgentRepository agentRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        IKnowledgeRepository knowledgeRepository,
        ILogger<TemplateService> logger,
        ITenantContext tenantContext
    )
    {
        _agentRepository = agentRepository ?? throw new ArgumentNullException(nameof(agentRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _knowledgeRepository = knowledgeRepository ?? throw new ArgumentNullException(nameof(knowledgeRepository));
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
                Description = templateAgent.Agent.Description,
                Version = templateAgent.Agent.Version,
                Author = templateAgent.Agent.Author,
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

            // Clone all system-scoped knowledge associated with the template agent
            var systemKnowledge = await _knowledgeRepository.GetSystemScopedByAgentAsync<Knowledge>(agentName);
            var clonedKnowledgeCount = 0;
            
            foreach (var templateKnowledge in systemKnowledge)
            {
                var newKnowledge = new Knowledge
                {
                    Id = ObjectId.GenerateNewId().ToString(),
                    Name = templateKnowledge.Name,
                    Content = templateKnowledge.Content,
                    Type = templateKnowledge.Type,
                    Version = templateKnowledge.Version,
                    Agent = agentName, // Keep the same agent name
                    TenantId = _tenantContext.TenantId, // Assign to user's tenant
                    CreatedBy = _tenantContext.LoggedInUser,
                    CreatedAt = DateTime.UtcNow,
                    SystemScoped = false // User tenant knowledge is not system scoped
                };

                await _knowledgeRepository.CreateAsync(newKnowledge);
                clonedKnowledgeCount++;
                
                _logger.LogDebug("Cloned knowledge {KnowledgeName} for agent {AgentName}", 
                    templateKnowledge.Name, agentName);
            }

            _logger.LogInformation("Successfully deployed template agent {AgentName} with {DefinitionsCount} flow definitions and {KnowledgeCount} knowledge items for user {UserId} in tenant {TenantId}", 
                agentName, clonedDefinitionsCount, clonedKnowledgeCount, _tenantContext.LoggedInUser, _tenantContext.TenantId);

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
            Type = param.Type,
            Description = param.Description,
            Optional = param.Optional
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

    /// <summary>
    /// Deploys a system-scoped agent template to a specific tenant by creating a replica of the agent and its flow definitions.
    /// </summary>
    /// <param name="agentName">The name of the system-scoped agent to deploy.</param>
    /// <param name="tenantId">The tenant ID to deploy the template to.</param>
    /// <param name="createdBy">The user ID creating the deployment.</param>
    /// <param name="onboardingJson">Optional onboarding JSON to override the template's onboarding JSON.</param>
    /// <returns>A service result containing the newly created agent.</returns>
    public async Task<ServiceResult<Agent>> DeployTemplateToTenant(string agentName, string tenantId, string createdBy, string? onboardingJson = null)
    {
        try
        {
            _logger.LogInformation("Deploying template agent {AgentName} to tenant {TenantId} by user {CreatedBy}", 
                agentName, tenantId, createdBy);

            // Validate input
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return ServiceResult<Agent>.BadRequest("Agent name is required");
            }

            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return ServiceResult<Agent>.BadRequest("Tenant ID is required");
            }

            if (string.IsNullOrWhiteSpace(createdBy))
            {
                return ServiceResult<Agent>.BadRequest("Created by user ID is required");
            }

            // Find the system-scoped agent by name
            var systemScopedAgents = await _agentRepository.GetSystemScopedAgentsWithDefinitionsAsync(false);
            var templateAgent = systemScopedAgents.FirstOrDefault(a => a.Agent.Name.Equals(agentName, StringComparison.OrdinalIgnoreCase));

            if (templateAgent == null)
            {
                _logger.LogWarning("System-scoped agent {AgentName} not found", agentName);
                return ServiceResult<Agent>.NotFound($"System-scoped agent '{agentName}' not found");
            }

            // Check if agent already exists in the target tenant
            var existingAgent = await _agentRepository.GetByNameInternalAsync(agentName, tenantId);
            if (existingAgent != null)
            {
                _logger.LogWarning("Agent {AgentName} already exists in tenant {TenantId}", agentName, tenantId);
                return ServiceResult<Agent>.Conflict($"Agent '{agentName}' already exists in tenant '{tenantId}'. Delete it first if you want to redeploy.");
            }

            // Create a replica of the agent for the target tenant
            var newAgent = new Agent
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = templateAgent.Agent.Name,
                Tenant = tenantId,
                CreatedBy = createdBy,
                CreatedAt = DateTime.UtcNow,
                SystemScoped = false, // User tenant agents are not system scoped
                OnboardingJson = onboardingJson ?? templateAgent.Agent.OnboardingJson,
                Description = templateAgent.Agent.Description,
                Version = templateAgent.Agent.Version,
                Author = templateAgent.Agent.Author,
                OwnerAccess = new List<string>(),
                ReadAccess = new List<string>(),
                WriteAccess = new List<string>()
            };

            // Grant owner access to the creating user
            newAgent.GrantOwnerAccess(createdBy);

            // Create the agent
            await _agentRepository.CreateAsync(newAgent);
            _logger.LogInformation("Created agent replica {AgentName} with ID {AgentId} for tenant {TenantId} by user {CreatedBy}", 
                agentName, newAgent.Id, tenantId, createdBy);

            // Clone all flow definitions from the template
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
                    CreatedBy = createdBy,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    Tenant = tenantId,
                    SystemScoped = false // User tenant definitions are not system scoped
                };

                await _flowDefinitionRepository.CreateAsync(newDefinition);
                
                _logger.LogDebug("Cloned flow definition {WorkflowType} for agent {AgentName}", 
                    templateDefinition.WorkflowType, agentName);
            }

            // Clone all system-scoped knowledge associated with the template agent
            // var systemKnowledge = await _knowledgeRepository.GetSystemScopedByAgentAsync<Knowledge>(agentName);
            // var clonedKnowledgeCount = 0;
            
            // foreach (var templateKnowledge in systemKnowledge)
            // {
            //     var newKnowledge = new Knowledge
            //     {
            //         Id = ObjectId.GenerateNewId().ToString(),
            //         Name = templateKnowledge.Name,
            //         Content = templateKnowledge.Content,
            //         Type = templateKnowledge.Type,
            //         Version = templateKnowledge.Version,
            //         Agent = agentName, // Keep the same agent name
            //         TenantId = tenantId, // Assign to target tenant
            //         CreatedBy = createdBy,
            //         CreatedAt = DateTime.UtcNow,
            //         SystemScoped = false // User tenant knowledge is not system scoped
            //     };

            //     await _knowledgeRepository.CreateAsync(newKnowledge);
            //     clonedKnowledgeCount++;
                
            //     _logger.LogDebug("Cloned knowledge {KnowledgeName} for agent {AgentName}", 
            //         templateKnowledge.Name, agentName);
            // }

            // _logger.LogInformation("Successfully deployed template agent {AgentName} to tenant {TenantId} with {DefinitionsCount} flow definitions and {KnowledgeCount} knowledge items by user {CreatedBy}", 
            //     agentName, tenantId, clonedDefinitionsCount, clonedKnowledgeCount, createdBy);

            return ServiceResult<Agent>.Success(newAgent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deploying template agent {AgentName} to tenant {TenantId} by user {CreatedBy}", 
                agentName, tenantId, createdBy);
            return ServiceResult<Agent>.InternalServerError(
                "An error occurred while deploying the template to tenant");
        }
    }

    /// <summary>
    /// Gets a system-scoped agent template by its MongoDB ObjectId.
    /// </summary>
    /// <param name="templateObjectId">The MongoDB ObjectId of the template.</param>
    /// <returns>A service result containing the template agent.</returns>
    public async Task<ServiceResult<Agent>> GetSystemScopedAgentByIdAsync(string templateObjectId)
    {
        try
        {
            _logger.LogInformation("Retrieving system-scoped agent template by ID {TemplateObjectId}", templateObjectId);

            if (string.IsNullOrWhiteSpace(templateObjectId))
            {
                return ServiceResult<Agent>.BadRequest("Template ObjectId is required");
            }

            var template = await _agentRepository.GetByIdInternalAsync(templateObjectId);
            if (template == null)
            {
                _logger.LogWarning("Template with ID {TemplateObjectId} not found", templateObjectId);
                return ServiceResult<Agent>.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            if (!template.SystemScoped)
            {
                _logger.LogWarning("Agent with ID {TemplateObjectId} is not system-scoped", templateObjectId);
                return ServiceResult<Agent>.BadRequest($"Agent with ID '{templateObjectId}' is not a system-scoped template");
            }

            return ServiceResult<Agent>.Success(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving system-scoped agent template by ID {TemplateObjectId}", templateObjectId);
            return ServiceResult<Agent>.InternalServerError(
                "An error occurred while retrieving the template");
        }
    }

    /// <summary>
    /// Updates a system-scoped agent template.
    /// </summary>
    /// <param name="templateObjectId">The MongoDB ObjectId of the template.</param>
    /// <param name="description">Optional description to update.</param>
    /// <param name="onboardingJson">Optional onboarding JSON to update.</param>
    /// <param name="ownerAccess">Optional owner access list to update.</param>
    /// <param name="readAccess">Optional read access list to update.</param>
    /// <param name="writeAccess">Optional write access list to update.</param>
    /// <returns>A service result containing the updated template agent.</returns>
    public async Task<ServiceResult<Agent>> UpdateSystemScopedAgentAsync(string templateObjectId, string? description, string? onboardingJson, List<string>? ownerAccess, List<string>? readAccess, List<string>? writeAccess)
    {
        try
        {
            _logger.LogInformation("Updating system-scoped agent template by ID {TemplateObjectId}", templateObjectId);

            if (string.IsNullOrWhiteSpace(templateObjectId))
            {
                return ServiceResult<Agent>.BadRequest("Template ObjectId is required");
            }

            var template = await _agentRepository.GetByIdInternalAsync(templateObjectId);
            if (template == null)
            {
                _logger.LogWarning("Template with ID {TemplateObjectId} not found", templateObjectId);
                return ServiceResult<Agent>.NotFound($"Agent template with ID '{templateObjectId}' not found");
            }

            if (!template.SystemScoped)
            {
                _logger.LogWarning("Agent with ID {TemplateObjectId} is not system-scoped", templateObjectId);
                return ServiceResult<Agent>.BadRequest($"Agent with ID '{templateObjectId}' is not a system-scoped template");
            }

            // Update fields if provided
            if (description != null)
            {
                template.Description = description;
            }

            if (onboardingJson != null)
            {
                template.OnboardingJson = onboardingJson;
            }

            if (ownerAccess != null)
            {
                template.OwnerAccess = ownerAccess;
            }

            if (readAccess != null)
            {
                template.ReadAccess = readAccess;
            }

            if (writeAccess != null)
            {
                template.WriteAccess = writeAccess;
            }

            var updated = await _agentRepository.UpdateInternalAsync(template.Id, template);
            if (!updated)
            {
                _logger.LogWarning("Failed to update template with ID {TemplateObjectId}", templateObjectId);
                return ServiceResult<Agent>.InternalServerError("Failed to update the template");
            }

            _logger.LogInformation("Successfully updated system-scoped agent template {TemplateObjectId}", templateObjectId);
            return ServiceResult<Agent>.Success(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating system-scoped agent template by ID {TemplateObjectId}", templateObjectId);
            return ServiceResult<Agent>.InternalServerError(
                "An error occurred while updating the template");
        }
    }
}


