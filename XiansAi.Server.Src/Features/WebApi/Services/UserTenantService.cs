using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using Shared.Utils.Services;

namespace XiansAi.Server.Features.WebApi.Services;

public class UserTenantDto
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
}

public interface IUserTenantService
{
    Task<ServiceResult<List<string>>> GetTenantsForUser(string userId);
    Task<ServiceResult<bool>> AddTenantToUser(string userId, string tenantId);
    Task<ServiceResult<bool>> RemoveTenantFromUser(string userId, string tenantId);
}

public class UserTenantService : IUserTenantService
{
    private readonly IUserTenantRepository _repo;
    private readonly ILogger<UserTenantService> _logger;

    public UserTenantService(IUserTenantRepository repo, ILogger<UserTenantService> logger)
    {
        _repo = repo;
        _logger = logger;
    }

    public async Task<ServiceResult<List<string>>> GetTenantsForUser(string userId)
    {
        try
        {
            var tenants = await _repo.GetTenantsForUserAsync(userId);
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
            var result = await _repo.AddTenantToUserAsync(userId, tenantId);
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
            var result = await _repo.RemoveTenantFromUserAsync(userId, tenantId);
            return result
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.NotFound("Tenant not assigned to user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing tenant {TenantId} from user {UserId}", tenantId, userId);
            return ServiceResult<bool>.InternalServerError("Error removing tenant from user");
        }
    }
}