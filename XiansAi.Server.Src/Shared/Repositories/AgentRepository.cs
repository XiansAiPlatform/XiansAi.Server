using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Data;
using Shared.Data.Models;

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
    Task<List<Agent>> GetAgentsWithPermissionAsync(string userId, string tenant);
    Task CreateAsync(Agent agent);
    Task<bool> UpdateAsync(string id, Agent agent, string userId, string[] userRoles);
    Task<bool> UpdatePermissionsAsync(string name, string tenant, Permission permissions, string userId, string[] userRoles);
    Task<bool> DeleteAsync(string id, string userId, string[] userRoles);
    
    // Internal methods without permission checking (for system use)
    Task<Agent?> GetByNameInternalAsync(string name, string tenant);
    Task<bool> UpdateInternalAsync(string id, Agent agent);
    Task<Agent> UpsertAgentAsync(string agentName, string tenant, string createdBy);

    Task<List<AgentWithDefinitions>> GetAgentsWithDefinitionsAsync(string userId, string tenant, DateTime? startTime, DateTime? endTime, bool basicDataOnly = false);

}

public class AgentRepository : IAgentRepository
{
    private readonly IMongoCollection<Agent> _agents;
    private readonly IMongoCollection<FlowDefinition> _definitions;
    private readonly ILogger<AgentRepository> _logger;
    private readonly ITenantContext _tenantContext;

    public AgentRepository(IDatabaseService databaseService, ILogger<AgentRepository> logger, ITenantContext tenantContext)
    {
        var database = databaseService.GetDatabaseAsync().Result;
        _agents = database.GetCollection<Agent>("agents");
        _logger = logger;
        _definitions = database.GetCollection<FlowDefinition>("flow_definitions");
        _tenantContext = tenantContext; 
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

    public async Task<Agent?> GetByNameInternalAsync(string name, string tenant)
    {
        return await _agents.Find(x => x.Name == name && x.Tenant == tenant).FirstOrDefaultAsync();
    }

    public async Task<List<Agent>> GetAgentsWithPermissionAsync(string userId, string tenant)
    {
        var filterBuilder = Builders<Agent>.Filter;
        var filters = new List<FilterDefinition<Agent>>();

        // System admin has access to everything in all tenants
        if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            // For system admin, we only filter by tenant if provided
            return await _agents.Find(filterBuilder.Eq(x => x.Tenant, tenant)).ToListAsync();
        }

        // Tenant admin has access to everything in their tenant
        if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
        {
            // For tenant admin, we ensure they can only access their tenant
            if (_tenantContext.TenantId.Equals(tenant, StringComparison.OrdinalIgnoreCase))
            {
                return await _agents.Find(filterBuilder.Eq(x => x.Tenant, tenant)).ToListAsync();
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
        var tenantFilter = filterBuilder.Eq(x => x.Tenant, tenant);

        // Combine filters
        var finalFilter = filterBuilder.And(permissionFilter, tenantFilter);

        return await _agents.Find(finalFilter).ToListAsync();
    }

    public async Task CreateAsync(Agent agent)
    {
        agent.CreatedAt = DateTime.UtcNow;
        await _agents.InsertOneAsync(agent);
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
        var result = await _agents.ReplaceOneAsync(x => x.Id == id, agent);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdatePermissionsAsync(string name, string tenant, Permission permissions, string userId, string[] userRoles)
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

        var filter = Builders<Agent>.Filter.And(
            Builders<Agent>.Filter.Eq(x => x.Name, name),
            Builders<Agent>.Filter.Eq(x => x.Tenant, tenant)
        );
        
        var update = Builders<Agent>.Update.Set(x => x.OwnerAccess, permissions.OwnerAccess)
            .Set(x => x.ReadAccess, permissions.ReadAccess)
            .Set(x => x.WriteAccess, permissions.WriteAccess);
        var result = await _agents.UpdateOneAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> DeleteAsync(string id, string userId, string[] userRoles)
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

        var result = await _agents.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
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
        var filters = new List<FilterDefinition<FlowDefinition>>
        {
            filterBuilder.In(x => x.Agent, agentNames)
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
                    .Include(x => x.CreatedAt)
                    .Include(x => x.UpdatedAt))
                .ToListAsync();
        }
        else
        {
            allDefinitions = await findFluent.ToListAsync();
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

    public async Task<Agent> UpsertAgentAsync(string agentName, string tenant, string createdBy)
    {
        _logger.LogInformation("Upserting agent: {AgentName} for user: {UserId} in tenant: {Tenant}", agentName, createdBy, tenant);
        
        // Create new agent object
        var newAgent = new Agent
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = agentName,
            Tenant = tenant,
            CreatedBy = createdBy,
            CreatedAt = DateTime.UtcNow,
            OwnerAccess = new List<string>(),
            ReadAccess = new List<string>(),
            WriteAccess = new List<string>()
        };
        newAgent.GrantOwnerAccess(createdBy);

        try
        {
            // Try to insert the new agent - will fail if duplicate exists due to unique index
            await _agents.InsertOneAsync(newAgent);
            _logger.LogInformation("Agent {AgentName} created successfully", agentName);
            return newAgent;
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000) // Duplicate key error
        {
            _logger.LogInformation("Agent {AgentName} already exists, retrieving existing agent", agentName);
            
            // Agent already exists, get the existing one
            var existingAgent = await GetByNameInternalAsync(agentName, tenant);
            if (existingAgent != null)
            {
                return existingAgent;
            }
            
            // This shouldn't happen, but if it does, throw the original exception
            _logger.LogError(ex, "Agent {AgentName} duplicate key error but could not retrieve existing agent", agentName);
            throw new InvalidOperationException($"Agent {agentName} creation failed due to duplicate key, but existing agent could not be retrieved", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while upserting agent {AgentName}", agentName);
            throw;
        }
    }

    private bool HasSystemAccess(string agentTenantId)
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
} 