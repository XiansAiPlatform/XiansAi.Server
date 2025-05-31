using Shared.Data;
using Shared.Auth;
using Shared.Data.Models;

namespace Shared.Repositories;

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
    private readonly IAgentRepository _agentRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly ILogger<AgentPermissionRepository> _logger;
    private readonly ITenantContext _tenantContext;

    public AgentPermissionRepository(
        IAgentRepository agentRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        ILogger<AgentPermissionRepository> logger,
        ITenantContext tenantContext)
    {
        _agentRepository = agentRepository;
        _flowDefinitionRepository = flowDefinitionRepository;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<Permission?> GetAgentPermissionsAsync(string agentName)
    {
        _logger.LogInformation("Getting permissions for agent: {AgentName}", agentName);
        
        var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
        return agent?.Permissions;
    }

    public async Task<bool> UpdateAgentPermissionsAsync(string agentName, Permission permissions)
    {
        _logger.LogInformation("Updating permissions for agent: {AgentName}", agentName);
        
        // Use permission-aware method that checks if user has owner permission
        var agentUpdated = await _agentRepository.UpdatePermissionsAsync(
            agentName, 
            _tenantContext.TenantId, 
            CleanPermissionLevels(permissions),
            _tenantContext.LoggedInUser,
            _tenantContext.UserRoles);
        
        if (!agentUpdated)
        {
            _logger.LogWarning("Failed to update permissions for agent {AgentName} - either not found or insufficient permissions", agentName);
            return false;
        }

        return true;
    }

    public async Task<bool> AddUserToAgentAsync(string agentName, string userId, PermissionLevel permissionLevel)
    {
        _logger.LogInformation("Adding user {UserId} to agent {AgentName} with permission level {PermissionLevel}", 
            userId, agentName, permissionLevel);
        
        // Get the agent without permission check first, then check owner permission explicitly
        var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
        if (agent == null)
        {
            _logger.LogWarning("Agent not found: {AgentName}", agentName);
            return false;
        }

        // Check if user has owner permission (required to add users)
        if (!agent.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner))
        {
            _logger.LogWarning("User {UserId} attempted to add user to agent {AgentName} without owner permission", 
                _tenantContext.LoggedInUser, agentName);
            return false;
        }

        // Remove user from all permission levels first
        agent.OwnerAccess.Remove(userId);
        agent.WriteAccess.Remove(userId);
        agent.ReadAccess.Remove(userId);

        // Add user to the appropriate level
        switch (permissionLevel)
        {
            case PermissionLevel.Owner:
                agent.GrantOwnerAccess(userId);
                break;
            case PermissionLevel.Write:
                agent.GrantWriteAccess(userId);
                break;
            case PermissionLevel.Read:
                agent.GrantReadAccess(userId);
                break;
        }

        // Use the internal update method since we've already verified permissions
        var result = await _agentRepository.UpdateInternalAsync(agent.Id, agent);
        
        return result;
    }

    public async Task<bool> RemoveUserFromAgentAsync(string agentName, string userId)
    {
        _logger.LogInformation("Removing user {UserId} from agent {AgentName}", userId, agentName);
        
        // Get the agent without permission check first, then check owner permission explicitly
        var agent = await _agentRepository.GetByNameInternalAsync(agentName, _tenantContext.TenantId);
        if (agent == null)
        {
            _logger.LogWarning("Agent not found: {AgentName}", agentName);
            return false;
        }

        // Check if user has owner permission (required to remove users)
        if (!agent.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner))
        {
            _logger.LogWarning("User {UserId} attempted to remove user from agent {AgentName} without owner permission", 
                _tenantContext.LoggedInUser, agentName);
            return false;
        }

        // Track if any changes were made (for logging purposes)
        bool wasUserFound = agent.OwnerAccess.Contains(userId) || 
                           agent.WriteAccess.Contains(userId) || 
                           agent.ReadAccess.Contains(userId);

        // Remove user from all permission levels
        agent.RevokeOwnerAccess(userId);
        agent.RevokeWriteAccess(userId);
        agent.RevokeReadAccess(userId);

        // Use the internal update method since we've already verified permissions
        var result = await _agentRepository.UpdateInternalAsync(agent.Id, agent);
        
        // If the user wasn't found in any permission list, still consider it successful (idempotent operation)
        if (!wasUserFound)
        {
            _logger.LogInformation("User {UserId} was not found in any permission lists for agent {AgentName}, operation considered successful", userId, agentName);
            return true;
        }
        
        return result;
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

    private void SyncPermissionsToFlowDefinitions(string agentName, Permission permissions)
    {
        // Flow definitions no longer have permissions - they inherit from agent permissions
        // This method is no longer needed but kept for backward compatibility
        _logger.LogInformation("Permission sync to flow definitions is no longer needed for agent {AgentName}", agentName);
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