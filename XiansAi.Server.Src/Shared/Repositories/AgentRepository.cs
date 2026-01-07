using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// Represents an agent with its associated flow definitions
/// </summary>
public class AgentWithDefinitions
{
    public required Agent Agent { get; set; }
    public required List<FlowDefinition> Definitions { get; set; }
}

public interface IAgentRepository
{
    Task<Agent?> GetByNameAsync(string name, string tenant, string userId, string[] userRoles);
    Task<Agent?> GetByIdAsync(string id, string userId, string[] userRoles);
    Task<List<Agent>> GetAgentsWithPermissionAsync(string userId, string tenant);
    Task CreateAsync(Agent agent);
    Task<bool> UpdateAsync(string id, Agent agent, string userId, string[] userRoles);
    Task<bool> UpdatePermissionsAsync(string name, string tenant, Permission permissions, string userId, string[] userRoles);
    Task<bool> DeleteAsync(string id, string userId, string[] userRoles);
    
    // Internal methods without permission checking (for system use)
    Task<Agent?> GetByNameInternalAsync(string name, string? tenant);
    Task<Agent?> GetByIdInternalAsync(string id);
    Task<bool> IsSystemAgent(string name);
    Task<bool> UpdateInternalAsync(string id, Agent agent);
    Task<Agent> UpsertAgentAsync(string agentName, bool systemScoped, string tenant, string createdBy, string? onboardingJson = null);

    Task<List<AgentWithDefinitions>> GetAgentsWithDefinitionsAsync(string userId, string tenant, DateTime? startTime, DateTime? endTime, bool basicDataOnly = false);
    Task<List<AgentWithDefinitions>> GetSystemScopedAgentsWithDefinitionsAsync(bool basicDataOnly = false);

    // Helper methods for instance management (using metadata)
    Task<bool> ExistsAsync(string name, string tenant);
    Task<bool> AddReportingTargetAsync(string agentId, ReportingTo reportingTarget);
    Task<bool> RemoveReportingTargetAsync(string agentId, string targetId);
    
    // Legacy methods for backward compatibility
    [Obsolete("Use AddReportingTargetAsync instead")]
    Task<bool> AddReportingUserAsync(string agentId, string userId);
    [Obsolete("Use RemoveReportingTargetAsync instead")]
    Task<bool> RemoveReportingUserAsync(string agentId, string userId);

}

public class AgentRepository : IAgentRepository
{
    private readonly IMongoCollection<Agent> _agents;
    private readonly IMongoCollection<FlowDefinition> _definitions;
    private readonly IMongoCollection<User> _users;
    private readonly ILogger<AgentRepository> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAgentTemplateRepository _agentTemplateRepository;

