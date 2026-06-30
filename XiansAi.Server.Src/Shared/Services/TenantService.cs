using MongoDB.Bson;
using Shared.Auth;
using Shared.Utils;
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
    public string? Domain { get; set; }
    public string? Description { get; set; }
    public string? CreatedBy { get; set; }
    public Logo? Logo { get; set; }
    public string? Theme { get; set; }
    public string? Timezone { get; set; }
    public bool UseSpecificTemporalNamespace { get; set; } = false;
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
    public bool? UseSpecificTemporalNamespace { get; set; }
}

public class UpdateTenantThemeRequest
{
    public string? Theme { get; set; }
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
    Task<ServiceResult<Tenant>> GetTenantByTenantId(string tenantId, CancellationToken cancellationToken = default, bool bypassCache = false);
    Task<ServiceResult<Tenant>> GetCurrentTenantInfo(CancellationToken cancellationToken = default);
    Task<ServiceResult<List<Tenant>>> GetAllTenants();
    Task<ServiceResult<List<string>>> GetTenantIdList();
    Task<ServiceResult<TenantCreatedResult>> CreateTenant(CreateTenantRequest request, string? createdBy = null);
    Task<ServiceResult<Tenant>> UpdateTenant(string id, UpdateTenantRequest request);
    Task<ServiceResult<Tenant>> UpdateTenantTheme(string id, string? theme);
    Task<ServiceResult<Tenant>> UpdateTenantLogo(string id, Logo? logo);
    Task<ServiceResult<bool>> DeleteTenant(string id);
}

