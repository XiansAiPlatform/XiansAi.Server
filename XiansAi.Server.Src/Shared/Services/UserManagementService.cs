using MongoDB.Driver;
using Shared.Auth;
using Shared.Utils.Services;
using System.Text.Json.Serialization;
using Shared.Repositories;
using Shared.Data.Models;
using MongoDB.Bson.Serialization.Attributes;
using Shared.Utils;
using Shared.Providers.Auth;

namespace Shared.Services;

public class UserDto
{
    [JsonPropertyName("userId")]
    public string UserId { get; set; } = string.Empty;
    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// OIDC authority the subject was authenticated by, pinned onto the new record. Null for
    /// provisioning paths that do not authenticate against a specific provider, such as operator
    /// bootstrap and admin-created users; those records are pinned on first sign-in instead.
    /// </summary>
    [JsonPropertyName("providerAuthority")]
    public string? ProviderAuthority { get; set; }
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
    [JsonPropertyName("isApproved")]
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
    /// <summary>When set, restricts results to users where IsSysAdmin matches this value.</summary>
    [JsonPropertyName("isSysAdmin")]
    public bool? IsSysAdmin { get; set; }
    /// <summary>When set, restricts results to enabled (true) or disabled (false) accounts.</summary>
    [JsonPropertyName("isEnabled")]
    public bool? IsEnabled { get; set; }
    /// <summary>
    /// When set, restricts results to users holding this tenant role
    /// (within <see cref="Tenant"/> when it is also set, otherwise in any tenant).
    /// Expects a canonical role name, e.g. TenantAdmin.
    /// </summary>
    [JsonPropertyName("role")]
    public string? Role { get; set; }
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
    NON_ADMIN,
    PARTICIPANT,
    PARTICIPANT_ADMIN,
    /// <summary>
    /// Users whose approved tenant role includes <see cref="SystemRoles.TenantParticipant"/> or
    /// <see cref="SystemRoles.TenantParticipantAdmin"/> (may also include other roles such as TenantAdmin or TenantUser).
    /// </summary>
    PARTICIPANT_SCOPE
}

public interface IUserManagementService
{
    Task<ServiceResult<bool>> LockUserAsync(string userId, string reason);
    Task<ServiceResult<bool>> UnlockUserAsync(string userId);
    Task<ServiceResult<bool>> IsUserLockedOutAsync(string userId);
    Task<ServiceResult<PagedUserResult>> GetAllUsersAsync(UserFilter filter);
    Task<ServiceResult<User>> GetUserAsync(string userId);
    /// <summary>
    /// Creates a user record. When <paramref name="allowFirstUserSysAdminBootstrap"/> is false the
    /// new user is never promoted to SysAdmin, even on an empty deployment. Pass false from paths
    /// that provision users implicitly rather than from a deliberate operator sign-in.
    /// </summary>
    Task<ServiceResult<bool>> CreateNewUser(UserDto user, bool allowFirstUserSysAdminBootstrap = true);
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
    private readonly IUserAuthorizationInvalidator _authorizationInvalidator;

    private const string EMAIL_SUBJECT = "Xians.ai - Invitation";

