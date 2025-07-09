using Features.WebApi.Auth.Providers;
using Shared.Auth;
using Shared.Utils.Services;
using System.Text.Json.Serialization;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;

namespace XiansAi.Server.Features.WebApi.Services
{
    public class RoleDto
    {
        [JsonPropertyName("userId")]
        public string UserId { get; set; } = string.Empty;

        [JsonPropertyName("tenantId")]
        public string TenantId { get; set; } = string.Empty;
        [JsonPropertyName("role")]
        public string Role { get; set; } = string.Empty;
    }

    public class UserInfoDto
    {
        [JsonPropertyName("userId")]
        public required string UserId { get; set; }
        [JsonPropertyName("nickname")]
        public required string Nickname { get; set; }
    }

    public interface IRoleManagementService
    {
        Task<ServiceResult<bool>> AssignBootstrapSysAdminRolesToUserAsync();
        Task<ServiceResult<bool>> AssignSysAdminRolesToUserAsync(string userId);
        Task<ServiceResult<bool>> AssignTenantRoletoUserAsync(RoleDto roleDto, bool skipValidation = false);
        Task<ServiceResult<bool>> RemoveSysAdminRolesToUserAsync(string userId);
        Task<ServiceResult<bool>> RemoveRoleFromUserAsync(RoleDto roleDto);
        Task<ServiceResult<List<string>>> GetUserRolesAsync(string userId, string tenantId);
        Task<ServiceResult<List<UserInfoDto>>> GetSystemAdminsAsync();
        Task<ServiceResult<List<UserInfoDto>>> GetUsersByRoleAsync(string role, string tenantId);
        Task<ServiceResult<List<string>>> GetCurrentUserRolesAsync();

        Task<ServiceResult<List<UserInfoDto>>> GetUsersInfoByRoleAsync(string role, string tenantId);
    }

    public class RoleManagementService : IRoleManagementService
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleCacheService _roleCacheService;
        private readonly ITenantContext _tenantContext;
        private readonly ILogger<RoleManagementService> _logger;
        private readonly IAuthProviderFactory _authProviderFactory;

        public RoleManagementService(
            IUserRepository userRepository,
            IRoleCacheService roleCacheService,
            ITenantContext tenantContext,
            ILogger<RoleManagementService> logger,
            IAuthProviderFactory authProviderFactory)
        {
            _userRepository = userRepository;
            _roleCacheService = roleCacheService;
            _tenantContext = tenantContext;
            _logger = logger;
            _authProviderFactory = authProviderFactory;
        }

        public async Task<ServiceResult<bool>> AssignBootstrapSysAdminRolesToUserAsync()
        {
            try
            {
                var existingAdmins = await _userRepository.GetSystemAdminAsync();
                bool isBootstrapping = !existingAdmins.Any();

                // If not bootstrapping, verify current user has permission
                if (!isBootstrapping &&
                    !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                {
                    return ServiceResult<bool>.Forbidden("Only system admins can create other system admins");
                }

                var user = await _userRepository.GetByUserIdAsync(_tenantContext.LoggedInUser);
                if (user == null)
                    return ServiceResult<bool>.NotFound("User not found");

                user.IsSysAdmin = true;
                var result = await _userRepository.UpdateAsync(_tenantContext.LoggedInUser, user);
                _roleCacheService.InvalidateUserRoles(_tenantContext.LoggedInUser, _tenantContext.TenantId);
                return ServiceResult<bool>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning bootstrap sysadmin roles to user {UserId}", _tenantContext.LoggedInUser);
                return ServiceResult<bool>.InternalServerError("An error occurred while assigning bootstrap sysadmin roles.");
            }
        }

        public async Task<ServiceResult<bool>> AssignSysAdminRolesToUserAsync(string userId)
        {
            var validationResult = ValidateTenantAccess("assign roles", null);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                {
                    return ServiceResult<bool>.Forbidden("Insufficient permissions to assign system admin role");
                }

                var user = await _userRepository.GetByUserIdAsync(userId);
                if (user == null)
                    return ServiceResult<bool>.NotFound("User not found");

                user.IsSysAdmin = true;

                var result = await _userRepository.UpdateAsync(userId, user);
                _roleCacheService.InvalidateUserRoles(userId, _tenantContext.TenantId);

                return ServiceResult<bool>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning roles to user {UserId}", userId);
                return ServiceResult<bool>.InternalServerError("Error assigning roles");
            }
        }

