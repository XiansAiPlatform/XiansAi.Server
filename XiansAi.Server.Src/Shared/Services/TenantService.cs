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
    public List<TenantMetadata>? Metadata { get; set; }
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
    public List<TenantMetadata>? Metadata { get; set; }
}

public class UpdateTenantThemeRequest
{
    public string? Theme { get; set; }
}

public class UpsertTenantMetadataRequest
{
    public required string Value { get; set; }
    public MetadataType Type { get; set; } = MetadataType.PlainText;
}

public class TenantCreatedResult
{
    public Tenant Tenant { get; set; } = default!;
    public string Location { get; set; } = string.Empty;
}

/// <summary>
/// A single page of tenants together with pagination metadata.
/// Reuses <see cref="PaginationInfo"/> so the shape matches other paginated AdminApi responses.
/// </summary>
public class TenantListResult
{
    public List<Tenant> Tenants { get; set; } = new();
    public PaginationInfo Pagination { get; set; } = new();
}

public interface ITenantService
{
    Task<ServiceResult<Tenant>> GetTenantById(string id);
    Task<ServiceResult<Tenant>> GetTenantByDomain(string domain);
    Task<ServiceResult<Tenant>> GetTenantByTenantId(string tenantId, CancellationToken cancellationToken = default, bool bypassCache = false);
    Task<ServiceResult<List<TenantMetadata>>> GetTenantMetadata(string tenantId, CancellationToken cancellationToken = default, bool bypassCache = false);
    Task<ServiceResult<TenantMetadata>> GetTenantMetadataByKey(string tenantId, string key, CancellationToken cancellationToken = default, bool bypassCache = false);
    Task<ServiceResult<TenantMetadata>> UpsertTenantMetadata(string tenantId, string key, UpsertTenantMetadataRequest request);
    Task<ServiceResult<bool>> DeleteTenantMetadata(string tenantId, string key);
    Task<ServiceResult<Tenant>> GetCurrentTenantInfo(CancellationToken cancellationToken = default);
    Task<ServiceResult<List<Tenant>>> GetAllTenants();
    Task<ServiceResult<TenantListResult>> GetAllTenants(int? page, int? pageSize, string? search = null);
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
    private readonly IWebhookEventPublisher _webhookEventPublisher;
    private readonly ITenantMetadataProtector _metadataProtector;


    public TenantService(
        ITenantRepository tenantRepository,
        ITenantCacheService tenantCacheService,
        ILogger<TenantService> logger,
        ITenantContext tenantContext,
        IRoleManagementService roleManagementService,
        IWebhookEventPublisher webhookEventPublisher,
        ITenantMetadataProtector metadataProtector)
    {
        _tenantRepository = tenantRepository ?? throw new ArgumentNullException(nameof(tenantRepository));
        _tenantCacheService = tenantCacheService ?? throw new ArgumentNullException(nameof(tenantCacheService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _roleManagementService = roleManagementService ?? throw new ArgumentNullException(nameof(roleManagementService));
        _webhookEventPublisher = webhookEventPublisher ?? throw new ArgumentNullException(nameof(webhookEventPublisher));
        _metadataProtector = metadataProtector ?? throw new ArgumentNullException(nameof(metadataProtector));
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

    /// <summary>
    /// Returns the tenant's metadata with Secret values decrypted. This is the only
    /// place decrypted metadata is exposed; all other tenant reads return the stored
    /// (encrypted) form so secrets never travel with general tenant payloads.
    /// </summary>
    public async Task<ServiceResult<List<TenantMetadata>>> GetTenantMetadata(string tenantId, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
            var accessibleTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == tenantId);
            if (accessibleTenantId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to metadata of tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<List<TenantMetadata>>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantCacheService.GetByTenantIdAsync(tenantId, cancellationToken, bypassCache);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<List<TenantMetadata>>.NotFound("Tenant not found");
            }

            var metadata = _metadataProtector.Unprotect(tenant.Metadata, tenant.TenantId) ?? new List<TenantMetadata>();
            return ServiceResult<List<TenantMetadata>>.Success(metadata);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant metadata: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<List<TenantMetadata>>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving metadata for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<List<TenantMetadata>>.InternalServerError("An error occurred while retrieving the tenant metadata.");
        }
    }

    /// <summary>
    /// Returns a single tenant metadata entry by key (case-insensitive), with the value
    /// decrypted when the entry is of type Secret. Only the requested entry is decrypted.
    /// </summary>
    public async Task<ServiceResult<TenantMetadata>> GetTenantMetadataByKey(string tenantId, string key, CancellationToken cancellationToken = default, bool bypassCache = false)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
            if (string.IsNullOrWhiteSpace(key))
            {
                return ServiceResult<TenantMetadata>.BadRequest("Metadata key is required");
            }

            var accessibleTenantId = _tenantContext.AuthorizedTenantIds?.FirstOrDefault(t => t == tenantId);
            if (accessibleTenantId == null)
            {
                _logger.LogWarning("Unauthorized access attempt to metadata of tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<TenantMetadata>.Forbidden("Access denied: insufficient permissions");
            }

            var tenant = await _tenantCacheService.GetByTenantIdAsync(tenantId, cancellationToken, bypassCache);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<TenantMetadata>.NotFound("Tenant not found");
            }

            var entry = tenant.Metadata?.FirstOrDefault(m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase));
            if (entry == null)
            {
                _logger.LogWarning("Metadata key {Key} not found for tenant {TenantId}", LogSanitizer.Sanitize(key), LogSanitizer.Sanitize(tenantId));
                return ServiceResult<TenantMetadata>.NotFound("Metadata key not found");
            }

