using Shared.Auth;
using Shared.Utils.Services;
using System.Text.Json.Serialization;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;

namespace XiansAi.Server.Features.WebApi.Services
{
    public class RolesDto
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty;
        [JsonPropertyName("roles")]
        public List<string> Roles { get; set; } = new();
    }

    public interface IRoleManagementService
    {
        Task<ServiceResult<bool>> AssignRolesToUserAsync(string userId, string tenantId, List<string> roles);
        Task<ServiceResult<bool>> RemoveRoleFromUserAsync(string userId, string tenantId, List<string> roles);
        Task<ServiceResult<List<string>>> GetUserRolesAsync(string userId, string tenantId);
        Task<ServiceResult<List<UserRole>>> GetUsersByRoleAsync(string role, string tenantId);
        Task<ServiceResult<bool>> PromoteToTenantAdminAsync(string userId, string tenantId);
    }

    public class RoleManagementService : IRoleManagementService
    {
        private readonly IUserRoleRepository _userRoleRepository;
        private readonly IRoleCacheService _roleCacheService;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<RoleManagementService> _logger;

        public RoleManagementService(
            IUserRoleRepository userRoleRepository,
            IRoleCacheService roleCacheService,
            ITenantContext tenantContext,
            ILogger<RoleManagementService> logger)
        {
            _userRoleRepository = userRoleRepository;
            _roleCacheService = roleCacheService;
            _tenantContext = tenantContext;
            _logger = logger;
        }

        public async Task<ServiceResult<bool>> AssignRolesToUserAsync(string userId, string tenantId, List<string> roles)
        {
            var validationResult = ValidateTenantAccess(tenantId, "assign roles");
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                // Check if this is a bootstrap scenario (no admins exist)
                if (roles.Contains(SystemRoles.SysAdmin))
                {
                    var existingAdmins = await _userRoleRepository.GetUsersByRoleAsync(SystemRoles.SysAdmin, tenantId);
                    bool isBootstrapping = !existingAdmins.Any();

                    // If not bootstrapping, verify current user has permission
                    if (!isBootstrapping &&
                        !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                    {
                        return ServiceResult<bool>.Forbidden("Only system admins can create other system admins");
                    }
                }

                // Regular tenant admin check
                if (roles.Contains(SystemRoles.TenantAdmin) &&
                    !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) &&
                    !_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    return ServiceResult<bool>.Forbidden("Insufficient permissions to assign tenant admin role");
                }

                var result = await _userRoleRepository.AssignRolesAsync(
                    userId,
                    tenantId,
                    roles,
                    _tenantContext.LoggedInUser ?? "system");  // Use "system" for bootstrap case

                _roleCacheService.InvalidateUserRoles(userId, tenantId);
                return ServiceResult<bool>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning roles to user {UserId}", userId);
                return ServiceResult<bool>.InternalServerError("Error assigning roles");
            }
        }

        public async Task<ServiceResult<bool>> RemoveRoleFromUserAsync(string userId, string tenantId, List<string> roles)
        {
            var validationResult = ValidateTenantAccess(tenantId, "remove roles");
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                foreach (var role in roles)
                {
                    var success = await _userRoleRepository.RemoveRoleAsync(userId, tenantId, role);
                    if (!success)
                    {
                        return ServiceResult<bool>.BadRequest($"Failed to remove role: {role}.");
                    }
                }

                _roleCacheService.InvalidateUserRoles(userId, tenantId);
                return ServiceResult<bool>.Success(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing roles from user.");
                return ServiceResult<bool>.InternalServerError("An error occurred while removing roles.");
            }
        }

        public async Task<ServiceResult<List<string>>> GetUserRolesAsync(string userId, string tenantId)
        {
            var validationResult = ValidateTenantAccess(tenantId, "access user roles");
            if (!validationResult.IsSuccess)
                return ServiceResult<List<string>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                var roles = await _roleCacheService.GetUserRolesAsync(userId, tenantId);
                return ServiceResult<List<string>>.Success(roles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user roles.");
                return ServiceResult<List<string>>.InternalServerError("An error occurred while retrieving user roles.");
            }
        }

        public async Task<ServiceResult<List<UserRole>>> GetUsersByRoleAsync(string role, string tenantId)
        {
            var validationResult = ValidateTenantAccess(tenantId, "access user roles");
            if (!validationResult.IsSuccess)
                return ServiceResult<List<UserRole>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                var users = await _userRoleRepository.GetUsersByRoleAsync(role, tenantId);
                return ServiceResult<List<UserRole>>.Success(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users by role.");
                return ServiceResult<List<UserRole>>.InternalServerError("An error occurred while retrieving users by role.");
            }
        }

        public async Task<ServiceResult<bool>> PromoteToTenantAdminAsync(string userId, string tenantId)
        {
            var validationResult = ValidateTenantAccess(tenantId, "promote to tenant admin");
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                var roles = new List<string> { SystemRoles.TenantAdmin };
                var success = await _userRoleRepository.AssignRolesAsync(userId, tenantId, roles, _tenantContext.LoggedInUser);
                if (success)
                {
                    _roleCacheService.InvalidateUserRoles(userId, tenantId);
                    return ServiceResult<bool>.Success(true);
                }
                return ServiceResult<bool>.BadRequest("Failed to promote user to Tenant Admin.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error promoting user to Tenant Admin.");
                return ServiceResult<bool>.InternalServerError("An error occurred while promoting user to Tenant Admin.");
            }
        }

        private ServiceResult ValidateTenantAccess(string tenantId, string operation)
        {
            // System admin has access to everything
            if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                return ServiceResult.Success();
            }

            // Tenant admin can only access their own tenant
            if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
            {
                if (!_tenantContext.TenantId.Equals(tenantId, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Tenant admin {UserId} attempted to {Operation} in different tenant {TenantId}",
                        _tenantContext.LoggedInUser, operation, tenantId);
                    return ServiceResult.Failure("Tenant admins can only access their own tenant", StatusCode.Forbidden);
                }
                return ServiceResult.Success();
            }

            // Regular users need to be at least tenant admin
            _logger.LogWarning("User {UserId} attempted to {Operation} without proper permissions",
                _tenantContext.LoggedInUser, operation);
            return ServiceResult.Failure("Insufficient permissions to manage roles", StatusCode.Forbidden);
        }
    }
}
