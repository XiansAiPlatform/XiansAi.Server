using MongoDB.Driver;
using Shared.Data.Models;
using Shared.Data;

namespace Features.WebApi.Repositories;

public interface IAgentPermissionRepository
{
    Task<Permission?> GetAgentPermissionsAsync(string agentName);
    Task<bool> UpdateAgentPermissionsAsync(string agentName, Permission permissions);
    Task<bool> AddUserToAgentAsync(string agentName, string userId, PermissionLevel permissionLevel);
    Task<bool> RemoveUserFromAgentAsync(string agentName, string userId);
    Task<bool> UpdateUserPermissionAsync(string agentName, string userId, PermissionLevel newPermissionLevel);
}

public class AgentPermissionRepository : IAgentPermissionRepository
{
    private readonly IMongoCollection<FlowDefinition> _definitions;
    private readonly ILogger<AgentPermissionRepository> _logger;

    public AgentPermissionRepository(IDatabaseService databaseService, ILogger<AgentPermissionRepository> logger)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _definitions = database.GetCollection<FlowDefinition>("flow_definitions");
        _logger = logger;
    }

    public async Task<Permission?> GetAgentPermissionsAsync(string agentName)
    {
        _logger.LogInformation("Getting permissions for agent: {AgentName}", agentName);
        
        // Get all flow definitions for the agent
        var definitions = await _definitions.Find(x => x.Agent == agentName).ToListAsync();
        
        if (!definitions.Any())
        {
            _logger.LogWarning("No flow definitions found for agent: {AgentName}", agentName);
            return null;
        }

        // Merge permissions from all flow definitions
        var mergedPermissions = new Permission();
        foreach (var definition in definitions)
        {
            mergedPermissions.OwnerAccess = mergedPermissions.OwnerAccess.Union(definition.Permissions.OwnerAccess).ToList();
            mergedPermissions.WriteAccess = mergedPermissions.WriteAccess.Union(definition.Permissions.WriteAccess).ToList();
            mergedPermissions.ReadAccess = mergedPermissions.ReadAccess.Union(definition.Permissions.ReadAccess).ToList();
        }

        return mergedPermissions;
    }

    public async Task<bool> UpdateAgentPermissionsAsync(string agentName, Permission permissions)
    {
        _logger.LogInformation("Updating permissions for agent: {AgentName}", agentName);
        
        // Get all flow definitions for the agent
        var definitions = await _definitions.Find(x => x.Agent == agentName).ToListAsync();
        
        if (!definitions.Any())
        {
            _logger.LogWarning("No flow definitions found for agent: {AgentName}", agentName);
            return false;
        }

        // Ensure users are only in one permission level
        var cleanedPermissions = CleanPermissionLevels(permissions);

        // Update permissions for all flow definitions
        var updateResult = await _definitions.UpdateManyAsync(
            x => x.Agent == agentName,
            Builders<FlowDefinition>.Update.Set(x => x.Permissions, cleanedPermissions)
        );

        return updateResult.ModifiedCount > 0;
    }

    public async Task<bool> AddUserToAgentAsync(string agentName, string userId, PermissionLevel permissionLevel)
    {
        _logger.LogInformation("Adding user {UserId} to agent {AgentName} with permission level {PermissionLevel}", 
            userId, agentName, permissionLevel);
        
        // Get all flow definitions for the agent
        var definitions = await _definitions.Find(x => x.Agent == agentName).ToListAsync();
        
        if (!definitions.Any())
        {
            _logger.LogWarning("No flow definitions found for agent: {AgentName}", agentName);
            return false;
        }

        var updateBuilder = Builders<FlowDefinition>.Update;
        var filter = Builders<FlowDefinition>.Filter.Eq(x => x.Agent, agentName);

        // First remove user from all permission levels
        var removeUpdate = updateBuilder
            .Pull(x => x.Permissions.OwnerAccess, userId)
            .Pull(x => x.Permissions.WriteAccess, userId)
            .Pull(x => x.Permissions.ReadAccess, userId);

        await _definitions.UpdateManyAsync(filter, removeUpdate);

        // Then add user to the appropriate level
        switch (permissionLevel)
        {
            case PermissionLevel.Owner:
                var ownerUpdate = updateBuilder.AddToSet(x => x.Permissions.OwnerAccess, userId);
                await _definitions.UpdateManyAsync(filter, ownerUpdate);
                break;
            case PermissionLevel.Write:
                var writeUpdate = updateBuilder.AddToSet(x => x.Permissions.WriteAccess, userId);
                await _definitions.UpdateManyAsync(filter, writeUpdate);
                break;
            case PermissionLevel.Read:
                var readUpdate = updateBuilder.AddToSet(x => x.Permissions.ReadAccess, userId);
                await _definitions.UpdateManyAsync(filter, readUpdate);
                break;
        }

        return true;
    }

    public async Task<bool> RemoveUserFromAgentAsync(string agentName, string userId)
    {
        _logger.LogInformation("Removing user {UserId} from agent {AgentName}", userId, agentName);
        
        // Get all flow definitions for the agent
        var definitions = await _definitions.Find(x => x.Agent == agentName).ToListAsync();
        
        if (!definitions.Any())
        {
            _logger.LogWarning("No flow definitions found for agent: {AgentName}", agentName);
            return false;
        }

        var updateBuilder = Builders<FlowDefinition>.Update;
        var filter = Builders<FlowDefinition>.Filter.Eq(x => x.Agent, agentName);

        // Remove user from all permission levels
        var update = updateBuilder
            .Pull(x => x.Permissions.OwnerAccess, userId)
            .Pull(x => x.Permissions.WriteAccess, userId)
            .Pull(x => x.Permissions.ReadAccess, userId);

        var result = await _definitions.UpdateManyAsync(filter, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateUserPermissionAsync(string agentName, string userId, PermissionLevel newPermissionLevel)
    {
        _logger.LogInformation("Updating permission for user {UserId} in agent {AgentName} to {PermissionLevel}", 
            userId, agentName, newPermissionLevel);
        
        // First remove the user from all permission levels
        await RemoveUserFromAgentAsync(agentName, userId);
        
        // Then add them with the new permission level
        return await AddUserToAgentAsync(agentName, userId, newPermissionLevel);
    }

    private Permission CleanPermissionLevels(Permission permissions)
    {
        var cleanedPermissions = new Permission();
        
        // Process owner access first (highest level)
        foreach (var userId in permissions.OwnerAccess)
        {
            cleanedPermissions.OwnerAccess.Add(userId);
        }

        // Process write access (remove users who are already owners)
        foreach (var userId in permissions.WriteAccess)
        {
            if (!cleanedPermissions.OwnerAccess.Contains(userId))
            {
                cleanedPermissions.WriteAccess.Add(userId);
            }
        }

        // Process read access (remove users who are already owners or writers)
        foreach (var userId in permissions.ReadAccess)
        {
            if (!cleanedPermissions.OwnerAccess.Contains(userId) && 
                !cleanedPermissions.WriteAccess.Contains(userId))
            {
                cleanedPermissions.ReadAccess.Add(userId);
            }
        }

        return cleanedPermissions;
    }
} 