using Features.WebApi.Models;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Utils.Services;
using System.IdentityModel.Tokens.Jwt;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using XiansAi.Server.Shared.Repositories;


namespace XiansAi.Server.Features.WebApi.Services;

public class UserTenantDto
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}

public class JoinTenantRequestDto
{
    public string TenantId { get; set; } = string.Empty;
}

public interface IUserTenantService
{
    Task<ServiceResult<List<string>>> GetCurrentUserTenants(string token);
    Task<ServiceResult<List<string>>> GetTenantsForCurrentUser();
    Task<ServiceResult<List<string>>> GetTenantsForUser(string userId);
    Task<ServiceResult<List<UserTenantDto>>> GetUnapprovedUsers(string? tenantId = null);
    Task<ServiceResult<bool>> AddTenantToUser(string userId, string tenantId);
    Task<ServiceResult<bool>> RemoveTenantFromUser(string userId, string tenantId);
    Task<ServiceResult<bool>> ApproveUser(string userId, string tenantId);
    Task<ServiceResult<bool>> RequestToJoinTenant(string tenantId);
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

    public UserTenantService(IUserRepository userRepository, 
        ILogger<UserTenantService> logger, 
        ITenantContext tenantContext,
        IAuthMgtConnect authMgtConnect,
        IConfiguration configuration,
        IUserManagementService userManagementService,
        ITenantRepository tenantRepository)
    {
        _userRepository = userRepository;
        _logger = logger;
        _tenantRepository = tenantRepository;
        _tenantContext = tenantContext;
        _authMgtConnect = authMgtConnect;
        _configuration = configuration;
        _userManagementService = userManagementService;
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
                return ServiceResult<List<string>>.Success(allTenants.Select(t => t.TenantId).ToList());
            }

            var tenants = await _userRepository.GetUserTenantsAsync(userId);

            //for backward compatibility, ensure existing users with their Tenants
            if(tenants.Count == 0)
            {
                tenants = await _authMgtConnect.GetUserTenants(userId);

                //check if tenant exist in DB collection
                foreach (var tenantId in tenants.ToList())
                {
                    var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
                    if (tenant == null)
                    {
                        _logger.LogInformation("Tenant {TenantId} not found in database, adding to DB collection", tenantId);
                        
                        var newTenant = new Tenant
                        {
                            Id = ObjectId.GenerateNewId().ToString(),
                            TenantId = tenantId,
                            Name = tenantId,
                            Domain = tenantId,
                            Enabled = true,
                            CreatedBy = "system",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await _tenantRepository.CreateAsync(newTenant);
                        _logger.LogInformation("New tenant created successfully with ID: {TenantId}", tenantId);
                    }
                }
            }

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
                return ServiceResult<List<string>>.Success(allTenants.Select(t => t.TenantId).ToList());
            }

            var tenants =  await _userRepository.GetUserTenantsAsync(userId);
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

    public async Task<ServiceResult<List<UserTenantDto>>> GetUnapprovedUsers(string? tenantId = null)
    {
        try
        {
            var validationResult = ValidateTenantAccess("get unapproved users", _tenantContext.TenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<List<UserTenantDto>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);
            var users = await _userRepository.GetUsersWithUnapprovedTenantAsync(tenantId);
            return ServiceResult<List<UserTenantDto>>.Success(
                users.SelectMany(user =>
                    (user.TenantRoles == null || user.TenantRoles.Count == 0)
                        ? new[] { new UserTenantDto { UserId = user.UserId, TenantId = string.Empty } }
                        : user.TenantRoles
                            .Where(tr => !tr.IsApproved && (tenantId != null ? tr.Tenant == tenantId : true))
                            .Select(tr => new UserTenantDto
                            {
                                UserId = user.UserId,
                                TenantId = tr.Tenant
                            })
                ).ToList()
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unapproved users");
            return ServiceResult<List<UserTenantDto>>.InternalServerError("Error getting unapproved users");
        }
    }

    public async Task<ServiceResult<bool>> ApproveUser(string userId, string tenantId)
    {
        try
        {
            var validationResult = ValidateTenantAccess("approve user", null);
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

                if (user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId) is TenantRole tenantRole)
                {
                    tenantRole.IsApproved = true;
                    tenantRole.Roles = tenantRole.Roles.Count > 0 ? tenantRole.Roles : new List<string> { SystemRoles.TenantUser };
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

    private async Task<UserDto> createUserFromToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadToken(token) as JwtSecurityToken;

        if (jsonToken == null)
        {
            _logger.LogWarning("Invalid JWT token format");
            throw new ArgumentException("Invalid token format", nameof(token));
        }

        // First try the standard 'sub' claim
        var userId = jsonToken.Claims.FirstOrDefault(c => c.Type == "sub")?.Value;
        if (!string.IsNullOrEmpty(userId))
        {
            _logger.LogDebug("Found user ID in 'sub' claim: {UserId}", userId);
        }
        else
        {
            // Fallback to preferred_username for Keycloak tokens missing 'sub' claim
            userId = jsonToken.Claims.FirstOrDefault(c => c.Type == "preferred_username")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                _logger.LogInformation("Using 'preferred_username' as user ID: {UserId}", userId);
            }
            else
            {
                // Try other common alternative claim names
                var alternativeClaimTypes = new[] { "user_id", "uid", "id", "email", "username" };
                foreach (var claimType in alternativeClaimTypes)
                {
                    userId = jsonToken.Claims.FirstOrDefault(c => c.Type == claimType)?.Value;
                    if (!string.IsNullOrEmpty(userId))
                    {
                        _logger.LogInformation("Found user ID in '{ClaimType}' claim: {UserId}", claimType, userId);
                        break;
                    }
                }
            }
        }

        if (string.IsNullOrWhiteSpace(userId))
        {
            _logger.LogWarning("User ID claim not found in token");
            throw new ArgumentException("User ID not found in token", nameof(token));
        }

        // var authProviderConfig = _configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ??
        //     new AuthProviderConfig();
        var name = jsonToken.Claims.FirstOrDefault(c => c.Type == "name")?.Value ?? string.Empty;
        var email = jsonToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? string.Empty;
        var newUser = new UserDto
        {
            UserId = userId,
            Email = email,
            Name = name,
        };
        var createdUser = await _userManagementService.CreateNewUser(newUser);

        if (!createdUser.IsSuccess && createdUser.StatusCode == StatusCode.Conflict)
        {
            _logger.LogInformation("User {UserId} already exists, returning existing user", userId);
            return new UserDto { UserId = userId, Email = email, Name = name };
        }

        return createdUser.IsSuccess
            ? newUser
            : throw new Exception($"Failed to create user {userId} from token: {createdUser.ErrorMessage}");
    }
}