    public UserManagementService(
        IUserRepository userRepository,
        ITenantContext tenantContext,
        IAuthMgtConnect authMgtConnect,
        IConfiguration configuration,
        IInvitationRepository invitationRepository,
        IEmailService emailService,
        IJwtClaimsExtractor jwtClaimsExtractor,
        IUserAuthorizationInvalidator authorizationInvalidator,
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
        _authorizationInvalidator = authorizationInvalidator;
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

            // Every cached decision about this account, across all four APIs, so that the lock
            // takes effect on the next request rather than when those entries expire.
            await _authorizationInvalidator.InvalidateAsync(userId);
            _logger.LogInformation("User {UserId} locked by {AdminUserId} and cached access invalidated", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error locking user {UserId}", LogSanitizer.Sanitize(userId));
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

            // Cached refusals are dropped too, so the account works again without a wait.
            await _authorizationInvalidator.InvalidateAsync(userId);
            _logger.LogInformation("User {UserId} unlocked by {AdminUserId} and cached access invalidated", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error unlocking user {UserId}", LogSanitizer.Sanitize(userId));
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
            _logger.LogError(ex, "Error checking lock status for user {UserId}", LogSanitizer.Sanitize(userId));
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
            _logger.LogError(ex, "Error retrieving user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<User>.InternalServerError("An error occurred while retrieving the user");
        }
    }

    public async Task<ServiceResult<bool>> CreateNewUser(UserDto userDto, bool allowFirstUserSysAdminBootstrap = true)
    {
        try
        {
            var existingUser = await _userRepository.GetByUserIdAsync(userDto.UserId);
            if (existingUser != null)
            {
                return ServiceResult<bool>.Conflict("User already exists");
            }

            // One person can legitimately hold an account at two identity providers, so an address
            // shared across providers is allowed. Two records under the *same* provider are not:
            // there the address really does name one account, so a second one is a duplicate.
            //
            // Records with no email cannot collide and are not compared, or they would all match
            // each other.
            var admission = EmailAdmission.Allowed;
            if (!string.IsNullOrWhiteSpace(userDto.Email))
            {
                admission = await AdmitEmailAsync(userDto);
                if (admission.Conflict != null)
                {
                    return admission.Conflict;
                }
            }
            else
            {
                // Without an address the record cannot be matched to an existing account on a later
                // sign-in through a different door, so it silently becomes a second identity for
                // the same person. The creation proceeds — blank emails are not unique — but the
                // gap is worth seeing without going looking for it.
                _logger.LogWarning(
                    "Provisioning {UserId} with no email from {Authority}; the record cannot be " +
                    "matched to an existing account and may become a duplicate",
                    LogSanitizer.RedactUserId(userDto.UserId),
                    LogSanitizer.Sanitize(userDto.ProviderAuthority ?? "(unknown)"));
            }
            // The very first user record ever created becomes the global SysAdmin, which bootstraps
            // a fresh deployment. Callers that provision users implicitly rather than from an
            // operator sign-in opt out, so that a token holder cannot claim SysAdmin simply by
            // being the first through the door.
            var isFirstUserBootstrap = allowFirstUserSysAdminBootstrap
                && (await _userRepository.GetAnyUserAsync()) == null;

            var newUser = new User
            {
                UserId = userDto.UserId,
                Email = userDto.Email,
                Name = userDto.Name,
                ProviderAuthority = userDto.ProviderAuthority,
                IsSysAdmin = isFirstUserBootstrap,
                IsLockedOut = admission.NeedsReview,
                LockedOutReason = admission.NeedsReview
                    ? SharedWithSysAdminReason(admission.SysAdminOwnerId!)
                    : null,
                LockedOutAt = admission.NeedsReview ? DateTime.UtcNow : null,
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
                        _logger.LogInformation("User {UserId} already exists, creation was redundant", LogSanitizer.RedactUserId(userDto.UserId));
                        return ServiceResult<bool>.Conflict("User already exists");
                    }
                    // The insert was rejected and the record still is not there, so this is not a
                    // creation race. On a deployment that has carried more than one schema version
                    // the usual cause is a unique index no longer in mongodb-indexes.yaml — Cosmos DB
                    // does not drop unused indexes, so an old one keeps rejecting writes forever.
                    _logger.LogError(
                        "Could not create user {UserId} and it is still absent afterwards. Check the " +
                        "preceding UserRepository entry for the rejected write, and compare " +
                        "db.users.getIndexes() against mongodb-indexes.yaml for stale unique indexes",
                        LogSanitizer.RedactUserId(userDto.UserId));
                    return ServiceResult<bool>.InternalServerError("Failed to create new user");
                }
                _logger.LogInformation("New user created: {UserId}", LogSanitizer.RedactUserId(userDto.UserId));
                return ServiceResult<bool>.Success(true);
            }
            catch (Exception createEx)
            {
                _logger.LogWarning(createEx, "User creation failed for {UserId}, checking if user already exists", LogSanitizer.RedactUserId(userDto.UserId));

                // Check if user was created by another process
                existingUser = await _userRepository.GetByUserIdAsync(userDto.UserId);
                if (existingUser != null)
                {
                    _logger.LogInformation("User {UserId} already exists after creation failure", LogSanitizer.RedactUserId(userDto.UserId));
                    return ServiceResult<bool>.Conflict("User already exists");
                }

                throw; // Re-throw if it's not a duplicate key issue
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new user {UserId}", LogSanitizer.RedactUserId(userDto.UserId));
            return ServiceResult<bool>.InternalServerError("An error occurred while creating the new user");
        }
    }

    /// <summary>
    /// Whether a record may be created for an address somebody already holds, and in what state.
    /// </summary>
    /// <param name="Conflict">The result to return instead of creating anything.</param>
    /// <param name="SysAdminOwnerId">
    /// The system administrator already holding the address, when the record is to be created
    /// disabled so a person can decide whether the two are the same someone.
    /// </param>
    private sealed record EmailAdmission(ServiceResult<bool>? Conflict, string? SysAdminOwnerId)
    {
        public static readonly EmailAdmission Allowed = new(null, null);

        public static EmailAdmission Refuse(ServiceResult<bool> conflict) => new(conflict, null);

        public static EmailAdmission ForReview(string sysAdminOwnerId) => new(null, sysAdminOwnerId);

        public bool NeedsReview => SysAdminOwnerId != null;
    }

    /// <summary>
    /// Decides what to do about an address an existing record already holds.
    ///
    /// A second account at a *different* identity provider belongs to the same person arriving
    /// through another door, so it is allowed — that is the case this exists for. Two records under
    /// providers that cannot be told apart are refused: the same provider, where the address really
    /// does name one account, and an unknown provider on either side, since a record that might
    /// have come from the same provider has to be treated as the same account.
    ///
    /// An address a system administrator holds is neither allowed outright nor refused. Refusing it
    /// leaves a real person in a second directory unable to sign in at all, with no way through
    /// that does not involve deleting somebody's account. Allowing it outright would strip SysAdmin
    /// from the account that has it, since the role is never resolved from an address whose records
    /// do not all hold it. So the record is created disabled: inert, invisible to that resolution,
    /// and waiting for an administrator to decide.
    /// </summary>
    private async Task<EmailAdmission> AdmitEmailAsync(UserDto userDto)
    {
        var owners = await _userRepository.GetAllByUserEmailAsync(userDto.Email);
        if (owners.Count == 0)
        {
            return EmailAdmission.Allowed;
        }

        // Before the SysAdmin case, so that a duplicate under the same provider is refused for
        // being a duplicate rather than queued for a review that should never happen.
        var sameProviderOwner = owners.FirstOrDefault(
            owner => !IsDifferentProvider(owner.ProviderAuthority, userDto.ProviderAuthority));
        if (sameProviderOwner != null)
        {
            _logger.LogWarning(
                "Refusing to create {UserId} from {Authority}: {ExistingUserId} already holds its email " +
                "and cannot be told apart as a separate provider",
                LogSanitizer.RedactUserId(userDto.UserId),
                LogSanitizer.Sanitize(userDto.ProviderAuthority ?? "(unknown)"),
                LogSanitizer.RedactUserId(sameProviderOwner.UserId));
            return EmailAdmission.Refuse(ServiceResult<bool>.Conflict("A user with this email already exists"));
        }

        var sysAdminOwner = owners.FirstOrDefault(owner => owner.IsSysAdmin);
        if (sysAdminOwner != null)
        {
            _logger.LogWarning(
                "Creating {UserId} from {Authority} disabled: its email belongs to system administrator " +
                "{ExistingUserId}. Until an administrator enables it and grants it the same role, it " +
                "cannot sign in and does not affect what that address resolves to",
                LogSanitizer.RedactUserId(userDto.UserId),
                LogSanitizer.Sanitize(userDto.ProviderAuthority ?? "(unknown)"),
                LogSanitizer.RedactUserId(sysAdminOwner.UserId));
            return EmailAdmission.ForReview(sysAdminOwner.UserId);
        }

        _logger.LogInformation(
            "Creating {UserId} from {Authority} with an email already held by {Count} account(s) at " +
            "other providers; they resolve as one identity where a credential names only the address",
            LogSanitizer.RedactUserId(userDto.UserId),
            LogSanitizer.Sanitize(userDto.ProviderAuthority ?? "(unknown)"),
            owners.Count);
        return EmailAdmission.Allowed;
    }

    /// <summary>
    /// Why the record is disabled, written for whoever finds it in the admin console and has to
    /// decide. It states the consequence of enabling it on its own, because that consequence lands
    /// on a different account than the one being enabled and is otherwise invisible from here.
    /// </summary>
    private static string SharedWithSysAdminReason(string sysAdminUserId) =>
        $"Created disabled for review: this email is also held by system administrator {sysAdminUserId}. " +
        "If both accounts belong to the same person, enable this one and grant it the system " +
        "administrator role in the same sitting. Enabling it without the grant takes that role away " +
        "from credentials that name only an email address — legacy API keys and certificates whose " +
        "OU is an email — until the grant is made.";

    /// <summary>
    /// True only when both records name a provider and the two differ. An unknown provider on
    /// either side reads as "cannot be told apart", because a record that might have come from the
    /// same provider has to be treated as the same account.
    /// </summary>
    private static bool IsDifferentProvider(string? existingAuthority, string? incomingAuthority) =>
        !string.IsNullOrEmpty(existingAuthority)
        && !string.IsNullOrEmpty(incomingAuthority)
        && !string.Equals(existingAuthority, incomingAuthority, StringComparison.OrdinalIgnoreCase);

    public async Task<ServiceResult<bool>> UpdateUser(EditUserDto user)
    {
        var existingUser = await _userRepository.GetByIdAsync(user.Id);
        if (existingUser == null)
        {
            return ServiceResult<bool>.NotFound("User not found");
        }

        // Validate that the user belongs to the current tenant (unless logged-in user is SysAdmin)
        if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
        {
            var belongsToTenant = existingUser.TenantRoles.Any(tr => tr.Tenant == _tenantContext.TenantId);
            if (!belongsToTenant && !existingUser.IsSysAdmin)
            {
                _logger.LogWarning("User {UserId} does not belong to tenant {TenantId}. IDOR attempt detected.", LogSanitizer.Sanitize(user.UserId), LogSanitizer.Sanitize(_tenantContext.TenantId));
                return ServiceResult<bool>.Forbidden("User does not belong to the current tenant");
            }
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

        // Drop every cached authorization decision for this account so role/lock changes take effect now.
        await _authorizationInvalidator.InvalidateAsync(user.UserId);
        _logger.LogInformation("Invalidated cached authorization for user {UserId} after update", LogSanitizer.Sanitize(user.UserId));

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<string>> InviteUserAsync(InviteUserDto invite)
    {
        try
        {
            // Validate tenant access - tenant admins can only invite to their own tenant
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                if (!_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    _logger.LogWarning("User {UserId} attempted to invite user without admin permissions", 
                        LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
                    return ServiceResult<string>.Forbidden("Only admins can invite users");
                }
                
                // Tenant admin can only invite to their own tenant
                if (invite.TenantId != _tenantContext.TenantId)
                {
                    _logger.LogWarning("Tenant admin {UserId} attempted to invite user to different tenant. Current: {CurrentTenant}, Invite: {InviteTenant}", 
                        LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(_tenantContext.TenantId), LogSanitizer.Sanitize(invite.TenantId));
                    return ServiceResult<string>.Forbidden("Cannot invite users to other tenants");
                }
            }
            
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

            _logger.LogInformation("Invitation created for {Email} (tenant: {TenantId}) by user {UserId}", 
                LogSanitizer.Sanitize(invite.Email), LogSanitizer.Sanitize(invite.TenantId), LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
            return ServiceResult<string>.Success(token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error inviting user {Email}", LogSanitizer.Sanitize(invite.Email));
            return ServiceResult<string>.InternalServerError("An error occurred while inviting the user");
        }
    }

    public async Task<ServiceResult<List<InviteDto>>> GetAllInvitationsAsync(string tenantId)
    {
        try
        {
            // Validate tenant access - tenant admins can only see invitations for their own tenant
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                if (!_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    _logger.LogWarning("User {UserId} attempted to get invitations without admin permissions", 
                        LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
                    return ServiceResult<List<InviteDto>>.Forbidden("Only admins can view invitations");
                }
                
                // Tenant admin can only view invitations for their own tenant
                if (tenantId != _tenantContext.TenantId)
                {
                    _logger.LogWarning("Tenant admin {UserId} attempted to get invitations for different tenant. Current: {CurrentTenant}, Requested: {RequestedTenant}", 
                        LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(_tenantContext.TenantId), LogSanitizer.Sanitize(tenantId));
                    return ServiceResult<List<InviteDto>>.Forbidden("Cannot view invitations from other tenants");
                }
            }
            
            var invitations = await _invitationRepository.GetAllAsync(tenantId);
            var returnData = invitations.Select(x =>
            {
                return new InviteDto { Email = x.Email, Token = x.Token, CreatedAt = x.CreatedAt, ExpiresAt = x.ExpiresAt, Status = x.Status };
            }).ToList();
            return ServiceResult<List<InviteDto>>.Success(returnData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting invitations for tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<List<InviteDto>>.InternalServerError("An error occurred while retrieving invitations");
        }
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
            _logger.LogError(ex, "Error retrieving invitation for user {userId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
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
                return ServiceResult<bool>.Conflict("User does not exist");
            }
            
            // Security: Verify invitation email matches the current user's email
            if (!string.Equals(invitation.Email, existingUser.Email, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("User {UserId} ({Email}) attempted to accept invitation for different email {InvitationEmail}", 
                    LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.RedactEmail(existingUser.Email), LogSanitizer.RedactEmail(invitation.Email));
                return ServiceResult<bool>.Forbidden("This invitation is for a different email address");
            }
            
            // Check if user already has this tenant
            if (existingUser.TenantRoles.Any(tr => tr.Tenant == invitation.TenantId))
            {
                _logger.LogWarning("User {UserId} already has access to tenant {TenantId}", 
                    LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(invitation.TenantId));
                return ServiceResult<bool>.Conflict("You already have access to this tenant");
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

            _logger.LogInformation("User {UserId} accepted invitation for tenant {TenantId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(invitation.TenantId));
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error accepting invitation for user {UserId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
            return ServiceResult<bool>.InternalServerError("An error occurred while accepting the invitation");
        }
    }

    public async Task<ServiceResult<bool>> DeleteUser(string userId)
    {
        // System admins can delete users from any tenant, others can only delete from their own tenant
        var tenantId = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) 
            ? null 
            : _tenantContext.TenantId;
            
        var deleted = await _userRepository.DeleteUser(userId, tenantId);
        if (!deleted)
        {
            return ServiceResult<bool>.NotFound("User not found or does not belong to the current tenant");
        }
        
        // Drop every cached authorization decision for the deleted user.
        await _authorizationInvalidator.InvalidateAsync(userId);
        _logger.LogInformation("User {UserId} deleted and authorization caches invalidated", LogSanitizer.Sanitize(userId));
        
        return ServiceResult<bool>.Success(deleted);
    }

    public async Task<ServiceResult<List<UserDto>>> SearchUsers(string query)
    {
        // System admins can search users across all tenants, others can only search within their own tenant
        var tenantId = _tenantContext.UserRoles.Contains(SystemRoles.SysAdmin) 
            ? null 
            : _tenantContext.TenantId;
            
        var users = await _userRepository.SearchUsersAsync(query, tenantId);
        var userDtos = users.Select(u => new UserDto
        {
            UserId = u.UserId,
            Email = u.Email,
            Name = u.Name,
        }).ToList();
        return ServiceResult<List<UserDto>>.Success(userDtos);
    }

    public async Task<ServiceResult<bool>> DeleteInvitation(string token)
    {
        try
        {
            // First, get the invitation to check its tenant
            var invitation = await _invitationRepository.GetByTokenAsync(token);
            if (invitation == null)
            {
                return ServiceResult<bool>.NotFound("Invitation not found");
            }
            
            // Validate tenant access - only sysadmin or tenant admin of the invitation's tenant can delete it
            if (!_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                if (!_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
                {
                    _logger.LogWarning("User {UserId} attempted to delete invitation without admin permissions", 
                        LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
                    return ServiceResult<bool>.Forbidden("Only admins can delete invitations");
                }
                
                // Tenant admin can only delete invitations for their own tenant
                if (invitation.TenantId != _tenantContext.TenantId)
                {
                    _logger.LogWarning("Tenant admin {UserId} attempted to delete invitation for different tenant. Current: {CurrentTenant}, Invitation: {InvitationTenant}", 
                        LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(_tenantContext.TenantId), LogSanitizer.Sanitize(invitation.TenantId));
                    return ServiceResult<bool>.Forbidden("Cannot delete invitations from other tenants");
                }
            }
            
            var deleted = await _invitationRepository.DeleteInvitation(token);
            if (!deleted)
            {
                return ServiceResult<bool>.InternalServerError("Failed to delete invitation");
            }
            
            _logger.LogInformation("Invitation {Token} deleted by user {UserId}", LogSanitizer.Sanitize(token), LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
            return ServiceResult<bool>.Success(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting invitation {Token}", LogSanitizer.Sanitize(token));
            return ServiceResult<bool>.InternalServerError("An error occurred while deleting the invitation");
        }
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
                    LogSanitizer.Sanitize(jwtResult.ErrorMessage));
                throw new ArgumentException(jwtResult.ErrorMessage ?? "Invalid or expired token", nameof(token));
            }
            
            if (string.IsNullOrWhiteSpace(jwtResult.Email))
            {
                _logger.LogWarning("Email claim not found in validated token for user: {UserId}", LogSanitizer.Sanitize(jwtResult.UserId));
                throw new ArgumentException("Email not found in token", nameof(token));
            }

            _logger.LogDebug("Successfully extracted email from validated token for user: {UserId}", LogSanitizer.Sanitize(jwtResult.UserId));
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