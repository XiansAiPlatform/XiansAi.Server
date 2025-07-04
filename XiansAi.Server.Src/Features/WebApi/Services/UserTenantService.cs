using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using Shared.Utils.Services;
using Shared.Auth;

namespace XiansAi.Server.Features.WebApi.Services;

public class UserTenantDto
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}

public interface IUserTenantService
{
    Task<ServiceResult<List<string>>> GetTenantsForCurrentUser();
    Task<ServiceResult<List<string>>> GetTenantsForUser(string userId);
    Task<ServiceResult<bool>> AddTenantToUser(string userId, string tenantId);
    Task<ServiceResult<bool>> RemoveTenantFromUser(string userId, string tenantId);
}

public class UserTenantService : IUserTenantService
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<UserTenantService> _logger;
    private readonly ITenantContext _tenantContext;

    public UserTenantService(IUserRepository userRepository, 
        ILogger<UserTenantService> logger, 
        ITenantContext tenantContext,
        ITenantRepository tenantRepository)
    {
        _userRepository = userRepository;
        _logger = logger;
        _tenantRepository = tenantRepository;
        _tenantContext = tenantContext;
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
                    Roles = new List<string>()
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
}