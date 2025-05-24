using System.Text.Json.Serialization;
using Shared.Data;
using Shared.Auth;
using Shared.Utils.Services;
using Features.WebApi.Repositories;
using Shared.Repositories;

namespace Shared.Services;

public class UserPermissionDto
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("permissionLevel")]
    public string PermissionLevel { get; set; } = string.Empty;
}

public class PermissionDto
{
    [JsonPropertyName("ownerAccess")]
    public List<string> OwnerAccess { get; set; } = new();

    [JsonPropertyName("readAccess")]
    public List<string> ReadAccess { get; set; } = new();

    [JsonPropertyName("writeAccess")]
    public List<string> WriteAccess { get; set; } = new();
}

public interface IPermissionsService
{
    Task<ServiceResult<PermissionDto>> GetPermissions(string agentName);
    Task<ServiceResult<bool>> UpdatePermissions(string agentName, PermissionDto permissions);
    Task<ServiceResult<bool>> AddUser(string agentName, string userId, string permissionLevel);
    Task<ServiceResult<bool>> RemoveUser(string agentName, string userId);
    Task<ServiceResult<bool>> UpdateUserPermission(string agentName, string userId, string newPermissionLevel);
    Task<ServiceResult<bool>> HasReadPermission(string agentName);
    Task<ServiceResult<bool>> HasWritePermission(string agentName);
    Task<ServiceResult<bool>> HasOwnerPermission(string agentName);
}

public class PermissionsService : IPermissionsService
{
    private readonly IAgentPermissionRepository _permissionRepository;
    private readonly ILogger<PermissionsService> _logger;
    private readonly ITenantContext _tenantContext;

    public PermissionsService(IAgentPermissionRepository permissionRepository, ILogger<PermissionsService> logger, ITenantContext tenantContext)
    {
        _permissionRepository = permissionRepository;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    public async Task<ServiceResult<PermissionDto>> GetPermissions(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<PermissionDto>.BadRequest("Agent name is required");
        }

        _logger.LogInformation("Getting permissions for agent: {AgentName}", agentName);
        
        var permissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (permissions == null)
        {
            _logger.LogWarning("No permissions found for agent: {AgentName}", agentName);
            return ServiceResult<PermissionDto>.NotFound("No permissions found for this agent");
        }
        
        return ServiceResult<PermissionDto>.Success(new PermissionDto
        {
            OwnerAccess = permissions.OwnerAccess,
            ReadAccess = permissions.ReadAccess,
            WriteAccess = permissions.WriteAccess
        });
    }