            var decrypted = _metadataProtector.Unprotect(new List<TenantMetadata> { entry }, tenant.TenantId)!.Single();
            return ServiceResult<TenantMetadata>.Success(decrypted);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while retrieving tenant metadata by key: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<TenantMetadata>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving metadata key {Key} for tenant {TenantId}", LogSanitizer.Sanitize(key), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantMetadata>.InternalServerError("An error occurred while retrieving the tenant metadata.");
        }
    }

    /// <summary>
    /// Adds a metadata entry, or updates value/type when the key (case-insensitive) already
    /// exists. Secret values are encrypted before persisting; the response echoes the entry
    /// as provided by the caller.
    /// </summary>
    public async Task<ServiceResult<TenantMetadata>> UpsertTenantMetadata(string tenantId, string key, UpsertTenantMetadataRequest request)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);

            var existingTenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found for metadata upsert", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<TenantMetadata>.NotFound("Tenant not found");
            }

            EnsureTenantAccessOrThrow(existingTenant.TenantId);

            var entry = new TenantMetadata
            {
                Key = key,
                Value = request.Value,
                Type = request.Type
            };
            var validatedEntry = entry.SanitizeAndValidate();

            var protectedEntry = _metadataProtector.Protect([validatedEntry], existingTenant.TenantId)!.Single();

            var metadata = existingTenant.Metadata ?? [];
            var index = metadata.FindIndex(m => string.Equals(m.Key, validatedEntry.Key, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                metadata[index] = protectedEntry;
            }
            else
            {
                metadata.Add(protectedEntry);
            }
            existingTenant.Metadata = metadata;

            var persistResult = await PersistTenantUpdate(existingTenant, existingTenant.Id);
            if (!persistResult.IsSuccess)
            {
                return ServiceResult<TenantMetadata>.Failure(
                    persistResult.ErrorMessage ?? "Failed to update tenant metadata.", persistResult.StatusCode);
            }

            return ServiceResult<TenantMetadata>.Success(validatedEntry);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while upserting tenant metadata: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<TenantMetadata>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<TenantMetadata>.Forbidden("Access denied: insufficient permissions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error upserting metadata key {Key} for tenant {TenantId}", LogSanitizer.Sanitize(key), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<TenantMetadata>.InternalServerError("An error occurred while updating the tenant metadata.");
        }
    }

    /// <summary>
    /// Removes a single metadata entry by key (case-insensitive).
    /// </summary>
    public async Task<ServiceResult<bool>> DeleteTenantMetadata(string tenantId, string key)
    {
        try
        {
            tenantId = Tenant.SanitizeAndValidateTenantId(tenantId);
            if (string.IsNullOrWhiteSpace(key))
            {
                return ServiceResult<bool>.BadRequest("Metadata key is required");
            }

            var existingTenant = await _tenantRepository.GetByTenantIdAsync(tenantId);
            if (existingTenant == null)
            {
                _logger.LogWarning("Tenant with tenant ID {TenantId} not found for metadata delete", LogSanitizer.Sanitize(tenantId));
                return ServiceResult<bool>.NotFound("Tenant not found");
            }

            EnsureTenantAccessOrThrow(existingTenant.TenantId);

            var removed = existingTenant.Metadata?.RemoveAll(
                m => string.Equals(m.Key, key, StringComparison.OrdinalIgnoreCase)) ?? 0;
            if (removed == 0)
            {
                _logger.LogWarning("Metadata key {Key} not found for tenant {TenantId}", LogSanitizer.Sanitize(key), LogSanitizer.Sanitize(tenantId));
                return ServiceResult<bool>.NotFound("Metadata key not found");
            }

            var persistResult = await PersistTenantUpdate(existingTenant, existingTenant.Id);
            if (!persistResult.IsSuccess)
            {
                return ServiceResult<bool>.Failure(
                    persistResult.ErrorMessage ?? "Failed to update tenant metadata.", persistResult.StatusCode);
            }

            return ServiceResult<bool>.Success(true);
        }
        catch (ValidationException ex)
        {
            _logger.LogWarning("Validation failed while deleting tenant metadata: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<bool>.BadRequest($"Validation failed: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning("Access denied: {Message}", LogSanitizer.Sanitize(ex.Message));
            return ServiceResult<bool>.Forbidden("Access denied: insufficient permissions");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting metadata key {Key} for tenant {TenantId}", LogSanitizer.Sanitize(key), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the tenant metadata.");
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

    /// <summary>
    /// Retrieves a single page of tenants, optionally filtered by a search term
    /// (case-insensitive match on tenantId, name, domain or description). SysAdmin only.
    /// Pagination and filtering are applied at the database level for scalability. Invalid
    /// page/pageSize values fall back to sensible defaults (page 1, pageSize 20) and
    /// pageSize is capped at 100.
    /// </summary>
    public async Task<ServiceResult<TenantListResult>> GetAllTenants(int? page, int? pageSize, string? search = null)
    {
        try
        {
            // Only SysAdmin can list all tenants
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                _logger.LogWarning("Unauthorized attempt to get all tenants by user {UserId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
                return ServiceResult<TenantListResult>.Forbidden("Access denied: Only system administrators can retrieve all tenants");
            }

            const int defaultPageSize = 20;
            const int maxPageSize = 100;

            var pageNum = page.GetValueOrDefault(1);
            if (pageNum < 1)
            {
                pageNum = 1;
            }

            var pageSizeNum = pageSize.GetValueOrDefault(defaultPageSize);
            if (pageSizeNum < 1)
            {
                pageSizeNum = defaultPageSize;
            }
            pageSizeNum = Math.Min(pageSizeNum, maxPageSize);

            var skip = (pageNum - 1) * pageSizeNum;
            var searchTerm = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

            var totalItems = (int)await _tenantRepository.CountAsync(searchTerm);
            var tenants = await _tenantRepository.GetPagedAsync(skip, pageSizeNum, searchTerm);

            var result = new TenantListResult
            {
                Tenants = tenants,
                Pagination = new PaginationInfo
                {
                    Page = pageNum,
                    PageSize = pageSizeNum,
                    TotalPages = (int)Math.Ceiling(totalItems / (double)pageSizeNum),
                    TotalItems = totalItems,
                    HasNext = skip + pageSizeNum < totalItems,
                    HasPrevious = pageNum > 1
                }
            };

            return ServiceResult<TenantListResult>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving paginated tenants");
            return ServiceResult<TenantListResult>.InternalServerError("An error occurred while retrieving tenants.");
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

            var newTenantId = Tenant.SanitizeAndValidateNewTenantId(request.TenantId);

            var tenant = new Tenant
            {
                Id = ObjectId.GenerateNewId().ToString(),
                TenantId = newTenantId,
                Name = request.Name,
                Domain = request.Domain,
                Description = request.Description,
                Logo = request.Logo,
                Theme = request.Theme,
                Timezone = request.Timezone,
                Enabled = false,
                CreatedBy = finalCreatedBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                Metadata = request.Metadata
            };
            _logger.LogInformation("Tenant object created with CreatedBy: {CreatedBy}", LogSanitizer.Sanitize(tenant.CreatedBy));

            var validatedTenant = tenant.SanitizeAndValidate();
            _logger.LogInformation("After sanitization, CreatedBy: {CreatedBy}", LogSanitizer.Sanitize(validatedTenant.CreatedBy));

            // Tenant ids are unique ignoring case: reject "MyTenant" when "mytenant" already exists.
            // The unique index on tenant_id remains the backstop for exact-case duplicates under races.
            var duplicateTenant = await _tenantRepository.GetByTenantIdCaseInsensitiveAsync(validatedTenant.TenantId);
            if (duplicateTenant != null)
            {
                _logger.LogWarning("Tenant ID {TenantId} already exists (case-insensitive match with {ExistingTenantId})",
                    LogSanitizer.Sanitize(validatedTenant.TenantId), LogSanitizer.Sanitize(duplicateTenant.TenantId));
                return ServiceResult<TenantCreatedResult>.BadRequest("A tenant with this ID already exists.");
            }

            // Persist with Secret metadata values encrypted.
            validatedTenant.Metadata = _metadataProtector.Protect(validatedTenant.Metadata, validatedTenant.TenantId);

            await _tenantRepository.CreateAsync(validatedTenant);
            _logger.LogInformation("Created new tenant with ID {Id} and CreatedBy: {CreatedBy}", LogSanitizer.Sanitize(validatedTenant.Id), LogSanitizer.Sanitize(validatedTenant.CreatedBy));

            await _webhookEventPublisher.PublishAsync(
                WebhookEventTypes.TenantCreated,
                new
                {
                    tenantId = validatedTenant.TenantId,
                    name = validatedTenant.Name,
                    domain = validatedTenant.Domain,
                    createdBy = validatedTenant.CreatedBy,
                },
                validatedTenant.TenantId);

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
            var message = DescribeDuplicateTenant(ex.WriteError?.Message, request.TenantId, request.Domain);
            _logger.LogWarning("Rejected duplicate tenant {TenantId}: {Message}. Write error: {WriteError}",
                LogSanitizer.Sanitize(request.TenantId), message, LogSanitizer.Sanitize(ex.WriteError?.Message));
            return ServiceResult<TenantCreatedResult>.BadRequest(message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating tenant");
            return ServiceResult<TenantCreatedResult>.InternalServerError("An error occurred while creating the tenant.");
        }
    }

    /// <summary>
    /// Explains which value collided when MongoDB rejects a tenant write as a duplicate.
    /// The write error names the violated index, which is the only way to tell a tenant id
    /// clash apart from a domain clash.
    /// </summary>
    private static string DescribeDuplicateTenant(string? writeErrorMessage, string tenantId, string? domain)
    {
        var writeError = writeErrorMessage ?? string.Empty;

        if (writeError.Contains("tenant_id", StringComparison.OrdinalIgnoreCase))
        {
            return $"A tenant with ID '{tenantId}' already exists.";
        }

        if (writeError.Contains("domain", StringComparison.OrdinalIgnoreCase))
        {
            // A unique domain index also rejects a tenant that leaves the domain empty when another
            // one already has none, which reads as a false conflict unless the message spells it out.
            return string.IsNullOrWhiteSpace(domain)
                ? "This database requires every tenant to have a unique domain, and another tenant already has none. Provide a domain for this tenant."
                : $"A tenant with domain '{domain}' already exists.";
        }

        return "A tenant with this ID or domain already exists.";
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

            var wasEnabled = existingTenant.Enabled;

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

            if (request.Metadata != null)
                existingTenant.Metadata = _metadataProtector.Protect(request.Metadata, existingTenant.TenantId);

            var result = await PersistTenantUpdate(existingTenant, id);

            if (result.IsSuccess && request.Enabled.HasValue && request.Enabled.Value != wasEnabled)
            {
                await _webhookEventPublisher.PublishAsync(
                    request.Enabled.Value ? WebhookEventTypes.TenantEnabled : WebhookEventTypes.TenantDisabled,
                    new { tenantId = existingTenant.TenantId, id = existingTenant.Id },
                    existingTenant.TenantId);
            }

            return result;
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

        bool success;
        try
        {
            success = await _tenantRepository.UpdateAsync(id, validatedTenant);
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000) // Duplicate key error
        {
            var message = DescribeDuplicateTenant(ex.WriteError?.Message, validatedTenant.TenantId, validatedTenant.Domain);
            _logger.LogWarning("Rejected duplicate tenant update for {Id}: {Message}. Write error: {WriteError}",
                LogSanitizer.Sanitize(id), message, LogSanitizer.Sanitize(ex.WriteError?.Message));
            return ServiceResult<Tenant>.BadRequest(message);
        }

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

        await _webhookEventPublisher.PublishAsync(
            WebhookEventTypes.TenantUpdated,
            new
            {
                tenantId = validatedTenant.TenantId,
                id = validatedTenant.Id,
                name = validatedTenant.Name,
                domain = validatedTenant.Domain,
                enabled = validatedTenant.Enabled,
            },
            validatedTenant.TenantId);

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

                await _webhookEventPublisher.PublishAsync(
                    WebhookEventTypes.TenantDeleted,
                    new { tenantId = existingTenant.TenantId, id = existingTenant.Id, name = existingTenant.Name },
                    existingTenant.TenantId);

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