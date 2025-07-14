using Features.WebApi.Auth;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;
using System.IdentityModel.Tokens.Jwt;
using System.Text.Json.Serialization;
using XiansAi.Server.Features.WebApi.Models;
using XiansAi.Server.Features.WebApi.Repositories;
using Shared.Providers.Auth;
using XiansAi.Server.Shared.Repositories;
using Shared.Data.Models;

namespace XiansAi.Server.Features.WebApi.Services;

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
}

public interface IUserManagementService
{
    Task<ServiceResult<bool>> LockUserAsync(string userId, string reason);
    Task<ServiceResult<bool>> UnlockUserAsync(string userId);
    Task<ServiceResult<bool>> IsUserLockedOutAsync(string userId);
    Task<ServiceResult<User>> GetUserAsync(string userId);
    Task<ServiceResult<bool>> CreateNewUser(UserDto user);
    Task<ServiceResult<string>> InviteUserAsync(InviteUserDto invite);
    Task<ServiceResult<List<InviteDto>>> GetAllInvitationsAsync();
    Task<ServiceResult<InviteDto?>> GetInviteByUserEmailAsync(string token);
    Task<ServiceResult<bool>> AcceptInvitationAsync(string invitationToken);
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

    private const string EMAIL_SUBJECT = "Xians.ai - Invitation";

    public UserManagementService(
        IUserRepository userRepository,
        ITenantContext tenantContext,
        IAuthMgtConnect authMgtConnect,
        IConfiguration configuration,
        IInvitationRepository invitationRepository,
        IEmailService emailService,
        ILogger<UserManagementService> logger)
    {
        _userRepository = userRepository;
        _tenantContext = tenantContext;
        _logger = logger;
        _authMgtConnect = authMgtConnect;
        _configuration = configuration;
        _invitationRepository = invitationRepository;
        _emailService = emailService;
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

            var newUser = new User
            {
                UserId = userDto.UserId,
                Email = userDto.Email,
                Name = userDto.Name,
                IsSysAdmin = anyUser == null, 
                IsLockedOut = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TenantRoles = new List<TenantRole>()
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

    public async Task<ServiceResult<List<InviteDto>>> GetAllInvitationsAsync()
    {
        var invitations = await _invitationRepository.GetAllAsync();
        var returnData = invitations.Select(x =>
        {
            return new InviteDto { Email = x.Email, Token = x.Token };
        }).ToList();
        return ServiceResult<List<InviteDto>>.Success(returnData);
    }

    public async Task<ServiceResult<InviteDto?>> GetInviteByUserEmailAsync(string token)
    {
        try
        {
            var email = getUserEmailfromToken(token);
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
            return ServiceResult<InviteDto?>.Success(new InviteDto { Email = email, Token = invitation.Token});
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
    private string getUserEmailfromToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadToken(token) as JwtSecurityToken;
        if (jsonToken == null)
        {
            _logger.LogWarning("Invalid JWT token format");
            throw new ArgumentException("Invalid token format", nameof(token));
        }
        var email = jsonToken.Claims.FirstOrDefault(c => c.Type == "email")?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("Email claim not found in token");
            throw new ArgumentException("Email not found in token", nameof(token));
        }
        return email;
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