    public async Task<ServiceResult<bool>> UpdatePermissions(string agentName, PermissionDto permissions)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<bool>.BadRequest("Agent name is required");
        }

        if (permissions == null)
        {
            _logger.LogWarning("Permissions object is null");
            return ServiceResult<bool>.BadRequest("Permissions object is required");
        }

        if (permissions.OwnerAccess == null || permissions.ReadAccess == null || permissions.WriteAccess == null)
        {
            _logger.LogWarning("Invalid permissions object structure");
            return ServiceResult<bool>.BadRequest("Permissions object must contain ownerAccess, readAccess, and writeAccess arrays");
        }

        _logger.LogInformation("Updating permissions for agent: {AgentName}", agentName);
        
        // Check if user has owner permission
        var currentPermissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (currentPermissions != null && !currentPermissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner))
        {
            _logger.LogWarning("User {UserId} attempted to update permissions for agent {AgentName} without owner permission", 
                _tenantContext.LoggedInUser, agentName);
            return ServiceResult<bool>.Forbidden("You must have owner permission to update permissions");
        }

        var permission = new Permission
        {
            OwnerAccess = permissions.OwnerAccess,
            ReadAccess = permissions.ReadAccess,
            WriteAccess = permissions.WriteAccess
        };

        var result = await _permissionRepository.UpdateAgentPermissionsAsync(agentName, permission);
        return ServiceResult<bool>.Success(result);
    }

    public async Task<ServiceResult<bool>> AddUser(string agentName, string userId, string permissionLevel)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<bool>.BadRequest("Agent name is required");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Invalid user ID provided");
            return ServiceResult<bool>.BadRequest("User ID is required");
        }

        if (string.IsNullOrWhiteSpace(permissionLevel))
        {
            _logger.LogWarning("Invalid permission level provided");
            return ServiceResult<bool>.BadRequest("Permission level is required");
        }

        _logger.LogInformation("Adding user {UserId} to agent {AgentName} with permission level {PermissionLevel}", 
            userId, agentName, permissionLevel);
        
        // Check if user has owner permission
        var currentPermissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (currentPermissions != null && !currentPermissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner))
        {
            _logger.LogWarning("User {UserId} attempted to add user to agent {AgentName} without owner permission", 
                _tenantContext.LoggedInUser, agentName);
            return ServiceResult<bool>.Forbidden("You must have owner permission to add users");
        }

        var level = NormalizePermissionLevel(permissionLevel);
        if (level == null)
        {
            _logger.LogWarning("Invalid permission level: {PermissionLevel}", permissionLevel);
            return ServiceResult<bool>.BadRequest("Invalid permission level. Must be one of: Owner, Write, Read");
        }

        var result = await _permissionRepository.AddUserToAgentAsync(agentName, userId, level.Value);
        return ServiceResult<bool>.Success(result);
    }

    public async Task<ServiceResult<bool>> RemoveUser(string agentName, string userId)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<bool>.BadRequest("Agent name is required");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Invalid user ID provided");
            return ServiceResult<bool>.BadRequest("User ID is required");
        }

        _logger.LogInformation("Removing user {UserId} from agent {AgentName}", userId, agentName);
        
        // Check if user has owner permission
        var currentPermissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (currentPermissions != null && !currentPermissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner))
        {
            _logger.LogWarning("User {UserId} attempted to remove user from agent {AgentName} without owner permission", 
                _tenantContext.LoggedInUser, agentName);
            return ServiceResult<bool>.Forbidden("You must have owner permission to remove users");
        }

        var result = await _permissionRepository.RemoveUserFromAgentAsync(agentName, userId);
        return ServiceResult<bool>.Success(result);
    }

    public async Task<ServiceResult<bool>> UpdateUserPermission(string agentName, string userId, string newPermissionLevel)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<bool>.BadRequest("Agent name is required");
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("Invalid user ID provided");
            return ServiceResult<bool>.BadRequest("User ID is required");
        }

        if (string.IsNullOrWhiteSpace(newPermissionLevel))
        {
            _logger.LogWarning("Invalid permission level provided");
            return ServiceResult<bool>.BadRequest("Permission level is required");
        }

        _logger.LogInformation("Updating permission for user {UserId} in agent {AgentName} to {PermissionLevel}", 
            userId, agentName, newPermissionLevel);
        
        // Check if user has owner permission
        var currentPermissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (currentPermissions != null && !currentPermissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner))
        {
            _logger.LogWarning("User {UserId} attempted to update user permission for agent {AgentName} without owner permission", 
                _tenantContext.LoggedInUser, agentName);
            return ServiceResult<bool>.Forbidden("You must have owner permission to update user permissions");
        }

        var level = NormalizePermissionLevel(newPermissionLevel);
        if (level == null)
        {
            _logger.LogWarning("Invalid permission level: {PermissionLevel}", newPermissionLevel);
            return ServiceResult<bool>.BadRequest("Invalid permission level. Must be one of: Owner, Write, Read");
        }

        var result = await _permissionRepository.UpdateUserPermissionAsync(agentName, userId, level.Value);
        return ServiceResult<bool>.Success(result);
    }

    public async Task<ServiceResult<bool>> HasReadPermission(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<bool>.BadRequest("Agent name is required");
        }

        _logger.LogInformation("Checking read permission for agent: {AgentName}", agentName);
        
        var permissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (permissions == null)
        {
            _logger.LogWarning("No permissions found for agent: {AgentName}", agentName);
            return ServiceResult<bool>.NotFound("Agent not found");
        }

        var hasReadPermission = permissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Read);
        return ServiceResult<bool>.Success(hasReadPermission);
    }

    public async Task<ServiceResult<bool>> HasWritePermission(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<bool>.BadRequest("Agent name is required");
        }

        _logger.LogInformation("Checking write permission for agent: {AgentName}", agentName);
        
        var permissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (permissions == null)
        {
            _logger.LogWarning("No permissions found for agent: {AgentName}", agentName);
            return ServiceResult<bool>.NotFound("Agent not found");
        }

        var hasWritePermission = permissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Write);
        return ServiceResult<bool>.Success(hasWritePermission);
    }

    public async Task<ServiceResult<bool>> HasOwnerPermission(string agentName)
    {
        if (string.IsNullOrWhiteSpace(agentName))
        {
            _logger.LogWarning("Invalid agent name provided");
            return ServiceResult<bool>.BadRequest("Agent name is required");
        }

        _logger.LogInformation("Checking owner permission for agent: {AgentName}", agentName);
        
        var permissions = await _permissionRepository.GetAgentPermissionsAsync(agentName);
        if (permissions == null)
        {
            _logger.LogWarning("No permissions found for agent: {AgentName}", agentName);
            return ServiceResult<bool>.NotFound("Agent not found");
        }

        var hasOwnerPermission = permissions.HasPermission(_tenantContext.LoggedInUser, _tenantContext.UserRoles, PermissionLevel.Owner);
        return ServiceResult<bool>.Success(hasOwnerPermission);
    }

    private PermissionLevel? NormalizePermissionLevel(string permissionLevel)
    {
        // Remove any "Access" suffix and normalize case
        var normalized = permissionLevel.Replace("Access", "", StringComparison.OrdinalIgnoreCase)
                                      .Trim();

        if (Enum.TryParse<PermissionLevel>(normalized, true, out var level))
        {
            return level;
        }

        return null;
    }
} 