    public AgentRepository(IDatabaseService databaseService, ILogger<AgentRepository> logger, ITenantContext tenantContext, IAgentTemplateRepository agentTemplateRepository)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _agents = database.GetCollection<Agent>("agents");
        _logger = logger;
        _definitions = database.GetCollection<FlowDefinition>("flow_definitions");
        _users = database.GetCollection<User>("users");
        _tenantContext = tenantContext;
        _agentTemplateRepository = agentTemplateRepository;
    }

    public async Task<bool> IsSystemAgent(string name)
    {
        // Check both collections:
        // 1. Legacy system-scoped agents in agents collection (Tenant is null)
        // 2. New agent templates in agent_templates collection
        var legacyAgent = await _agents.Find(x => x.Name == name && x.Tenant == null).FirstOrDefaultAsync();
        if (legacyAgent != null)
        {
            return true;
        }
        
        // Check agent_templates collection
        var template = await _agentTemplateRepository.GetByNameAsync(name);
        return template != null;
    }   

    public async Task<Agent?> GetByNameAsync(string name, string tenant, string userId, string[] userRoles)
    {
        var agent = await GetByNameInternalAsync(name, tenant);
        if (agent == null)
        {
            return null;
        }

        // Check if user has at least read permission
        if (!CheckPermissions(agent, PermissionLevel.Read))
        {
            _logger.LogWarning("User {UserId} attempted to access agent {AgentName} without read permission", userId, name);
            return null;
        }

        return agent;
    }

    public async Task<Agent?> GetByNameInternalAsync(string name, string? tenant)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Handle null tenant explicitly for system-scoped agents
            if (tenant == null)
            {
                return await _agents.Find(x => x.Name == name && x.Tenant == null).FirstOrDefaultAsync();
            }
            return await _agents.Find(x => x.Name == name && x.Tenant == tenant).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAgentByName");
    }

    public async Task<Agent?> GetByIdAsync(string id, string userId, string[] userRoles)
    {
        var agent = await GetByIdInternalAsync(id);
        if (agent == null)
        {
            return null;
        }

        // Check if user has at least read permission
        if (!CheckPermissions(agent, PermissionLevel.Read))
        {
            _logger.LogWarning("User {UserId} attempted to access agent {AgentId} without read permission", userId, id);
            return null;
        }

        return agent;
    }

    public async Task<Agent?> GetByIdInternalAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            if (!ObjectId.TryParse(id, out _))
            {
                return null;
            }
            return await _agents.Find(x => x.Id == id).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetAgentById");
    }

    public async Task<List<Agent>> GetAgentsWithPermissionAsync(string userId, string tenant)
    {
        var filterBuilder = Builders<Agent>.Filter;
        var filters = new List<FilterDefinition<Agent>>();

        // Build tenant filter - handle null explicitly for system-scoped agents
        var tenantFilter = tenant == null 
            ? filterBuilder.Eq(x => x.Tenant, null)
            : filterBuilder.Eq(x => x.Tenant, tenant);

        // System admin has access to everything in all tenants
        if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            // For system admin, we only filter by tenant if provided
            return await _agents.Find(tenantFilter).ToListAsync();
        }

        // Tenant admin has access to everything in their tenant
        if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
        {
            // For tenant admin, we ensure they can only access their tenant
            if (_tenantContext.TenantId.Equals(tenant, StringComparison.OrdinalIgnoreCase))
            {
                return await _agents.Find(tenantFilter).ToListAsync();
            }
        }

        // For regular users, check explicit permissions
        var permissionFilters = new List<FilterDefinition<Agent>>
    {
        // Check if user is owner
        filterBuilder.AnyEq("owner_access", userId),
        // Check if user has read access
        filterBuilder.AnyEq("read_access", userId),
        // Check if user has write access (which includes read)
        filterBuilder.AnyEq("write_access", userId),

        // for backward compatibility
        filterBuilder.AnyEq("permissions.owner_access", userId),
        filterBuilder.AnyEq("permissions.read_access", userId),
        filterBuilder.AnyEq("permissions.write_access", userId)
    };

        var permissionFilter = filterBuilder.Or(permissionFilters);

        // Combine filters
        var finalFilter = filterBuilder.And(permissionFilter, tenantFilter);

        return await _agents.Find(finalFilter).ToListAsync();
    }

    public async Task CreateAsync(Agent agent)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            agent.CreatedAt = DateTime.UtcNow;
            await _agents.InsertOneAsync(agent);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateAgent");
    }

    public async Task<bool> UpdateAsync(string id, Agent agent, string userId, string[] userRoles)
    {
        var existingAgent = await _agents.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (existingAgent == null)
        {
            _logger.LogWarning("Agent with ID {AgentId} not found", id);
            return false;
        }

        // Check if user has write permission
        if (!CheckPermissions(existingAgent, PermissionLevel.Write))
        {
            _logger.LogWarning("User {UserId} attempted to update agent {AgentName} without write permission", userId, existingAgent.Name);
            return false;
        }

        return await UpdateInternalAsync(id, agent);
    }

    public async Task<bool> UpdateInternalAsync(string id, Agent agent)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var result = await _agents.ReplaceOneAsync(x => x.Id == id, agent);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateAgent");
    }

    public async Task<bool> UpdatePermissionsAsync(string name, string tenant, Permission permissions, string userId, string[] userRoles)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var agent = await GetByNameInternalAsync(name, tenant);
            if (agent == null)
            {
                _logger.LogWarning("Agent {AgentName} not found in tenant {Tenant}", name, tenant);
                return false;
            }   

            // Check if user has owner permission
            if (!CheckPermissions(agent, PermissionLevel.Owner))
            {
                _logger.LogWarning("User {UserId} attempted to update permissions for agent {AgentName} without owner permission", userId, name);
                return false;
            }

            // Handle null tenant explicitly for system-scoped agents
            var tenantFilter = tenant == null
                ? Builders<Agent>.Filter.Eq(x => x.Tenant, null)
                : Builders<Agent>.Filter.Eq(x => x.Tenant, tenant);

            var filter = Builders<Agent>.Filter.And(
                Builders<Agent>.Filter.Eq(x => x.Name, name),
                tenantFilter
            );
            
            var update = Builders<Agent>.Update.Set(x => x.OwnerAccess, permissions.OwnerAccess)
                .Set(x => x.ReadAccess, permissions.ReadAccess)
                .Set(x => x.WriteAccess, permissions.WriteAccess);
            var result = await _agents.UpdateOneAsync(filter, update);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpdateAgentPermissions");
    }

    public async Task<bool> DeleteAsync(string id, string userId, string[] userRoles)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var agent = await _agents.Find(x => x.Id == id).FirstOrDefaultAsync();
            if (agent == null)
            {
                _logger.LogWarning("Agent with ID {AgentId} not found", id);
                return false;
            }

            // Check if user has owner permission
            if (!CheckPermissions(agent, PermissionLevel.Owner))
            {
                _logger.LogWarning("User {UserId} attempted to delete agent {AgentName} without owner permission", userId, agent.Name);
                return false;
            }

            // Delete associated flow definitions first
            var definitionFilter = Builders<FlowDefinition>.Filter.And(
                Builders<FlowDefinition>.Filter.Eq(x => x.Agent, agent.Name),
                Builders<FlowDefinition>.Filter.Eq(x => x.Tenant, agent.Tenant),
                Builders<FlowDefinition>.Filter.Eq(x => x.SystemScoped, agent.SystemScoped)
            );

            var definitionDeleteResult = await _definitions.DeleteManyAsync(definitionFilter);
            _logger.LogInformation("Deleted {Count} flow definitions for agent {AgentName}", definitionDeleteResult.DeletedCount, agent.Name);

            // Delete the agent
            var result = await _agents.DeleteOneAsync(x => x.Id == id);
            return result.DeletedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteAgent");
    }

    public async Task<List<AgentWithDefinitions>> GetAgentsWithDefinitionsAsync(string userId, string tenant, DateTime? startTime, DateTime? endTime, bool basicDataOnly = false)
    {
        _logger.LogInformation("Getting agents with definitions for user: {UserId}, tenant: {Tenant}, start time: {StartTime} and end time: {EndTime}", userId, tenant, startTime, endTime);

        // First, get agents where user has read permission
        var allowedAgents = await GetAgentsWithPermissionAsync(userId, tenant);
        
        if (!allowedAgents.Any())
        {
            _logger.LogInformation("User {UserId} has no access to any agents in tenant {Tenant}", userId, tenant);
            return new List<AgentWithDefinitions>();
        }

        var agentNames = allowedAgents.Select(a => a.Name).ToList();
        var filterBuilder = Builders<FlowDefinition>.Filter;
        
        // Handle null tenant explicitly for system-scoped agents
        var tenantFilter = tenant == null
            ? filterBuilder.Eq(x => x.Tenant, null)
            : filterBuilder.Eq(x => x.Tenant, tenant);
        
        var filters = new List<FilterDefinition<FlowDefinition>>
        {
            filterBuilder.In(x => x.Agent, agentNames),
            tenantFilter
        };
        if (startTime.HasValue)
            filters.Add(filterBuilder.Gte(x => x.UpdatedAt, startTime.Value));
        if (endTime.HasValue)
            filters.Add(filterBuilder.Lte(x => x.UpdatedAt, endTime.Value));
        var finalFilter = filterBuilder.And(filters);

        var findFluent = _definitions.Find(finalFilter).SortByDescending(x => x.UpdatedAt);
        List<FlowDefinition> allDefinitions;
        if (basicDataOnly)
        {
            // Project only basic fields
            allDefinitions = await findFluent
                .Project<FlowDefinition>(Builders<FlowDefinition>.Projection
                    .Include(x => x.Agent)
                    .Include(x => x.WorkflowType)
                    .Include(x => x.Name)
                    .Include(x => x.CreatedAt)
                    .Include(x => x.UpdatedAt))
                .ToListAsync();
        }
        else
        {
            allDefinitions = await findFluent.ToListAsync();
            await ReplaceCreatedByWithUserNames(allDefinitions);
        }

        var definitionsByAgent = allDefinitions.GroupBy(d => d.Agent)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = allowedAgents.Select(agent => new AgentWithDefinitions
        {
            Agent = agent,
            Definitions = definitionsByAgent.TryGetValue(agent.Name, out var defs) ? defs : new List<FlowDefinition>()
        }).ToList();

        _logger.LogInformation("Returning {Count} agents with their definitions for user {UserId}", result.Count, userId);
        return result;
    }

    public async Task<List<AgentWithDefinitions>> GetSystemScopedAgentsWithDefinitionsAsync(bool basicDataOnly = false)
    {
        _logger.LogInformation("Getting system-scoped agents with definitions (no permission checks)");

        // Get all system-scoped agents (where SystemScoped = true)
        var systemScopedAgents = await _agents.Find(x => x.SystemScoped == true).ToListAsync();
        
        if (!systemScopedAgents.Any())
        {
            _logger.LogInformation("No system-scoped agents found");
            return new List<AgentWithDefinitions>();
        }

        var agentNames = systemScopedAgents.Select(a => a.Name).ToList();
        var filterBuilder = Builders<FlowDefinition>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.In(x => x.Agent, agentNames),
            filterBuilder.Eq(x => x.SystemScoped, true)
        );

        var findFluent = _definitions.Find(filter).SortByDescending(x => x.UpdatedAt);
        List<FlowDefinition> allDefinitions;
        
        if (basicDataOnly)
        {
            // Project only basic fields
            allDefinitions = await findFluent
                .Project<FlowDefinition>(Builders<FlowDefinition>.Projection
                    .Include(x => x.Agent)
                    .Include(x => x.WorkflowType)
                    .Include(x => x.Name)
                    .Include(x => x.CreatedAt)
                    .Include(x => x.UpdatedAt))
                .ToListAsync();
        }
        else
        {
            allDefinitions = await findFluent.ToListAsync();
            await ReplaceCreatedByWithUserNames(allDefinitions);
        }

        var definitionsByAgent = allDefinitions.GroupBy(d => d.Agent)
            .ToDictionary(g => g.Key, g => g.ToList());

        var result = systemScopedAgents.Select(agent => new AgentWithDefinitions
        {
            Agent = agent,
            Definitions = definitionsByAgent.TryGetValue(agent.Name, out var defs) ? defs : new List<FlowDefinition>()
        }).ToList();

        _logger.LogInformation("Returning {Count} system-scoped agents with their definitions", result.Count);
        return result;
    }

    private async Task ReplaceCreatedByWithUserNames(List<FlowDefinition> definitions)
    {
        if (definitions == null || definitions.Count == 0)
        {
            return;
        }

        try
        {
            var createdByIds = definitions
                .Select(d => d.CreatedBy)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();

            if (createdByIds.Count == 0)
            {
                return;
            }

            var filter = Builders<User>.Filter.In(u => u.UserId, createdByIds);
            var projection = Builders<User>.Projection.Expression(u => new { u.UserId, u.Name });
            var users = await _users.Find(filter).Project(projection).ToListAsync();

            var idToName = users
                .Where(u => !string.IsNullOrWhiteSpace(u.UserId))
                .ToDictionary(u => u.UserId, u => u.Name);

            foreach (var def in definitions)
            {
                if (!string.IsNullOrWhiteSpace(def.CreatedBy) && idToName.TryGetValue(def.CreatedBy, out var name))
                {
                    def.CreatedBy = name;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to replace CreatedBy with user names. Returning original CreatedBy values.");
        }
    }

    public async Task<Agent> UpsertAgentAsync(string agentName, bool systemScoped, string tenant, string createdBy, string? onboardingJson = null)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            _logger.LogInformation("Upserting agent: {AgentName} for user: {UserId} in tenant: {Tenant}, systemScoped: {SystemScoped}", 
                agentName, createdBy, tenant, systemScoped);
            
            // For system-scoped agents, use agent_templates collection
            if (systemScoped)
            {
                return await UpsertSystemScopedAgentAsync(agentName, createdBy, onboardingJson);
            }
            
            // For tenant-scoped agents, use agents collection (existing behavior)
            return await UpsertTenantScopedAgentAsync(agentName, tenant, createdBy, onboardingJson);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "UpsertAgent");
    }

    /// <summary>
    /// Upserts a system-scoped agent template.
    /// First checks agents collection for legacy system-scoped agents (backward compatibility).
    /// If not found, creates/updates in agent_templates collection.
    /// </summary>
    private async Task<Agent> UpsertSystemScopedAgentAsync(string agentName, string createdBy, string? onboardingJson = null)
    {
        // STEP 1: Check if this is a legacy system-scoped agent in the agents collection
        // This handles backward compatibility for old agents that are just restarting
        var legacyAgent = await GetByNameInternalAsync(agentName, null);
        if (legacyAgent != null && legacyAgent.SystemScoped)
        {
            _logger.LogInformation("Found legacy system-scoped agent {AgentName} in agents collection, using existing agent (not creating in agent_templates)", agentName);
            
            // Update OnboardingJson if provided
            if (!string.IsNullOrEmpty(onboardingJson) && legacyAgent.OnboardingJson != onboardingJson)
            {
                _logger.LogInformation("Updating OnboardingJson for legacy system-scoped agent {AgentName}", agentName);
                legacyAgent.OnboardingJson = onboardingJson;
                await UpdateInternalAsync(legacyAgent.Id, legacyAgent);
            }
            
            return legacyAgent;
        }
        
        // STEP 2: No legacy agent found, proceed with agent_templates collection
        // Create new template object
        var newTemplate = new AgentTemplate
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            OnboardingJson = onboardingJson,
            OwnerAccess = new List<string>(),
            ReadAccess = new List<string>(),
            WriteAccess = new List<string>(),
            Metadata = new Dictionary<string, object>()
        };
        newTemplate.GrantOwnerAccess(createdBy);

        try
        {
            // Try to insert the new template - will fail if duplicate exists due to unique index
            await _agentTemplateRepository.CreateAsync(newTemplate);
            _logger.LogInformation("Agent template {AgentName} created successfully in agent_templates collection", agentName);
            return ConvertTemplateToAgent(newTemplate);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("already exists"))
        {
            _logger.LogInformation("Agent template {AgentName} already exists in agent_templates, retrieving existing template", agentName);
            
            // Template already exists, get the existing one
            var existingTemplate = await _agentTemplateRepository.GetByNameAsync(agentName);
            if (existingTemplate != null)
            {
                // Update OnboardingJson if provided
                if (!string.IsNullOrEmpty(onboardingJson) && existingTemplate.OnboardingJson != onboardingJson)
                {
                    _logger.LogInformation("Updating OnboardingJson for existing agent template {AgentName}", agentName);
                    existingTemplate.OnboardingJson = onboardingJson;
                    await _agentTemplateRepository.UpdateAsync(existingTemplate.Id, existingTemplate);
                }
                
                return ConvertTemplateToAgent(existingTemplate);
            }
            
            // This shouldn't happen, but if it does, throw the original exception
            _logger.LogError(ex, "Agent template {AgentName} duplicate key error but could not retrieve existing template", agentName);
            throw new InvalidOperationException($"Agent template {agentName} creation failed due to duplicate key, but existing template could not be retrieved", ex);
        }
    }

    /// <summary>
    /// Upserts a tenant-scoped agent in the agents collection (existing behavior).
    /// </summary>
    private async Task<Agent> UpsertTenantScopedAgentAsync(string agentName, string tenant, string createdBy, string? onboardingJson = null)
    {
        // Create new agent object
        var newAgent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            SystemScoped = false,
            Tenant = tenant,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            OwnerAccess = new List<string>(),
            ReadAccess = new List<string>(),
            WriteAccess = new List<string>(),
            OnboardingJson = onboardingJson
        };
        newAgent.GrantOwnerAccess(createdBy);

        try
        {
            // Try to insert the new agent - will fail if duplicate exists due to unique index
            await _agents.InsertOneAsync(newAgent);
            _logger.LogInformation("Agent {AgentName} created successfully in agents collection", agentName);
            return newAgent;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000) // Duplicate key error
        {
            _logger.LogInformation("Agent {AgentName} already exists, retrieving existing agent", agentName);
            
            // Agent already exists, get the existing one
            var existingAgent = await GetByNameInternalAsync(agentName, tenant);
            if (existingAgent != null)
            {
                // Update OnboardingJson if provided
                if (!string.IsNullOrEmpty(onboardingJson) && existingAgent.OnboardingJson != onboardingJson)
                {
                    _logger.LogInformation("Updating OnboardingJson for existing agent {AgentName}", agentName);
                    
                    var filter = Builders<Agent>.Filter.And(
                        Builders<Agent>.Filter.Eq(x => x.Name, agentName),
                        Builders<Agent>.Filter.Eq(x => x.Tenant, tenant)
                    );
                    
                    var update = Builders<Agent>.Update.Set(x => x.OnboardingJson, onboardingJson);
                    await _agents.UpdateOneAsync(filter, update);
                    
                    // Update the local object to reflect the change
                    existingAgent.OnboardingJson = onboardingJson;
                }
                
                return existingAgent;
            }
            
            // This shouldn't happen, but if it does, throw the original exception
            _logger.LogError(ex, "Agent {AgentName} duplicate key error but could not retrieve existing agent", agentName);
            throw new InvalidOperationException($"Agent {agentName} creation failed due to duplicate key, but existing agent could not be retrieved", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while upserting tenant-scoped agent {AgentName}", agentName);
            throw;
        }
    }

    /// <summary>
    /// Converts AgentTemplate to Agent for backward compatibility.
    /// </summary>
    private Agent ConvertTemplateToAgent(AgentTemplate template)
    {
        return new Agent
        {
            Id = template.Id,
            Name = template.Name,
            Tenant = null, // System-scoped
            SystemScoped = true,
            CreatedBy = template.CreatedBy,
            CreatedAt = template.CreatedAt,
            OwnerAccess = template.OwnerAccess,
            ReadAccess = template.ReadAccess,
            WriteAccess = template.WriteAccess,
            OnboardingJson = template.OnboardingJson,
            AgentTemplateId = template.Id, // Reference to the template
            Metadata = template.Metadata != null ? new Dictionary<string, object>(template.Metadata) : null
        };
    }

    private bool HasSystemAccess(string? agentTenantId)
    {
        // System admin has access to everything
        if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            return true;

        // Tenant admin has access to everything in their tenant
        if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
        {
            return !string.IsNullOrEmpty(agentTenantId) &&
                   _tenantContext.TenantId.Equals(agentTenantId, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private bool CheckPermissions(Agent agent, PermissionLevel requiredLevel)
    {
        if (HasSystemAccess(agent.Tenant))
        {
            return true;
        }

        return agent?.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, requiredLevel) ?? false;
    }

    public async Task<bool> ExistsAsync(string name, string tenant)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var agent = await _agents.Find(x => x.Name == name && x.Tenant == tenant && x.SystemScoped == false).FirstOrDefaultAsync();
            return agent != null;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CheckAgentExists");
    }

    public async Task<bool> AddReportingTargetAsync(string agentId, ReportingTo reportingTarget)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var agent = await _agents.Find(x => x.Id == agentId).FirstOrDefaultAsync();
            if (agent == null)
            {
                _logger.LogWarning("Agent with ID {AgentId} not found", agentId);
                return false;
            }

            agent.AddReportingTarget(reportingTarget);
            var result = await _agents.ReplaceOneAsync(x => x.Id == agentId, agent);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "AddReportingTarget");
    }

    public async Task<bool> RemoveReportingTargetAsync(string agentId, string targetId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var agent = await _agents.Find(x => x.Id == agentId).FirstOrDefaultAsync();
            if (agent == null)
            {
                _logger.LogWarning("Agent with ID {AgentId} not found", agentId);
                return false;
            }

            agent.RemoveReportingTarget(targetId);
            var result = await _agents.ReplaceOneAsync(x => x.Id == agentId, agent);
            return result.ModifiedCount > 0;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "RemoveReportingTarget");
    }

    // Legacy methods for backward compatibility
    [Obsolete("Use AddReportingTargetAsync instead")]
    public async Task<bool> AddReportingUserAsync(string agentId, string userId)
    {
        var reportingTarget = new ReportingTo 
        { 
            Id = userId, 
            Attributes = new Dictionary<string, string> { { "type", "user" } },
            AddedAt = DateTime.UtcNow
        };
        return await AddReportingTargetAsync(agentId, reportingTarget);
    }

    [Obsolete("Use RemoveReportingTargetAsync instead")]
    public async Task<bool> RemoveReportingUserAsync(string agentId, string userId)
    {
        return await RemoveReportingTargetAsync(agentId, userId);
    }
} 