public class TenantService : ITenantService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly ITenantCacheService _tenantCacheService;
    private readonly ILogger<TenantService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IRoleManagementService _roleManagementService;


    public TenantService(
        ITenantRepository tenantRepository,
        ITenantCacheService tenantCacheService,
        ILogger<TenantService> logger,
        ITenantContext tenantContext,
        IRoleManagementService roleManagementService)
    {
        _tenantRepository = tenantRepository ?? throw new ArgumentNullException(nameof(tenantRepository));
        _tenantCacheService = tenantCacheService ?? throw new ArgumentNullException(nameof(tenantCacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _roleManagementService = roleManagementService ?? throw new ArgumentNullException(nameof(roleManagementService));
    }

    private string EnsureTenantAccessOrThrow(string tenantId)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while ensuring tenant access: {Message}", LogSanitizer.Sanitize(ex.Message));
            throw;
        }

        // If system admin, return null (indicating unrestricted access)
        if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
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
        _logger.LogWarning("Attempted to access tenant `{Id}` but is restricted to SysAdmins and of tenant `{TenantId}`", LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(_tenantContext.TenantId));
        throw new UnauthorizedAccessException("Access denied: insufficient permissions");
    }

    private string SanitizeAndValidateId(string id)
    {
        try
        {
            return Tenant.SanitizeAndValidateId(id);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while sanitizing and validating ID: {Message}", LogSanitizer.Sanitize(ex.Message));
            throw;
        }
    }

    public async Task<ServiceResult<Tenant>> GetTenantById(string id)
    {
        try
        {
            id = SanitizeAndValidateId(id);
            var accessibleTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == id);
            if (accessibleTenantId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to tenant with ID {Id}", LogSanitizer.Sanitize(id));
                return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantRepository.GetByIdAsync(id);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found", LogSanitizer.Sanitize(id));
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by ID: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with ID {Id}", LogSanitizer.Sanitize(id));
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
                _logger.LogWarning("Tenant with domain {Domain} not found", LogSanitizer.Sanitize(domain));
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by domain: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with domain {Domain}", LogSanitizer.Sanitize(domain));
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> GetTenantByTenantId(string tenantId, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
            var accessibleTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == tenantId);
            if (accessibleTenantId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to tenant with tenant ID {TenantId}", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantCacheService.GetByTenantIdAsync(tenantId, cancellationToken, bypassCache);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant by tenant ID: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving tenant with tenant ID {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<Tenant>.InternalServerError("An error occurred while retrieving the tenant.");
        }
    }

    public async Task<ServiceResult<Tenant>> GetCurrentTenantInfo(CancellationToken cancellationToken = default)
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
                _logger.LogWarning("Unauthorized access attempt to tenant with tenant ID {TenantId}", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantCacheService.GetByTenantIdAsync(tenantId, cancellationToken);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            return ServiceResult<Tenant>.Success(tenant);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving current tenant info: {Message}", LogSanitizer.Sanitize(ex.Message));
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
            // Only SysAdmin can get all tenants
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                _logger.LogWarning("Unauthorized attempt to get all tenants by user {UserId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
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
            // Only SysAdmin can get tenant list
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                _logger.LogWarning("Unauthorized attempt to get tenant list by user {UserId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
                return ServiceResult<List<string>>.Forbidden("Access denied: Only system administrators can retrieve tenant list");
            }

            var tenantIds = await _tenantRepository.GetAllTenantIdsAsync();
            return ServiceResult<List<string>>.Success(tenantIds);
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
            _logger.LogInformation("CreateTenant request received - CreatedBy: {RequestCreatedBy}, Method param CreatedBy: {MethodCreatedBy}, LoggedInUser: {LoggedInUser}", 
                LogSanitizer.Sanitize(request.CreatedBy), LogSanitizer.Sanitize(createdBy), LogSanitizer.Sanitize(_tenantContext.LoggedInUser));

            var finalCreatedBy = request.CreatedBy ?? createdBy ?? _tenantContext.LoggedInUser ?? throw new InvalidOperationException("Logged in user is not set");
            _logger.LogInformation("Final CreatedBy value determined: {FinalCreatedBy}", LogSanitizer.Sanitize(finalCreatedBy));

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
                UseSpecificTemporalNamespace = request.UseSpecificTemporalNamespace,
                CreatedBy = finalCreatedBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _logger.LogInformation("Tenant object created with CreatedBy: {CreatedBy}", LogSanitizer.Sanitize(tenant.CreatedBy));
            
            var validatedTenant = tenant.SanitizeAndValidate();
            _logger.LogInformation("After sanitization, CreatedBy: {CreatedBy}", LogSanitizer.Sanitize(validatedTenant.CreatedBy));


            await _tenantRepository.CreateAsync(validatedTenant);
            _logger.LogInformation("Created new tenant with ID {Id} and CreatedBy: {CreatedBy}", LogSanitizer.Sanitize(validatedTenant.Id), LogSanitizer.Sanitize(validatedTenant.CreatedBy));

            var result = new TenantCreatedResult
            {
                Tenant = validatedTenant,
                Location = $"/api/tenants/{validatedTenant.Id}"
            };
            return ServiceResult<TenantCreatedResult>.Success(result);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while creating tenant: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<TenantCreatedResult>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000) // Duplicate key error
        {
            _logger.LogWarning("Duplicate tenant ID or domain: {TenantId}", LogSanitizer.Sanitize(request.TenantId));
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
                _logger.LogWarning("Tenant with ID {Id} not found for update", LogSanitizer.Sanitize(id));
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            EnsureTenantAccessOrThrow(existingTenant.TenantId);

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

            if (request.UseSpecificTemporalNamespace.HasValue)
                existingTenant.UseSpecificTemporalNamespace = request.UseSpecificTemporalNamespace.Value;

            return await PersistTenantUpdate(existingTenant, id);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while updating tenant: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating tenant with ID {Id}", LogSanitizer.Sanitize(id));
            return ServiceResult<Tenant>.InternalServerError("An error occurred while updating the tenant.");
        }
    }

    /// <summary>
    /// Sets (or clears) the tenant's theme. Passing a null or whitespace theme removes it.
    /// </summary>
    public async Task<ServiceResult<Tenant>> UpdateTenantTheme(string id, string? theme)
    {
        try
        {
            SanitizeAndValidateId(id);

            var existingTenant = await _tenantRepository.GetByIdAsync(id);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found for theme update", LogSanitizer.Sanitize(id));
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            EnsureTenantAccessOrThrow(existingTenant.TenantId);

            existingTenant.Theme = string.IsNullOrWhiteSpace(theme) ? null : theme;

            return await PersistTenantUpdate(existingTenant, id);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while updating tenant theme: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating theme for tenant with ID {Id}", LogSanitizer.Sanitize(id));
            return ServiceResult<Tenant>.InternalServerError("An error occurred while updating the tenant theme.");
        }
    }

    /// <summary>
    /// Sets (or clears) the tenant's logo. Passing a null logo removes it.
    /// </summary>
    public async Task<ServiceResult<Tenant>> UpdateTenantLogo(string id, Logo? logo)
    {
        try
        {
            SanitizeAndValidateId(id);

            var existingTenant = await _tenantRepository.GetByIdAsync(id);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found for logo update", LogSanitizer.Sanitize(id));
                return ServiceResult<Tenant>.NotFound("Tenant not found");
            }

            EnsureTenantAccessOrThrow(existingTenant.TenantId);

            existingTenant.Logo = logo;

            return await PersistTenantUpdate(existingTenant, id);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while updating tenant logo: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<Tenant>.Forbidden("Access denied: insufficient permissions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating logo for tenant with ID {Id}", LogSanitizer.Sanitize(id));
            return ServiceResult<Tenant>.InternalServerError("An error occurred while updating the tenant logo.");
        }
    }

    /// <summary>
    /// Validates, persists and cache-invalidates an already-mutated tenant entity.
    /// Shared by the tenant update operations so the save/validate/cache logic lives in one place.
    /// </summary>
    private async Task<ServiceResult<Tenant>> PersistTenantUpdate(Tenant existingTenant, string id)
    {
        existingTenant.UpdatedAt = DateTime.UtcNow;
        var validatedTenant = existingTenant.SanitizeAndValidate();

        var success = await _tenantRepository.UpdateAsync(id, validatedTenant);
        if (!success)
        {
            _logger.LogError("Failed to update tenant with ID {Id}", LogSanitizer.Sanitize(id));
            return ServiceResult<Tenant>.BadRequest("Failed to update tenant.");
        }

        if (!string.IsNullOrEmpty(existingTenant.TenantId))
            _tenantCacheService.InvalidateTenant(existingTenant.TenantId);
        else
            _logger.LogWarning("Skipping tenant cache invalidation: Tenant {Id} has null or empty TenantId", LogSanitizer.Sanitize(id));

        _logger.LogInformation("Updated tenant with ID {Id}", LogSanitizer.Sanitize(id));
        return ServiceResult<Tenant>.Success(validatedTenant);
    }

    public async Task<ServiceResult<bool>> DeleteTenant(string id)
    {
        try
        {
            id = SanitizeAndValidateId(id);
            var existingTenant = await _tenantRepository.GetByIdAsync(id);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with ID {Id} not found for deletion", LogSanitizer.Sanitize(id));
                return ServiceResult<bool>.NotFound("Tenant not found");
            }
            EnsureTenantAccessOrThrow(existingTenant.TenantId);

            var success = await _tenantRepository.DeleteAsync(id);
            if (success)
            {
                if (!string.IsNullOrEmpty(existingTenant.TenantId))
                    _tenantCacheService.InvalidateTenant(existingTenant.TenantId);
                else
                    _logger.LogWarning("Skipping tenant cache invalidation: Tenant {Id} has null or empty TenantId", LogSanitizer.Sanitize(id));
                _logger.LogInformation("Deleted tenant with ID {Id}", LogSanitizer.Sanitize(id));
                return ServiceResult<bool>.Success(true);
            }
            else
            {
                _logger.LogWarning("Tenant with ID {Id} not found for deletion", LogSanitizer.Sanitize(id));
                return ServiceResult<bool>.NotFound("Tenant not found");
            }
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while deleting tenant: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<bool>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<bool>.Forbidden("Access denied: insufficient permissions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting tenant with ID {Id}", LogSanitizer.Sanitize(id));
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the tenant.");
        }
    }

}