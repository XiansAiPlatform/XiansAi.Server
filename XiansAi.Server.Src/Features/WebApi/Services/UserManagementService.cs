using Shared.Auth;
using Shared.Utils.Services;
using System.Text.Json.Serialization;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;

namespace XiansAi.Server.Features.WebApi.Services;

public class UserDto
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;
}

public interface IUserManagementService
{
    Task<ServiceResult<bool>> LockUserAsync(string userId, string reason);
    Task<ServiceResult<bool>> UnlockUserAsync(string userId);
    Task<ServiceResult<bool>> IsUserLockedOutAsync(string userId);
    Task<ServiceResult<User>> GetUserAsync(string userId);
    Task<ServiceResult<bool>> CreateNewUser(UserDto user);
}

public class UserManagementService : IUserManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<UserManagementService> _logger;

    public UserManagementService(
        IUserRepository userRepository,
        ITenantContext tenantContext,
        ILogger<UserManagementService> logger)
    {
        _userRepository = userRepository;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<ServiceResult<bool>> LockUserAsync(string userId, string reason)
    {
        try
        {
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) &&
                !_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
            {
                return ServiceResult<bool>.Forbidden("Only system or tenant admins can lock users");
            }

            var success = await _userRepository.LockUserAsync(userId, reason, _tenantContext.LoggedInUser);
            if (!success)
            {
                return ServiceResult<bool>.NotFound("User not found");
            }

            _logger.LogInformation("User {UserId} locked by {AdminUserId}", userId, _tenantContext.LoggedInUser);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error locking user {UserId}", userId);
            return ServiceResult<bool>.InternalServerError("An error occurred while locking the user");
        }
    }

    public async Task<ServiceResult<bool>> UnlockUserAsync(string userId)
    {
        try
        {
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) &&
                !_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
            {
                return ServiceResult<bool>.Forbidden("Only system or tenant admins can unlock users");
            }

            var success = await _userRepository.UnlockUserAsync(userId);
            if (!success)
            {
                return ServiceResult<bool>.NotFound("User not found");
            }

            _logger.LogInformation("User {UserId} unlocked by {AdminUserId}", userId, _tenantContext.LoggedInUser);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlocking user {UserId}", userId);
            return ServiceResult<bool>.InternalServerError("An error occurred while unlocking the user");
        }
    }

    public async Task<ServiceResult<bool>> IsUserLockedOutAsync(string userId)
    {
        try
        {
            var isLocked = await _userRepository.IsLockedOutAsync(userId);
            return ServiceResult<bool>.Success(isLocked);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking lock status for user {UserId}", userId);
            return ServiceResult<bool>.InternalServerError("An error occurred while checking user lock status");
        }
    }

    public async Task<ServiceResult<User>> GetUserAsync(string userId)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
            {
                return ServiceResult<User>.NotFound("User not found");
            }
            return ServiceResult<User>.Success(user);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user {UserId}", userId);
            return ServiceResult<User>.InternalServerError("An error occurred while retrieving the user");
        }
    }

    public async Task<ServiceResult<bool>> CreateNewUser(UserDto userDto)
    {
        try
        {
            var existingUser = await _userRepository.GetByUserIdAsync(userDto.UserId);
            if (existingUser != null)
            {
                return ServiceResult<bool>.Conflict("User already exists");
            }
            // to assign first user as SysAdmin
            var anyUser = await _userRepository.GetAnyUserAsync();

            var newUser = new User
            {
                UserId = userDto.UserId,
                Email = userDto.Email,
                Name = userDto.Name,
                IsSysAdmin = anyUser == null, 
                IsLockedOut = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TenantRoles = new List<TenantRole>()
            };
            var success = await _userRepository.CreateAsync(newUser);
            if (!success)
            {
                return ServiceResult<bool>.InternalServerError("Failed to create new user");
            }
            _logger.LogInformation("New user created: {UserId}", userDto.UserId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new user {UserId}", userDto.UserId);
            return ServiceResult<bool>.InternalServerError("An error occurred while creating the new user");
        }
    }
}