        public async Task<ServiceResult<bool>> RemoveSysAdminRolesToUserAsync(string userId)
        {
            var validationResult = ValidateTenantAccess("remove roles", null);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);
            try
            {
                if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
                {
                    return ServiceResult<bool>.Forbidden("Insufficient permissions to remove system admin role");
                }
                var user = await _userRepository.GetByUserIdAsync(userId);
                if (user == null)
                    return ServiceResult<bool>.NotFound("User not found");
                user.IsSysAdmin = false;
                var result = await _userRepository.UpdateAsync(userId, user);
                _roleCacheService.InvalidateUserRoles(userId, _tenantContext.TenantId);
                return ServiceResult<bool>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing roles from user {UserId}", userId);
                return ServiceResult<bool>.InternalServerError("Error removing roles");
            }
        }

        public async Task<ServiceResult<bool>> RemoveRoleFromUserAsync(RoleDto roleDto)
        {
            var validationResult = ValidateTenantAccess("remove roles", roleDto.TenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                var user = await _userRepository.GetByUserIdAsync(roleDto.UserId);
                if (user == null)
                    return ServiceResult<bool>.NotFound("User not found");

                var tenantEntry = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == roleDto.TenantId);
                var roleToRemove = CheckRole(roleDto.Role);

                if (tenantEntry != null)
                {
                    tenantEntry.Roles.RemoveAll(r => r == roleToRemove);

                    if (tenantEntry.Roles.Count == 0)
                    {
                        user.TenantRoles.Remove(tenantEntry);
                    }
                }

                var result = await _userRepository.UpdateAsync(roleDto.UserId, user);

                _roleCacheService.InvalidateUserRoles(roleDto.UserId, roleDto.TenantId);
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
            var validationResult = ValidateTenantAccess("access user roles", tenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<List<string>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                var user = await _userRepository.GetByUserIdAsync(userId);
                if (user == null)
                    return ServiceResult<List<string>>.NotFound("User not found");

                var tenantEntry = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId);

                if (tenantEntry != null)
                    return ServiceResult<List<string>>.Success(tenantEntry.Roles);

                return ServiceResult<List<string>>.Success(new List<string>());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user roles.");
                return ServiceResult<List<string>>.InternalServerError("An error occurred while retrieving user roles.");
            }
        }

        public async Task<ServiceResult<List<UserInfoDto>>> GetSystemAdminsAsync()
        {
            try
            {
                var admins = await _userRepository.GetSystemAdminAsync();
                return ServiceResult<List<UserInfoDto>>.Success(admins.Select(x => new UserInfoDto
                {
                        UserId = x.UserId,
                        Nickname = x.Name
                }).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving system admins.");
                return ServiceResult<List<UserInfoDto>>.InternalServerError("An error occurred while retrieving system admins.");
            }
        }

        public async Task<ServiceResult<List<UserInfoDto>>> GetUsersByRoleAsync(string role, string tenantId)
        {
            var validationResult = ValidateTenantAccess("access user roles", tenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<List<UserInfoDto>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                var users = await _userRepository.GetUsersByRoleAsync(role, tenantId);
                return ServiceResult<List<UserInfoDto>>.Success(users.Select(x => new UserInfoDto
                {
                        UserId = x.UserId,
                        Nickname = x.Name
                }).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users by role.");
                return ServiceResult<List<UserInfoDto>>.InternalServerError("An error occurred while retrieving users by role.");
            }
        }

        public async Task<ServiceResult<List<UserInfoDto>>> GetUsersInfoByRoleAsync(string role, string tenantId)
        {
            var validationResult = ValidateTenantAccess("access user roles", tenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<List<UserInfoDto>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            try
            {
                var users = await _userRepository.GetUsersByRoleAsync(role, tenantId);
                if (users == null || !users.Any())
                {
                    return ServiceResult<List<UserInfoDto>>.Success(new List<UserInfoDto>());
                }

                var userInfoList = new List<UserInfoDto>();
                
                foreach (var user in users)
                {
                    userInfoList.Add(new UserInfoDto
                    {
                        UserId = user.UserId,
                        Nickname = user.Name
                    });
                }
                return ServiceResult<List<UserInfoDto>>.Success(userInfoList);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving users by role.");
                return ServiceResult<List<UserInfoDto>>.InternalServerError("An error occurred while retrieving users by role.");
            }
        }

        public async Task<ServiceResult<bool>> AssignTenantRoletoUserAsync(RoleDto roleDto, bool skipValidation = false)
        {
            if (!skipValidation)
            {
                var validationResult = ValidateTenantAccess("assign tenant to user", roleDto.TenantId);
                if (!validationResult.IsSuccess)
                    return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);
            }

            try
            {
                var user = await _userRepository.GetByUserIdAsync(roleDto.UserId);
                if (user == null)
                    return ServiceResult<bool>.NotFound("User not found");

                var tenantEntry = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == roleDto.TenantId);
                var roleToAssign = CheckRole(roleDto.Role);

                if (tenantEntry == null)
                {
                    tenantEntry = new TenantRole
                    {
                        Tenant = roleDto.TenantId,
                        Roles = new List<string> { roleToAssign },
                        IsApproved = true
                    };
                    user.TenantRoles.Add(tenantEntry);
                }
                else if (tenantEntry.Roles.Contains(roleToAssign))
                {
                    _logger.LogWarning("Role {Role} already assigned to user {UserId} in tenant {TenantId}",
                        roleToAssign, roleDto.UserId, roleDto.TenantId);
                    return ServiceResult<bool>.Success(true); // Role already exists, no action needed
                }
                else if (!tenantEntry.Roles.Contains(roleToAssign))
                {
                    tenantEntry.Roles.Add(roleToAssign);
                }


                var result = await _userRepository.UpdateAsync(roleDto.UserId, user);
                return ServiceResult<bool>.Success(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning user role.");
                var errorMessage = "An error occurred while assigning user." + ex.Message;
                return ServiceResult<bool>.InternalServerError(errorMessage);
            }
        }

        public async Task<ServiceResult<List<string>>> GetCurrentUserRolesAsync()
        {
            try
            {
                if (string.IsNullOrEmpty(_tenantContext.LoggedInUser))
                {
                    _logger.LogWarning("No logged in user found in context");
                    return ServiceResult<List<string>>.Unauthorized("No logged in user found");
                }

                if (string.IsNullOrEmpty(_tenantContext.TenantId))
                {
                    _logger.LogWarning("No tenant ID found in context for user {UserId}",
                        _tenantContext.LoggedInUser);
                    return ServiceResult<List<string>>.BadRequest("No tenant ID found in context");
                }

                var user = await _userRepository.GetByUserIdAsync(_tenantContext.LoggedInUser);
                if (user == null)
                    return ServiceResult<List<string>>.Success(new List<string>());

                var roles = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == _tenantContext.TenantId)?.Roles;

                if (roles == null)
                {
                    roles = new List<string>();
                }

                if (user.IsSysAdmin)
                    roles.Add(SystemRoles.SysAdmin);

                return ServiceResult<List<string>>.Success(roles);

            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving current user roles for user {UserId} in tenant {TenantId}",
                    _tenantContext.LoggedInUser,
                    _tenantContext.TenantId);
                return ServiceResult<List<string>>.InternalServerError("An error occurred while retrieving current user roles");
            }
        }

        private ServiceResult ValidateTenantAccess(string operation, string? tenantId)
        {
            // System admin has access to everything
            if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                return ServiceResult.Success();
            }

            // Tenant admin can only access their own tenant
            if (tenantId != null && _tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
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

        private string CheckRole(string role)
        {
            if (string.IsNullOrEmpty(role))
                throw new ArgumentException("Role cannot be null or empty", nameof(role));
            
            if(role == SystemRoles.SysAdmin)
            {
                return SystemRoles.SysAdmin;
            }

            if (role == SystemRoles.TenantAdmin)
            {
                return SystemRoles.TenantAdmin;
            }

            if (role == SystemRoles.TenantUser)
            {
                return SystemRoles.TenantUser;
            }

            throw new Exception($"Invalid role: {role}");
        }
    }
}
