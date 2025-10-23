using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Auth;
using Shared.Utils;
using Features.WebApi.Services;
using Features.WebApi.Models;
using System.ComponentModel.DataAnnotations;

namespace Features.PublicApi.Services;

public class PublicJoinTenantRequest
{
    public string TenantId { get; set; } = string.Empty;
}

public class PublicJoinTenantResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty; // "pending", "approved", "rejected"
}

public class PublicCreateTenantRequest
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class PublicCreateTenantResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
}

public interface IPublicRegistrationService
{
    Task<ServiceResult<PublicJoinTenantResponse>> RequestToJoinTenant(PublicJoinTenantRequest request, string userToken);
    Task<ServiceResult<PublicCreateTenantResponse>> CreateNewTenant(PublicCreateTenantRequest request, string userToken);
}

public class PublicRegistrationService : IPublicRegistrationService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserManagementService _userManagementService;
    private readonly IJwtClaimsExtractor _jwtClaimsExtractor;
    private readonly ITenantService _tenantService;
    private readonly IRoleManagementService _roleManagementService;
    private readonly ILogger<PublicRegistrationService> _logger;

    public PublicRegistrationService(
        ITenantRepository tenantRepository,
        IUserRepository userRepository,
        IUserManagementService userManagementService,
        IJwtClaimsExtractor jwtClaimsExtractor,
        ITenantService tenantService,
        IRoleManagementService roleManagementService,
        ILogger<PublicRegistrationService> logger)
    {
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _userManagementService = userManagementService;
        _jwtClaimsExtractor = jwtClaimsExtractor;
        _tenantService = tenantService;
        _roleManagementService = roleManagementService;
        _logger = logger;
    }

    /// <summary>
    /// Sanitizes a domain by removing URL protocol prefixes (http:// or https://)
    /// </summary>
    /// <param name="domain">The domain that may contain URL protocol</param>
    /// <returns>The domain without protocol prefix</returns>
    private static string SanitizeDomainFromUrl(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
            return domain;

        // Remove http:// or https:// prefix if present (case insensitive)
        if (domain.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return domain.Substring(8); // Remove "https://"
        }
        else if (domain.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
        {
            return domain.Substring(7); // Remove "http://"
        }

        return domain;
    }

    public async Task<ServiceResult<PublicJoinTenantResponse>> RequestToJoinTenant(PublicJoinTenantRequest request, string userToken)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return ServiceResult<PublicJoinTenantResponse>.BadRequest("TenantId is required");
            }

            if (string.IsNullOrWhiteSpace(userToken))
            {
                return ServiceResult<PublicJoinTenantResponse>.BadRequest("UserToken is required");
            }

            // Check if tenant exists
            var tenant = await _tenantRepository.GetByTenantIdAsync(request.TenantId);
            if (tenant == null)
            {
                _logger.LogWarning("Tenant join request failed: Tenant {TenantId} not found", request.TenantId);
                return ServiceResult<PublicJoinTenantResponse>.NotFound("Tenant not found");
            }

            // Check if tenant is enabled
            if (!tenant.Enabled)
            {
                _logger.LogWarning("Tenant join request failed: Tenant {TenantId} is disabled", request.TenantId);
                return ServiceResult<PublicJoinTenantResponse>.BadRequest("Tenant is not accepting new members");
            }

            // Validate and extract user information from token using the JWT utility
            var jwtResult = await _jwtClaimsExtractor.ValidateAndExtractClaimsAsync(userToken);
            if (!jwtResult.IsValid || string.IsNullOrEmpty(jwtResult.UserId))
            {
                return ServiceResult<PublicJoinTenantResponse>.BadRequest(
                    jwtResult.ErrorMessage ?? "Invalid user token");
            }

            // Check if user already exists, if not create them
            var user = await _userRepository.GetByUserIdAsync(jwtResult.UserId);
            if (user == null)
            {
                // Create new user
                var newUserDto = new UserDto
                {
                    UserId = jwtResult.UserId,
                    Email = jwtResult.Email ?? string.Empty,
                    Name = jwtResult.Name ?? string.Empty,
                };

                var createUserResult = await _userManagementService.CreateNewUser(newUserDto);
                if (!createUserResult.IsSuccess)
                {
                    _logger.LogError("Failed to create user {UserId} for tenant join request: {Error}", 
                        jwtResult.UserId, createUserResult.ErrorMessage);
                    return ServiceResult<PublicJoinTenantResponse>.InternalServerError("Failed to create user account");
                }

                user = await _userRepository.GetByUserIdAsync(jwtResult.UserId);
                if (user == null)
                {
                    return ServiceResult<PublicJoinTenantResponse>.InternalServerError("Failed to retrieve created user");
                }
            }

            // Check if user is already associated with this tenant
            var existingTenantRole = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == request.TenantId);
            if (existingTenantRole != null)
            {
                if (existingTenantRole.IsApproved)
                {
                    return ServiceResult<PublicJoinTenantResponse>.Success(new PublicJoinTenantResponse
                    {
                        Success = true,
                        Message = "You are already a member of this tenant",
                        TenantId = request.TenantId,
                        Status = "approved"
                    });
                }
                else
                {
                    return ServiceResult<PublicJoinTenantResponse>.Success(new PublicJoinTenantResponse
                    {
                        Success = true,
                        Message = "Your request to join this tenant is already pending approval",
                        TenantId = request.TenantId,
                        Status = "pending"
                    });
                }
            }

            // Add tenant request to user (pending approval)
            var tenantRole = new TenantRole
            {
                Tenant = request.TenantId,
                Roles = new List<string> { SystemRoles.TenantUser },
                IsApproved = false // Requires approval from tenant admin
            };

            user.TenantRoles.Add(tenantRole);

            // Update user with new tenant request
            var updateResult = await _userRepository.UpdateAsync(user.UserId, user);
            if (!updateResult)
            {
                _logger.LogError("Failed to update user {UserId} with tenant join request for {TenantId}", 
                    user.UserId, request.TenantId);
                return ServiceResult<PublicJoinTenantResponse>.InternalServerError("Failed to submit join request");
            }

            _logger.LogInformation("User {UserId} ({Email}) requested to join tenant {TenantId}", 
                user.UserId, user.Email, request.TenantId);

            return ServiceResult<PublicJoinTenantResponse>.Success(new PublicJoinTenantResponse
            {
                Success = true,
                Message = "Your request to join the tenant has been submitted and is pending approval",
                TenantId = request.TenantId,
                Status = "pending"
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing tenant join request for tenant {TenantId}", request.TenantId);
            return ServiceResult<PublicJoinTenantResponse>.InternalServerError("An error occurred while processing your request");
        }
    }

    public async Task<ServiceResult<PublicCreateTenantResponse>> CreateNewTenant(PublicCreateTenantRequest request, string userToken)
    {
        try
        {
            // Validate input
            if (string.IsNullOrWhiteSpace(request.TenantId))
            {
                return ServiceResult<PublicCreateTenantResponse>.BadRequest("TenantId is required");
            }

            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return ServiceResult<PublicCreateTenantResponse>.BadRequest("Tenant name is required");
            }

            if (string.IsNullOrWhiteSpace(userToken))
            {
                return ServiceResult<PublicCreateTenantResponse>.BadRequest("UserToken is required");
            }

            // Sanitize domain by removing protocol prefixes if present
            if (!string.IsNullOrWhiteSpace(request.Domain))
            {
                request.Domain = SanitizeDomainFromUrl(request.Domain);
            }

            // Validate tenant name using the existing validation rules
            try
            {
                Tenant.SanitizeAndValidateTenantId(request.TenantId);
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning("Invalid tenant ID provided: {TenantId}, Error: {Error}", request.TenantId, ex.Message);
                return ServiceResult<PublicCreateTenantResponse>.BadRequest($"Invalid tenant ID: {ex.Message}");
            }

            // Check if tenant already exists
            var existingTenant = await _tenantRepository.GetByTenantIdAsync(request.TenantId);
            if (existingTenant != null)
            {
                _logger.LogWarning("Tenant creation failed: Tenant {TenantId} already exists", request.TenantId);
                return ServiceResult<PublicCreateTenantResponse>.Conflict("A tenant with this ID already exists");
            }

            // Validate and extract user information from token
            var jwtResult = await _jwtClaimsExtractor.ValidateAndExtractClaimsAsync(userToken);
            if (!jwtResult.IsValid || string.IsNullOrEmpty(jwtResult.UserId))
            {
                return ServiceResult<PublicCreateTenantResponse>.BadRequest(
                    jwtResult.ErrorMessage ?? "Invalid user token");
            }

            // Create the tenant
            var createTenantRequest = new CreateTenantRequest
            {
                TenantId = request.TenantId,
                Name = request.Name,
                Domain = request.Domain,
                Description = request.Description
            };

            var tenantResult = await _tenantService.CreateTenant(createTenantRequest, jwtResult.UserId);
            if (!tenantResult.IsSuccess)
            {
                _logger.LogError("Failed to create tenant {TenantId}: {Error}", request.TenantId, tenantResult.ErrorMessage);
                return ServiceResult<PublicCreateTenantResponse>.BadRequest(tenantResult.ErrorMessage ?? "Failed to create tenant");
            }

            // Check if user exists, if not create them
            var user = await _userRepository.GetByUserIdAsync(jwtResult.UserId);
            if (user == null)
            {
                // Create new user
                var newUserDto = new UserDto
                {
                    UserId = jwtResult.UserId,
                    Email = jwtResult.Email ?? string.Empty,
                    Name = jwtResult.Name ?? string.Empty,
                };

                var createUserResult = await _userManagementService.CreateNewUser(newUserDto);
                if (!createUserResult.IsSuccess)
                {
                    _logger.LogError("Failed to create user {UserId} for tenant creation: {Error}", 
                        jwtResult.UserId, createUserResult.ErrorMessage);
                    return ServiceResult<PublicCreateTenantResponse>.InternalServerError("Failed to create user account");
                }

                user = await _userRepository.GetByUserIdAsync(jwtResult.UserId);
                if (user == null)
                {
                    return ServiceResult<PublicCreateTenantResponse>.InternalServerError("Failed to retrieve created user");
                }
            }

            // Add the current user as tenant admin
            var roleDto = new RoleDto
            {
                UserId = jwtResult.UserId,
                TenantId = request.TenantId,
                Role = SystemRoles.TenantAdmin
            };

            var roleResult = await _roleManagementService.AssignTenantRoletoUserAsync(roleDto, skipValidation: true);
            if (!roleResult.IsSuccess)
            {
                _logger.LogError("Created tenant {TenantId} but failed to assign admin role to user {UserId}: {Error}", 
                    request.TenantId, jwtResult.UserId, roleResult.ErrorMessage);
                return ServiceResult<PublicCreateTenantResponse>.InternalServerError("Failed to assign admin role to user. Pelase contact support.");
            }

            _logger.LogInformation("User {UserId} ({Email}) created new tenant {TenantId} and was assigned as admin", 
                jwtResult.UserId, jwtResult.Email, request.TenantId);

            return ServiceResult<PublicCreateTenantResponse>.Success(new PublicCreateTenantResponse
            {
                Success = true,
                Message = "Tenant created successfully and you have been assigned as the tenant administrator",
                TenantId = request.TenantId
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new tenant {TenantId}", request.TenantId);
            return ServiceResult<PublicCreateTenantResponse>.InternalServerError("An error occurred while creating the tenant");
        }
    }

}
