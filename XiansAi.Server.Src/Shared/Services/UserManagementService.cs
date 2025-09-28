using MongoDB.Driver;
using Shared.Auth;
using Shared.Utils.Services;
using System.Text.Json.Serialization;
using Shared.Repositories;
using Shared.Data.Models;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Utils;

namespace Shared.Services;

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

public class EditUserDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;
    [JsonPropertyName("active")]
    public bool Active { get; set; }
    [JsonPropertyName("isSysAdmin")]
    public bool IsSysAdmin { get; set; }
    [JsonPropertyName("tenantRoles")]
    public List<TenantRoleDto> TenantRoles { get; set; } = new();
}

public class TenantRoleDto
{
    [JsonPropertyName("tenant")]
    public string Tenant { get; set; } = string.Empty;
    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new();
    [BsonElement("isApproved")]
    public required bool IsApproved { get; set; }
}


public class InviteUserDto
{
    [JsonPropertyName("email")]
    public required string Email { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("tenantId")]
    public required string TenantId { get; set; }
    [JsonPropertyName("roles")]
    public List<string> Roles { get; set; } = new List<string> { SystemRoles.TenantUser };
}

public class InviteDto
{
    [JsonPropertyName("email")]
    public string? Email { get; set; }
    [JsonPropertyName("token")]
    public required string Token { get; set; }
    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
    [JsonPropertyName("status")]
    public Status Status { get; set; } = Status.Pending;
}

public class UserFilter
{
    [JsonPropertyName("page")]
    public int  Page { get; set; }
    [JsonPropertyName("pageSize")]
    public int PageSize { get; set; }
    [JsonPropertyName("type")]
    public UserTypeFilter Type { get; set; }
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }
    [JsonPropertyName("search")]
    public string? Search { get; set; }
}

public class PagedUserResult
{
    public List<User> Users { get; set; } = new();
    public long TotalCount { get; set; }
}

public enum UserTypeFilter
{
    ALL,
    ADMIN,
    NON_ADMIN
}

public interface IUserManagementService
{
    Task<ServiceResult<bool>> LockUserAsync(string userId, string reason);
    Task<ServiceResult<bool>> UnlockUserAsync(string userId);
    Task<ServiceResult<bool>> IsUserLockedOutAsync(string userId);
    Task<ServiceResult<PagedUserResult>> GetAllUsersAsync(UserFilter filter);
    Task<ServiceResult<User>> GetUserAsync(string userId);
    Task<ServiceResult<bool>> CreateNewUser(UserDto user);
    Task<ServiceResult<bool>> UpdateUser(EditUserDto user);
    Task<ServiceResult<string>> InviteUserAsync(InviteUserDto invite);
    Task<ServiceResult<List<InviteDto>>> GetAllInvitationsAsync(string tenantId);
    Task<ServiceResult<InviteDto?>> GetInviteByUserEmailAsync(string token);
    Task<ServiceResult<bool>> AcceptInvitationAsync(string invitationToken);
    Task<ServiceResult<bool>> DeleteUser(string userId);
    Task<ServiceResult<List<UserDto>>> SearchUsers(string query);
    Task<ServiceResult<bool>> DeleteInvitation(string token);
}

