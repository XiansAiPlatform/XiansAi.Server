using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Auth;
using Shared.Utils;

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

public interface IPublicRegistrationService
{
    Task<ServiceResult<PublicJoinTenantResponse>> RequestToJoinTenant(PublicJoinTenantRequest request, string userToken);
}

public class PublicRegistrationService : IPublicRegistrationService
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUserManagementService _userManagementService;
    private readonly IJwtClaimsExtractor _jwtClaimsExtractor;
    private readonly ILogger<PublicRegistrationService> _logger;

    public PublicRegistrationService(
        ITenantRepository tenantRepository,
        IUserRepository userRepository,
        IUserManagementService userManagementService,
        IJwtClaimsExtractor jwtClaimsExtractor,
        ILogger<PublicRegistrationService> logger)
    {
        _tenantRepository = tenantRepository;
        _userRepository = userRepository;
        _userManagementService = userManagementService;
        _jwtClaimsExtractor = jwtClaimsExtractor;
        _logger = logger;
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
                    TenantId = request.TenantId // This will be the tenant they're requesting to join
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

}
