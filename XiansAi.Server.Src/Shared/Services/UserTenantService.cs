using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Utils.Services;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services;

public class UserTenantDto
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public bool IsApproved { get; set; } = false;
}

public class JoinTenantRequestDto
{
    public string TenantId { get; set; } = string.Empty;
}

public class AddUserToTenantDto
{
    public string Email { get; set; } = string.Empty;
}

public class CreateNewUserDto
{
    public required string Email { get; set; }
    public required string Name { get; set; }
    public List<string> TenantRoles { get; set; } = new();
}

public interface IUserTenantService
{
    Task<ServiceResult<List<string>>> GetCurrentUserTenants(string token);
    Task<ServiceResult<List<string>>> GetTenantsForCurrentUser();
    Task<ServiceResult<List<string>>> GetTenantsForUser(string userId);
    Task<ServiceResult<List<User>>> GetUnapprovedUsers();
    Task<ServiceResult<bool>> AddTenantToUser(string userId, string tenantId);
    Task<ServiceResult<bool>> RemoveTenantFromUser(string userId, string tenantId);
    Task<ServiceResult<bool>> ApproveUser(string userId, string tenantId, bool approve);
    Task<ServiceResult<bool>> RequestToJoinTenant(string tenantId);
    Task<ServiceResult<PagedUserResult>> GetTenantUsers(UserFilter filter);
    Task<ServiceResult<bool>> UpdateTenantUser(EditUserDto user);
    Task<ServiceResult<bool>> AddTenantToUserIfExist(string email);
    Task<ServiceResult<User>> CreateNewUserInTenant(CreateNewUserDto dto, string tenantId);
}

