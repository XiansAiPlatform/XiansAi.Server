using MongoDB.Bson;
using Shared.Auth;
using Shared.Utils.Services;
using Shared.Data.Models;
using System.ComponentModel.DataAnnotations;
using MongoDB.Driver;
using Shared.Repositories;
using Features.WebApi.Services;

namespace Shared.Services;

// Request DTOs
public class CreateTenantRequest
{
    public required string TenantId { get; set; }
    public required string Name { get; set; }
    public required string Domain { get; set; }
    public string? Description { get; set; }
    public Logo? Logo { get; set; }
    public string? Theme { get; set; }
    public string? Timezone { get; set; }
}

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public Logo? Logo { get; set; }
    public string? Theme { get; set; }
    public string? Timezone { get; set; }
    public bool? Enabled { get; set; }
}

public class TenantCreatedResult
{
    public Tenant Tenant { get; set; } = default!;
    public string Location { get; set; } = string.Empty;
}

public interface ITenantService
{
    Task<ServiceResult<Tenant>> GetTenantById(string id);
    Task<ServiceResult<Tenant>> GetTenantByDomain(string domain);
    Task<ServiceResult<Tenant>> GetTenantByTenantId(string tenantId);
    Task<ServiceResult<Tenant>> GetCurrentTenantInfo();
    Task<ServiceResult<List<Tenant>>> GetAllTenants();
    Task<ServiceResult<List<string>>> GetTenantIdList();
    Task<ServiceResult<TenantCreatedResult>> CreateTenant(CreateTenantRequest request, string? createdBy = null);
    Task<ServiceResult<Tenant>> UpdateTenant(string id, UpdateTenantRequest request);
    Task<ServiceResult<bool>> DeleteTenant(string id);
}