public class UserManagementService : IUserManagementService
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<UserManagementService> _logger;
    private readonly IAuthMgtConnect _authMgtConnect;
    private readonly IConfiguration _configuration;
    private readonly IInvitationRepository _invitationRepository;
    private readonly IEmailService _emailService;
    private readonly IJwtClaimsExtractor _jwtClaimsExtractor;

    private const string EMAIL_SUBJECT = "Xians.ai - Invitation";

    public UserManagementService(
        IUserRepository userRepository,
        ITenantContext tenantContext,
        IAuthMgtConnect authMgtConnect,
        IConfiguration configuration,
        IInvitationRepository invitationRepository,
        IEmailService emailService,
        IJwtClaimsExtractor jwtClaimsExtractor,
        ILogger<UserManagementService> logger)
    {
        _userRepository = userRepository;
        _tenantContext = tenantContext;
        _logger = logger;
        _authMgtConnect = authMgtConnect;
        _configuration = configuration;
        _invitationRepository = invitationRepository;
        _emailService = emailService;
        _jwtClaimsExtractor = jwtClaimsExtractor;
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

    public async Task<ServiceResult<PagedUserResult>> GetAllUsersAsync(UserFilter filter)
    {
        var users = await _userRepository.GetAllUsersAsync(filter);
        return ServiceResult<PagedUserResult>.Success(users);
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
            var tenantRole = new TenantRole
            {
                Tenant = userDto.TenantId,
                Roles = new List<string> { SystemRoles.TenantUser },
                IsApproved = false
            };
            var newUser = new User
            {
                UserId = userDto.UserId,
                Email = userDto.Email,
                Name = userDto.Name,
                IsSysAdmin = anyUser == null,
                IsLockedOut = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TenantRoles = !String.IsNullOrEmpty(userDto.TenantId) ? new List<TenantRole> { tenantRole }  : new List<TenantRole>()
            };

            try
            {
                var success = await _userRepository.CreateAsync(newUser);
                if (!success)
                {
                    // Check if user was created by another thread/request
                    existingUser = await _userRepository.GetByUserIdAsync(userDto.UserId);
                    if (existingUser != null)
                    {
                        _logger.LogInformation("User {UserId} already exists, creation was redundant", userDto.UserId);
                        return ServiceResult<bool>.Conflict("User already exists");
                    }
                    return ServiceResult<bool>.InternalServerError("Failed to create new user");
                }
                _logger.LogInformation("New user created: {UserId}", userDto.UserId);
                return ServiceResult<bool>.Success(true);
            }
            catch (Exception createEx)
            {
                _logger.LogWarning(createEx, "User creation failed for {UserId}, checking if user already exists", userDto.UserId);

                // Check if user was created by another process
                existingUser = await _userRepository.GetByUserIdAsync(userDto.UserId);
                if (existingUser != null)
                {
                    _logger.LogInformation("User {UserId} already exists after creation failure", userDto.UserId);
                    return ServiceResult<bool>.Conflict("User already exists");
                }

                throw; // Re-throw if it's not a duplicate key issue
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new user {UserId}", userDto.UserId);
            return ServiceResult<bool>.InternalServerError("An error occurred while creating the new user");
        }
    }

    public async Task<ServiceResult<bool>> UpdateUser(EditUserDto user)
    {
        var existingUser = await _userRepository.GetByIdAsync(user.Id);
        if (existingUser == null)
        {
            return ServiceResult<bool>.NotFound("User not found");
        }

        existingUser.Email = user.Email;
        existingUser.UserId = user.UserId;
        existingUser.Name = user.Name;
        existingUser.IsSysAdmin = user.IsSysAdmin;
        existingUser.IsLockedOut = !user.Active;

        if (existingUser.IsLockedOut)
        {
            existingUser.LockedOutBy = _tenantContext.LoggedInUser;
            existingUser.LockedOutReason = "Locked by admin";
        }

        existingUser.TenantRoles = user.TenantRoles.Select(x => {
            return new TenantRole
            {
                Tenant = x.Tenant,
                Roles = x.Roles,
                IsApproved = x.IsApproved
            };
        }).ToList();

        await _userRepository.UpdateAsyncById(user.Id, existingUser);

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<string>> InviteUserAsync(InviteUserDto invite)
    {
        try
        {
            var existingUser = await _userRepository.GetByUserEmailAsync(invite.Email);
            if (existingUser != null)
                return ServiceResult<string>.Conflict("User already exists");

            var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
            var invitation = new Invitation
            {
                Email = invite.Email,
                TenantId = invite.TenantId,
                Roles = invite.Roles,
                Token = token,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            await _invitationRepository.CreateAsync(invitation);
            await _emailService.SendEmailAsync(invite.Email, EMAIL_SUBJECT, GetEmailBody(invitation.ExpiresAt.ToString("f")), false);

            _logger.LogInformation("Invitation created for {Email} (tenant: {TenantId})", invite.Email, invite.TenantId);
            return ServiceResult<string>.Success(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inviting user {Email}", invite.Email);
            return ServiceResult<string>.InternalServerError("An error occurred while inviting the user");
        }
    }

    public async Task<ServiceResult<List<InviteDto>>> GetAllInvitationsAsync(string tenantId)
    {
        var invitations = await _invitationRepository.GetAllAsync(tenantId);
        var returnData = invitations.Select(x =>
        {
            return new InviteDto { Email = x.Email, Token = x.Token, CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt, Status = x.Status };
        }).ToList();
        return ServiceResult<List<InviteDto>>.Success(returnData);
    }

    public async Task<ServiceResult<InviteDto?>> GetInviteByUserEmailAsync(string token)
    {
        try
        {
            var email = await getUserEmailfromToken(token);
            var invitation = await _invitationRepository.GetByEmailAsync(email);
            if (invitation == null)
            {
                return ServiceResult<InviteDto?>.Success(null);
            }

            if (invitation.ExpiresAt < DateTime.UtcNow)
            {
                if (invitation?.ExpiresAt < DateTime.UtcNow)
                {
                    await _invitationRepository.MarkAsExpiredAsync(invitation.Token);
                }
                return ServiceResult<InviteDto?>.Success(null);
            }
            return ServiceResult<InviteDto?>.Success(new InviteDto { Email = email, Token = invitation.Token });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving invitation for user {userId}", _tenantContext.LoggedInUser);
            return ServiceResult<InviteDto?>.InternalServerError("An error occurred while retrieving the invitation");
        }
    }

    public async Task<ServiceResult<bool>> AcceptInvitationAsync(string invitationToken)
    {
        try
        {
            var invitation = await _invitationRepository.GetByTokenAsync(invitationToken);
            if (invitation == null || invitation.Status != Status.Pending || invitation.ExpiresAt < DateTime.UtcNow)
            {
                if (invitation?.ExpiresAt < DateTime.UtcNow)
                {
                    await _invitationRepository.MarkAsExpiredAsync(invitationToken);
                }
                return ServiceResult<bool>.NotFound("Invalid or expired invitation token");
            }

            var existingUser = await _userRepository.GetByUserIdAsync(_tenantContext.LoggedInUser);
            if (existingUser == null)
            {
                return ServiceResult<bool>.Conflict("User not exist");
            }

            existingUser.TenantRoles.Add(new TenantRole
            {
                Tenant = invitation.TenantId,
                Roles = invitation.Roles,
                IsApproved = true
            });

            var user = await _userRepository.UpdateAsync(_tenantContext.LoggedInUser, existingUser);


            if (!user)
                return ServiceResult<bool>.InternalServerError("Failed to accept user invitation");

            await _invitationRepository.MarkAsAcceptedAsync(invitationToken);

            _logger.LogInformation("User {UserId} accepted invitation for tenant {TenantId}", _tenantContext.LoggedInUser, invitation.TenantId);
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting invitation for user {UserId}", _tenantContext.LoggedInUser);
            return ServiceResult<bool>.InternalServerError("An error occurred while accepting the invitation");
        }
    }

    public async Task<ServiceResult<bool>> DeleteUser(string userId)
    {
        var deleted = await _userRepository.DeleteUser(userId);
        return ServiceResult<bool>.Success(deleted);
    }

    public async Task<ServiceResult<List<UserDto>>> SearchUsers(string query)
    {
        var users = await _userRepository.SearchUsersAsync(query);
        var userDtos = users.Select(u => new UserDto
        {
            UserId = u.UserId,
            Email = u.Email,
            Name = u.Name,
            TenantId = u.TenantRoles.FirstOrDefault()?.Tenant ?? string.Empty
        }).ToList();
        return ServiceResult<List<UserDto>>.Success(userDtos);
    }

    public async Task<ServiceResult<bool>> DeleteInvitation(string token)
    {
        var deleted = await _invitationRepository.DeleteInvitation(token);
        return ServiceResult<bool>.Success(deleted);
    }
    /// <summary>
    /// Extracts email from JWT token with proper validation using the centralized JWT utility
    /// SECURITY: Uses centralized JWT validation with JWKS before processing claims
    /// </summary>
    private async Task<string> getUserEmailfromToken(string token)
    {
        try
        {
            // Validate and extract user information using the centralized JWT utility
            var jwtResult = await _jwtClaimsExtractor.ValidateAndExtractClaimsAsync(token);
            if (!jwtResult.IsValid)
            {
                _logger.LogWarning("JWT token validation failed in getUserEmailfromToken: {Error}", 
                    jwtResult.ErrorMessage);
                throw new ArgumentException(jwtResult.ErrorMessage ?? "Invalid or expired token", nameof(token));
            }
            
            if (string.IsNullOrWhiteSpace(jwtResult.Email))
            {
                _logger.LogWarning("Email claim not found in validated token for user: {UserId}", jwtResult.UserId);
                throw new ArgumentException("Email not found in token", nameof(token));
            }

            _logger.LogDebug("Successfully extracted email from validated token for user: {UserId}", jwtResult.UserId);
            return jwtResult.Email;
        }
        catch (ArgumentException)
        {
            // Re-throw argument exceptions (these are expected validation failures)
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting email from token in UserManagementService");
            throw new ArgumentException("Failed to extract email from token", nameof(token), ex);
        }
    }

    private string GetEmailBody(string expiry)
    {
        return $@"Hi there,

You have been invited to Xians.ai. Please login to the portal and accept the invitation.

This invitation will expires on {expiry}.

Best regards,
The Xians.ai Team";
    }
}