public class UserTenantService : IUserTenantService
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<UserTenantService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthMgtConnect _authMgtConnect;
    private readonly IConfiguration _configuration;
    private readonly IUserManagementService _userManagementService;
    private readonly IJwtClaimsExtractor _jwtClaimsExtractor;

    public UserTenantService(IUserRepository userRepository,
        ILogger<UserTenantService> logger,
        ITenantContext tenantContext,
        IAuthMgtConnect authMgtConnect,
        IConfiguration configuration,
        IUserManagementService userManagementService,
        ITenantRepository tenantRepository,
        IJwtClaimsExtractor jwtClaimsExtractor)
    {
        _userRepository = userRepository;
        _logger = logger;
        _tenantRepository = tenantRepository;
        _tenantContext = tenantContext;
        _authMgtConnect = authMgtConnect;
        _configuration = configuration;
        _userManagementService = userManagementService;
        _jwtClaimsExtractor = jwtClaimsExtractor;
    }

    public async Task<ServiceResult<List<string>>> GetCurrentUserTenants(string token)
    {
        var userId = _tenantContext.LoggedInUser;
        if (string.IsNullOrEmpty(userId))
            return ServiceResult<List<string>>.Unauthorized("User not authenticated");

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Token is null or empty");
            return ServiceResult<List<string>>.Unauthorized("Token is required");
        }

        var user = await _userRepository.GetByUserIdAsync(userId);
        if (user == null)
        {
            // Ensure user exists in the system
            var userDto = await createUserFromToken(token);
            if (userDto == null)
            {
                _logger.LogError("Failed to create user from token {Token}", token);
                return ServiceResult<List<string>>.InternalServerError("Failed to create user from token");
            }
            _logger.LogInformation("User {UserId} created from token", userDto.UserId);
        }

        return await GetTenantsForCurrentUser();
    }

    public async Task<ServiceResult<List<string>>> GetTenantsForCurrentUser()
    {
        try
        {
            var userId = _tenantContext.LoggedInUser;
            if (string.IsNullOrEmpty(userId))
                return ServiceResult<List<string>>.Unauthorized("User not authenticated");

            var isSysAdmin = await _userRepository.IsSysAdmin(userId);

            if (isSysAdmin)
            {
                var allTenants = await _tenantRepository.GetAllAsync();
                // Filter out disabled tenants
                var enabledTenantIds = allTenants.Where(t => t.Enabled).Select(t => t.TenantId).ToList();
                return ServiceResult<List<string>>.Success(enabledTenantIds);
            }

            var tenants = await _userRepository.GetUserTenantsAsync(userId);

            return ServiceResult<List<string>>.Success(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenants for current user");
            return ServiceResult<List<string>>.InternalServerError("Error getting tenants for current user");
        }
    }

    public async Task<ServiceResult<List<string>>> GetTenantsForUser(string userId)
    {
        try
        {
            var validationResult = ValidateTenantAccess("get user tenant", null); // only sysadmin can access all user tenants
            if (!validationResult.IsSuccess)
                return ServiceResult<List<string>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            var isSysAdmin = await _userRepository.IsSysAdmin(userId);

            if (isSysAdmin)
            {
                var allTenants = await _tenantRepository.GetAllAsync();
                // Filter out disabled tenants
                var enabledTenantIds = allTenants.Where(t => t.Enabled).Select(t => t.TenantId).ToList();
                return ServiceResult<List<string>>.Success(enabledTenantIds);
            }

            var tenants = await _userRepository.GetUserTenantsAsync(userId);
            return ServiceResult<List<string>>.Success(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenants for user {UserId}", userId);
            return ServiceResult<List<string>>.InternalServerError("Error getting tenants for user");
        }
    }

    public async Task<ServiceResult<bool>> AddTenantToUser(string userId, string tenantId)
    {

        try
        {
            var validationResult = ValidateTenantAccess("add user tenant", _tenantContext.TenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");

            var tenantEntry = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId);

            if (tenantEntry == null)
            {
                tenantEntry = new TenantRole
                {
                    Tenant = tenantId,
                    Roles = new List<string>(),
                    IsApproved = false
                };
                user.TenantRoles.Add(tenantEntry);
            }

            var result = await _userRepository.UpdateAsync(userId, user);
            return result
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.Conflict("Tenant already assigned to user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding tenant {TenantId} to user {UserId}", tenantId, userId);
            return ServiceResult<bool>.InternalServerError("Error adding tenant to user");
        }
    }

    public async Task<ServiceResult<bool>> AddTenantToUserIfExist(string email)
    {

        try
        {
            var validationResult = ValidateTenantAccess("add user tenant", _tenantContext.TenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            var user = await _userRepository.GetByUserEmailAsync(email);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");

            var tenantEntry = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == _tenantContext.TenantId);

            if(tenantEntry != null && tenantEntry.IsApproved)
            {
                return ServiceResult<bool>.Conflict("Tenant already assigned to user");
            }

            if (tenantEntry == null)
            {
                tenantEntry = new TenantRole
                {
                    Tenant = _tenantContext.TenantId,
                    Roles = new List<string> { SystemRoles.TenantUser},
                    IsApproved = true
                };
                user.TenantRoles.Add(tenantEntry);
            }

            var result = await _userRepository.UpdateAsync(user.UserId, user);
            return ServiceResult<bool>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user with email user {email} to tenant {TenantId}", _tenantContext.TenantId, email);
            return ServiceResult<bool>.InternalServerError("Error adding tenant to user with email {email}, email");
        }
    }

    public async Task<ServiceResult<bool>> RemoveTenantFromUser(string userId, string tenantId)
    {
        try
        {
            var validationResult = ValidateTenantAccess("remove user tenant", _tenantContext.TenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");

            var removed = user.TenantRoles.RemoveAll(tr => tr.Tenant == tenantId) > 0;
            var result = await _userRepository.UpdateAsync(userId, user);
            return removed && result
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.NotFound("Tenant not assigned to user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tenant {TenantId} from user {UserId}", tenantId, userId);
            return ServiceResult<bool>.InternalServerError("Error removing tenant from user");
        }
    }

    public async Task<ServiceResult<List<User>>> GetUnapprovedUsers()
    {
        try
        {
            // Determine which tenant to query based on user role
            string? tenantIdToQuery = null;
            
            // System admin sees unapproved users for the current tenant (from X-Tenant-Id / organisation dropdown)
            if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                tenantIdToQuery = _tenantContext.TenantId;
            }
            // Tenant admin can only see unapproved users for their tenant
            else if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
            {
                var validationResult = ValidateTenantAccess("get unapproved users", _tenantContext.TenantId);
                if (!validationResult.IsSuccess)
                    return ServiceResult<List<User>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);
                    
                tenantIdToQuery = _tenantContext.TenantId;
            }
            else
            {
                _logger.LogWarning("User {UserId} attempted to get unapproved users without proper permissions",
                    _tenantContext.LoggedInUser);
                return ServiceResult<List<User>>.Forbidden("Insufficient permissions to view unapproved users");
            }

            if (string.IsNullOrEmpty(tenantIdToQuery))
            {
                _logger.LogWarning("GetUnapprovedUsers called without a current tenant (X-Tenant-Id required)");
                return ServiceResult<List<User>>.BadRequest("A tenant must be selected (X-Tenant-Id header is required).");
            }
            
            var users = await _userRepository.GetUsersWithUnapprovedTenantAsync(tenantIdToQuery);
            return ServiceResult<List<User>>.Success(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unapproved users");
            return ServiceResult<List<User>>.InternalServerError("Error getting unapproved users");
        }
    }

    public async Task<ServiceResult<bool>> ApproveUser(string userId, string tenantId, bool approve)
    {
        try
        {
            var validationResult = ValidateTenantAccess("approve user", tenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");
            if (user.TenantRoles.Any(tr => tr.Tenant == tenantId))
            {
                if (user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId)?.IsApproved == true)
                {
                    _logger.LogWarning("User {UserId} already approved for tenant {TenantId}", userId, tenantId);
                    return ServiceResult<bool>.Conflict("User already approved for this tenant");
                }

                if (approve && user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId) is TenantRole tenantRole)
                {
                    tenantRole.IsApproved = true;
                    tenantRole.Roles = tenantRole.Roles.Count > 0 ? tenantRole.Roles : new List<string> { SystemRoles.TenantUser };
                }
                else
                {
                    user.TenantRoles.RemoveAll(tr => tr.Tenant == tenantId);
                }
            }
            else
            {
                var tenantEntry = new TenantRole
                {
                    Tenant = tenantId,
                    Roles = new List<string> { SystemRoles.TenantUser }, // Default role on approval
                    IsApproved = true
                };
                user.TenantRoles.Add(tenantEntry);
            }

            var result = await _userRepository.UpdateAsync(userId, user);
            return result
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.InternalServerError("Failed to approve user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving user {UserId} for tenant {TenantId}", userId, tenantId);
            return ServiceResult<bool>.InternalServerError("Error approving user");
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

    public async Task<ServiceResult<bool>> RequestToJoinTenant(string tenantId)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(_tenantContext.LoggedInUser);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");
            if (user.TenantRoles.Any(tr => tr.Tenant == tenantId))
            {
                _logger.LogWarning("User {UserId} in tenant {TenantId}", _tenantContext.LoggedInUser, tenantId);
                return ServiceResult<bool>.Conflict("User already in this tenant");
            }
            var tenantEntry = new TenantRole
            {
                Tenant = tenantId,
                Roles = new List<string> { SystemRoles.TenantUser },
                IsApproved = false
            };
            user.TenantRoles.Add(tenantEntry);
            var result = await _userRepository.UpdateAsync(user.Id, user);
            return result
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.InternalServerError("Failed to approve user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving user {UserId} for tenant {TenantId}", _tenantContext.LoggedInUser, tenantId);
            return ServiceResult<bool>.InternalServerError("Error approving user");
        }
    }

    public async Task<ServiceResult<PagedUserResult>> GetTenantUsers(UserFilter filter)
    {
        var validationResult = ValidateTenantAccess("get tenant users", _tenantContext.TenantId);
        if (!validationResult.IsSuccess)
            return ServiceResult<PagedUserResult>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

        filter.Tenant = _tenantContext.TenantId;
        var users = await _userRepository.GetAllUsersByTenantAsync(filter);
        return ServiceResult<PagedUserResult>.Success(users);
    }

    public async Task<ServiceResult<bool>> UpdateTenantUser(EditUserDto user)
    {
        // Security: Validate tenant access
        var validationResult = ValidateTenantAccess("update tenant user", _tenantContext.TenantId);
        if (!validationResult.IsSuccess)
            return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

        var existingUser = await _userRepository.GetByUserIdAsync(user.UserId);
        if (existingUser == null)
        {
            return ServiceResult<bool>.NotFound("User not found");
        }

        // Security: Prevent modification of system admin status via tenant endpoint
        if (user.IsSysAdmin != existingUser.IsSysAdmin)
        {
            _logger.LogWarning("Attempt to modify system admin status for user {UserId} via tenant endpoint by {LoggedInUser}", 
                user.UserId, _tenantContext.LoggedInUser);
            return ServiceResult<bool>.Forbidden("Cannot modify system admin status via this endpoint");
        }

        // Security: Validate that only current tenant roles are being modified
        var rolesForOtherTenants = user.TenantRoles.Where(x => x.Tenant != _tenantContext.TenantId).ToList();
        if (rolesForOtherTenants.Any())
        {
            _logger.LogWarning("Attempt to modify roles for other tenants by user {LoggedInUser} in tenant {TenantId}", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);
            return ServiceResult<bool>.Forbidden("Can only modify roles for the current tenant");
        }

        // Update allowed user properties: Name, Email, Active status
        existingUser.Email = user.Email;
        existingUser.Name = user.Name;
        existingUser.IsLockedOut = !user.Active;

        if (existingUser.IsLockedOut)
        {
            existingUser.LockedOutBy = _tenantContext.LoggedInUser;
            existingUser.LockedOutReason = "Locked by admin";
        }
        else
        {
            // Clear lockout information when re-activating
            existingUser.LockedOutBy = null;
            existingUser.LockedOutReason = null;
        }

        // Update tenant roles for current tenant only
        var currentTenantRoles = existingUser.TenantRoles.Where(x => x.Tenant == _tenantContext.TenantId).ToList();
        var updatedRoles = user.TenantRoles
            .Where(x => x.Tenant == _tenantContext.TenantId)
            .Select(x => x.Roles).FirstOrDefault() ?? new List<string>();

        // Security: Prevent tenant admin from assigning system-wide roles
        var systemWideRoles = new[] { SystemRoles.SysAdmin };
        var hasSystemRoles = updatedRoles.Any(r => systemWideRoles.Contains(r));
        if (hasSystemRoles && !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            _logger.LogWarning("Attempt to assign system-wide roles by non-sysadmin user {LoggedInUser} in tenant {TenantId}", 
                _tenantContext.LoggedInUser, _tenantContext.TenantId);
            return ServiceResult<bool>.Forbidden("Cannot assign system-wide roles");
        }

        if (currentTenantRoles.Count > 0)
        {
            currentTenantRoles[0].Roles = updatedRoles;
        }
        else
        {
            existingUser.TenantRoles.Add(new TenantRole
            {
                Tenant = _tenantContext.TenantId,
                Roles = updatedRoles,
                IsApproved = true 
            });
        }

        await _userRepository.UpdateAsync(existingUser.UserId, existingUser);

        _logger.LogInformation("User {UserId} updated by {LoggedInUser} in tenant {TenantId}", 
            user.UserId, _tenantContext.LoggedInUser, _tenantContext.TenantId);

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<User>> CreateNewUserInTenant(CreateNewUserDto dto, string tenantId)
    {
        try
        {
            // Security: Validate tenant access
            var validationResult = ValidateTenantAccess("create user in tenant", tenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<User>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            // Validate input
            if (string.IsNullOrWhiteSpace(dto.Email))
                return ServiceResult<User>.BadRequest("Email is required and must be valid");

            if (string.IsNullOrWhiteSpace(dto.Name))
                return ServiceResult<User>.BadRequest("Name is required");

            if (dto.TenantRoles.Count == 0)
                return ServiceResult<User>.BadRequest("At least one tenant role is required");

            // Validate roles are from allowed list
            var allowedRoles = new[] { SystemRoles.TenantAdmin, SystemRoles.TenantUser, SystemRoles.TenantParticipant };
            var invalidRoles = dto.TenantRoles.Where(r => !allowedRoles.Contains(r)).ToList();
            if (invalidRoles.Any())
                return ServiceResult<User>.BadRequest($"Invalid roles: {string.Join(", ", invalidRoles)}");

            // Check if user with this email already exists
            var existingUser = await _userRepository.GetByUserEmailAsync(dto.Email);
            if (existingUser != null)
                return ServiceResult<User>.Conflict("A user with this email already exists in the system.");

            // Generate unique userId
            var userId = Guid.NewGuid().ToString();

            // Create new user
            var newUser = new User
            {
                UserId = userId,
                Email = dto.Email,
                Name = dto.Name,
                IsSysAdmin = false,
                IsLockedOut = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TenantRoles = new List<TenantRole>
                {
                    new TenantRole
                    {
                        Tenant = tenantId,
                        Roles = dto.TenantRoles,
                        IsApproved = true
                    }
                }
            };

            // Save to database
            var created = await _userRepository.CreateAsync(newUser);
            if (!created)
            {
                _logger.LogError("Failed to create user {Email} in database", dto.Email);
                return ServiceResult<User>.InternalServerError("Failed to create user");
            }

            _logger.LogInformation("User {UserId} ({Email}) created by {CreatedBy} in tenant {TenantId}",
                userId, dto.Email, _tenantContext.LoggedInUser, tenantId);

            // Retrieve the created user to return complete data
            var createdUser = await _userRepository.GetByUserIdAsync(userId);
            if (createdUser == null)
            {
                _logger.LogError("Created user {UserId} but failed to retrieve it", userId);
                return ServiceResult<User>.InternalServerError("User created but failed to retrieve");
            }

            return ServiceResult<User>.Success(createdUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new user in tenant {TenantId}", tenantId);
            return ServiceResult<User>.InternalServerError("An error occurred while creating the new user");
        }
    }

    /// <summary>
    /// Creates a user from a JWT token with proper validation using the centralized JWT utility
    /// SECURITY: Uses centralized JWT validation with JWKS before processing claims
    /// </summary>
    private async Task<UserDto> createUserFromToken(string token)
    {
        // Validate and extract user information using the centralized JWT utility
        var jwtResult = await _jwtClaimsExtractor.ValidateAndExtractClaimsAsync(token);
        if (!jwtResult.IsValid || string.IsNullOrEmpty(jwtResult.UserId))
        {
            _logger.LogWarning("JWT token validation failed in createUserFromToken: {Error}", 
                jwtResult.ErrorMessage);
            throw new ArgumentException(jwtResult.ErrorMessage ?? "Invalid or expired token", nameof(token));
        }

        var newUser = new UserDto
        {
            UserId = jwtResult.UserId,
            Email = jwtResult.Email ?? string.Empty,
            Name = jwtResult.Name ?? string.Empty,
        };

        var createdUser = await _userManagementService.CreateNewUser(newUser);

        if (!createdUser.IsSuccess && createdUser.StatusCode == StatusCode.Conflict)
        {
            _logger.LogInformation("User {UserId} already exists, returning existing user", jwtResult.UserId);
            return newUser;
        }

        return createdUser.IsSuccess
            ? newUser
            : throw new Exception($"Failed to create user {jwtResult.UserId} from token. Error: {createdUser.ErrorMessage}");
    }

}