public class TenantService : ITenantService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<TenantService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IRoleManagementService _roleManagementService;
    private readonly IUserRepository _userRepository;


    public TenantService(
        ITenantRepository tenantRepository,
        ILogger<TenantService> logger,
        ITenantContext tenantContext,
        IRoleManagementService roleManagementService,
        IUserRepository userRepository)
    {
        _tenantRepository = tenantRepository ?? throw new ArgumentNullException(nameof(tenantRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _roleManagementService = roleManagementService ?? throw new ArgumentNullException(nameof(roleManagementService));
        _userRepository = userRepository ?? throw new ArgumentNullException(nameof(userRepository));
    }

    private async Task<string> EnsureTenantAccessOrThrowAsync(string tenantId)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while ensuring tenant access: {Message}", ex.Message);
            throw new Exception($"Validation failed: {ex.Message}");
        }

        // Check if user is SysAdmin - first check UserRoles, then database if needed
        bool isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
        if (!isSysAdmin && !string.IsNullOrEmpty(_tenantContext.LoggedInUser))
        {
            isSysAdmin = await _userRepository.IsSysAdmin(_tenantContext.LoggedInUser);
        }
        
        // If system admin, return tenantId (indicating unrestricted access)
        if (isSysAdmin)
        {
            return tenantId;
        }

        // If tenant admin and tenantId matches, return the tenant
        if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin) &&
            _tenantContext.TenantId == tenantId)
        {
            return tenantId;
        }

        // Otherwise, forbidden
        _logger.LogWarning("Attempted to access tenant `{Id}` but is restricted to SysAdmins and of tenant `{TenantId}`", tenantId, _tenantContext.TenantId);
        throw new Exception("Access denied: insufficient permissions");
    }

    private string SanitizeAndValidateId(string id)
    {
        try
        {
            return Tenant.SanitizeAndValidateId(id);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while ensuring tenant access: {Message}", ex.Message);
            throw new Exception($"Validation failed: {ex.Message}");
        }
    }

    public async Task<ServiceResult<Tenant>> GetTenantById(string id)
    {
        try
        {
            id = SanitizeAndValidateId(id);
            
            // Check if user is SysAdmin - if so, allow access to any tenant
            bool isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
            if (!isSysAdmin && !string.IsNullOrEmpty(_tenantContext.LoggedInUser))
            {
                isSysAdmin = await _userRepository.IsSysAdmin(_tenantContext.LoggedInUser);
            }
            
            // If not SysAdmin, check authorized tenant IDs
            if (!isSysAdmin)
            {
                var accessableTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == id);
                if (accessableTenantId == null)
                {
                    _logger.LogWarning("Unauthorized access attempt to tenant with ID {Id}", id);
                    return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
                }
            }

            var tenant = await _tenantRepository.GetByIdAsync(id);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", id);
                return ServiceResult<Tenant>.Forbidden("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by ID: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with ID {Id}", id);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> GetTenantByDomain(string domain)
    {
        try
        {
            domain = Tenant.SanitizeAndValidateDomain(domain);

            var tenant = await _tenantRepository.GetByDomainAsync(domain);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with domain {Domain} not found", domain);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by domain: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with domain {Domain}", domain);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> GetTenantByTenantId(string tenantId)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
            var accessableTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == tenantId);
            if (accessableTenantId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to tenant with tenant ID {TenantId}", tenantId);
                return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", tenantId);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by domain: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with tenant ID {TenantId}", tenantId);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> GetCurrentTenantInfo()
    {
        try
        {
            if(_tenantContext.TenantId == null)
            {
                _logger.LogWarning("Tenant ID is not set in tenant context");
                return ServiceResult<Tenant>.BadRequest("Tenant ID is not set in the context");
            }

            var tenantId = Tenant.SanitizeAndValidateTenantId(_tenantContext.TenantId);

            var accessibleTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == tenantId);
            if (accessibleTenantId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to tenant with tenant ID {TenantId}", tenantId);
                return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", tenantId);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by domain: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving current tenant info");
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<List<Tenant>>> GetAllTenants()
    {
        try
        {
            // Check if user is SysAdmin - first check UserRoles, then database if needed
            bool isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
            
            // If not in UserRoles (e.g., AdminApi calls without X-Tenant-Id), check database directly
            if (!isSysAdmin && !string.IsNullOrEmpty(_tenantContext.LoggedInUser))
            {
                isSysAdmin = await _userRepository.IsSysAdmin(_tenantContext.LoggedInUser);
            }
            
            if (!isSysAdmin)
            {
                _logger.LogWarning("Unauthorized attempt to get all tenants by user {UserId}", _tenantContext.LoggedInUser);
                return ServiceResult<List<Tenant>>.Forbidden("Access denied: Only system administrators can retrieve all tenants");
            }

            var tenants = await _tenantRepository.GetAllAsync();
            return ServiceResult<List<Tenant>>.Success(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving all tenants");
            return ServiceResult<List<Tenant>>.InternalServerError("An error occurred while retrieving tenants.");
        }
    }

    public async Task<ServiceResult<List<string>>> GetTenantIdList()
    {
        try
        {
            // Check if user is SysAdmin - first check UserRoles, then database if needed
            bool isSysAdmin = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin);
            
            // If not in UserRoles (e.g., AdminApi calls without X-Tenant-Id), check database directly
            if (!isSysAdmin && !string.IsNullOrEmpty(_tenantContext.LoggedInUser))
            {
                isSysAdmin = await _userRepository.IsSysAdmin(_tenantContext.LoggedInUser);
            }
            
            if (!isSysAdmin)
            {
                _logger.LogWarning("Unauthorized attempt to get tenant list by user {UserId}", _tenantContext.LoggedInUser);
                return ServiceResult<List<string>>.Forbidden("Access denied: Only system administrators can retrieve tenant list");
            }

            var tenants = await _tenantRepository.GetAllAsync();
            return ServiceResult<List<string>>.Success(tenants.Select(t => t.TenantId).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant list");
            return ServiceResult<List<string>>.InternalServerError("An error occurred while retrieving tenants.");
        }
    }

    public async Task<ServiceResult<TenantCreatedResult>> CreateTenant(CreateTenantRequest request, string? createdBy = null)
    {
        try
        {
            var tenant = new Tenant
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = request.TenantId,
                Name = request.Name,
                Domain = request.Domain,
                Description = request.Description,
                Logo = request.Logo,
                Theme = request.Theme,
                Timezone = request.Timezone,
                Enabled = false,
                CreatedBy = createdBy ?? _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set"),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            var validatedTenant = tenant.SanitizeAndValidate();


            await _tenantRepository.CreateAsync(validatedTenant);
            _logger.LogInformation("Created new tenant with ID {Id}", tenant.Id);

            var result = new TenantCreatedResult
            {
                Tenant = tenant,
                Location = $"/api/tenants/{tenant.Id}"
            };
            return ServiceResult<TenantCreatedResult>.Success(result);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while creating tenant: {Message}", ex.Message);
            return ServiceResult<TenantCreatedResult>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000) // Duplicate key error
        {
            _logger.LogWarning("Duplicate tenant ID or domain: {TenantId}", request.TenantId);
            return ServiceResult<TenantCreatedResult>.BadRequest("A tenant with this ID or domain already exists.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return ServiceResult<TenantCreatedResult>.InternalServerError("An error occurred while creating the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> UpdateTenant(string id, UpdateTenantRequest request)
    {
        try
        {
            SanitizeAndValidateId(id);

            var existingTenant = await _tenantRepository.GetByIdAsync(id);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found for update", id);
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            await EnsureTenantAccessOrThrowAsync(existingTenant.TenantId);

            // Update only the properties that are provided in the request
            if (request.Name != null)
                existingTenant.Name = request.Name;

            if (request.Domain != null)
                existingTenant.Domain = request.Domain;

            if (request.Description != null)
                existingTenant.Description = request.Description;

            if (request.Logo != null)
                existingTenant.Logo = request.Logo;

            if (request.Theme != null)
                existingTenant.Theme = request.Theme;

            if (request.Enabled.HasValue)
                existingTenant.Enabled = request.Enabled.Value;

            if (request.Timezone != null)
                existingTenant.Timezone = request.Timezone;

            existingTenant.UpdatedAt = DateTime.UtcNow;
            var validatedTenant = existingTenant.SanitizeAndValidate();

            var success = await _tenantRepository.UpdateAsync(id, validatedTenant);
            if (success)
            {
                _logger.LogInformation("Updated tenant with ID {Id}", id);
                return ServiceResult<Tenant>.Success(validatedTenant);
            }
            else
            {
                _logger.LogError("Failed to update tenant with ID {Id}", id);
                return ServiceResult<Tenant>.BadRequest("Failed to update tenant.");
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while updating tenant: {Message}", ex.Message);
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant with ID {Id}", id);
            return ServiceResult<Tenant>.InternalServerError("An error occurred while updating the tenant.");
        }
    }

    public async Task<ServiceResult<bool>> DeleteTenant(string id)
    {
        try
        {
            await EnsureTenantAccessOrThrowAsync(id);
            id = SanitizeAndValidateId(id);

            var success = await _tenantRepository.DeleteAsync(id);
            if (success)
            {
                _logger.LogInformation("Deleted tenant with ID {Id}", id);
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Tenant with ID {Id} not found for deletion", id);
                return ServiceResult<bool>.NotFound("Tenant not found");
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while deleting tenant: {Message}", ex.Message);
            return ServiceResult<bool>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant with ID {Id}", id);
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the tenant.");
        }
    }

}