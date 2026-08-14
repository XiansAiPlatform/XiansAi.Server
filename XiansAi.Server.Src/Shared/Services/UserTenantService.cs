using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Auth;
using Shared.Utils.Services;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils;

namespace Shared.Services;

public class UserTenantDto
{
    public string UserId { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public bool IsApproved { get; set; } = false;
}

public class JoinTenantRequestDto
{
    public string TenantId { get; set; } = string.Empty;
}

public class AddUserToTenantDto
{
    public string Email { get; set; } = string.Empty;
}

public class TenantInfoDto
{
    public string TenantId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}

public class CreateNewUserDto
{
    public required string Email { get; set; }
    public required string Name { get; set; }
    public List<string> TenantRoles { get; set; } = new();
}

/// <summary>
/// Which account a validated token acts as, and what that account may act on.
/// </summary>
public class ResolvedUserAccess
{
    /// <summary>
    /// The account the token resolves to — the provider subject that keys the users collection.
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>Enabled tenants the account is an approved member of. Empty grants nothing.</summary>
    public required List<TenantInfoDto> Tenants { get; init; }

    /// <summary>
    /// The address that may name this account's message threads, or null when it may not because
    /// another account holds it too. Threads are namespaced by this, so it has to name exactly one
    /// account.
    /// </summary>
    public string? ConversationEmail { get; init; }

    /// <summary>
    /// The address stored on the record, whether or not another account also holds it. It may
    /// identify the caller — recognising them when they name themselves by email — but must never
    /// namespace anything, which is what <see cref="ConversationEmail"/> is for.
    /// </summary>
    public string? AccountEmail { get; init; }
}

/// <summary>
/// What a validated token says about the person signing in, as far as provisioning needs it.
/// </summary>
public sealed class SignInIdentity
{
    /// <summary>The raw provider subject, which is the form the users collection is keyed on.</summary>
    public required string UserId { get; init; }

    public string? Email { get; init; }

    public string? Name { get; init; }

    /// <summary>
    /// The authority whose signing keys authenticated the subject. A subject is only unique within
    /// one issuer, so the record is pinned to this.
    /// </summary>
    public string? ProviderAuthority { get; init; }
}

public interface IUserTenantService
{
    Task<ServiceResult<List<TenantInfoDto>>> GetCurrentUserTenants(string token);
    Task<ServiceResult<List<TenantInfoDto>>> GetTenantsForCurrentUser();
    Task<ServiceResult<List<TenantInfoDto>>> GetTenantsForUser(string userId);
    Task<ServiceResult<List<TenantInfoDto>>> GetApprovedTenantsForUserId(string userId);
    Task<ServiceResult<ResolvedUserAccess>> EnsureUserAndGetApprovedTenants(
        SignInIdentity identity, string? requestedTenantId = null, bool approveNewMembership = false);
    Task<ServiceResult<List<User>>> GetUnapprovedUsers();
    Task<ServiceResult<bool>> AddTenantToUser(string userId, string tenantId);
    Task<ServiceResult<bool>> RemoveTenantFromUser(string userId, string tenantId);
    Task<ServiceResult<bool>> ApproveUser(string userId, string tenantId, bool approve);
    Task<ServiceResult<bool>> RequestToJoinTenant(string tenantId);
    Task<ServiceResult<PagedUserResult>> GetTenantUsers(UserFilter filter);
    Task<ServiceResult<bool>> UpdateTenantUser(EditUserDto user);
    Task<ServiceResult<bool>> AddTenantToUserIfExist(string email);
    Task<ServiceResult<User>> CreateNewUserInTenant(CreateNewUserDto dto, string tenantId);
}

public class UserTenantService : IUserTenantService
{
    private readonly IUserRepository _userRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly ILogger<UserTenantService> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly IAuthMgtConnect _authMgtConnect;
    private readonly IConfiguration _configuration;
    private readonly IUserManagementService _userManagementService;
    private readonly IJwtClaimsExtractor _jwtClaimsExtractor;

    public UserTenantService(IUserRepository userRepository,
        ILogger<UserTenantService> logger,
        ITenantContext tenantContext,
        IAuthMgtConnect authMgtConnect,
        IConfiguration configuration,
        IUserManagementService userManagementService,
        ITenantRepository tenantRepository,
        IJwtClaimsExtractor jwtClaimsExtractor)
    {
        _userRepository = userRepository;
        _logger = logger;
        _tenantRepository = tenantRepository;
        _tenantContext = tenantContext;
        _authMgtConnect = authMgtConnect;
        _configuration = configuration;
        _userManagementService = userManagementService;
        _jwtClaimsExtractor = jwtClaimsExtractor;
    }

    public async Task<ServiceResult<List<TenantInfoDto>>> GetCurrentUserTenants(string token)
    {
        var userId = _tenantContext.LoggedInUser;
        if (string.IsNullOrEmpty(userId))
            return ServiceResult<List<TenantInfoDto>>.Unauthorized("User not authenticated");

        if (string.IsNullOrEmpty(token))
        {
            _logger.LogWarning("Token is null or empty");
            return ServiceResult<List<TenantInfoDto>>.Unauthorized("Token is required");
        }

        var user = await _userRepository.GetByUserIdAsync(userId);
        if (user == null)
        {
            // Ensure user exists in the system
            var userDto = await createUserFromToken(token);
            if (userDto == null)
            {
                // createUserFromToken returns null only for an email collision — no record exists
                // under this user id, and creating one would detach the person from the account
                // that already holds their address. Same wording as the UserApi path so both doors
                // agree.
                return ServiceResult<List<TenantInfoDto>>.Unauthorized(
                    "This email is already registered to a different account");
            }
            _logger.LogInformation("User {UserId} created from token", LogSanitizer.Sanitize(userDto.UserId));
        }

        // Optional: Azure AD group-based SysAdmin promotion
        // Runs on every login to keep IsSysAdmin in sync with Azure AD group membership
        await SyncSysAdminFromGroupClaimsAsync(userId, token);

        return await GetTenantsForCurrentUser();
    }

    public Task<ServiceResult<List<TenantInfoDto>>> GetTenantsForCurrentUser()
    {
        return GetApprovedTenantsForUserId(_tenantContext.LoggedInUser);
    }

    /// <summary>
    /// Creates the user record if this is their first sign-in, then returns the tenants they are an
    /// approved member of. Where the membership still needs approving this returns an empty list and
    /// the caller refuses the request until an admin acts. Identity comes from an already-validated
    /// token; the token is not re-parsed here.
    ///
    /// <see cref="SignInIdentity.ProviderAuthority"/> ties the subject to the provider that
    /// authenticated it. A subject is only unique within one issuer, so without this a provider
    /// could assert another provider's subject and resolve that person's record.
    ///
    /// <paramref name="requestedTenantId"/> is the tenant the caller was trying to reach. Supplying
    /// it registers the user on that tenant, which is what makes them visible to its admins; without
    /// it a rejected first-time user leaves no trace anyone can act on.
    ///
    /// <paramref name="approveNewMembership"/> makes that membership approved rather than pending.
    /// Only for callers that validated the token against the tenant's own OIDC configuration, where
    /// holding such a token is itself the tenant's statement that this person belongs to it. The
    /// WebAPI console path must not set it: its tokens are validated against the deployment-wide
    /// provider, so a token holder could otherwise join any tenant they name.
    /// </summary>
    public async Task<ServiceResult<ResolvedUserAccess>> EnsureUserAndGetApprovedTenants(
        SignInIdentity identity, string? requestedTenantId = null, bool approveNewMembership = false)
    {
        var userId = identity.UserId;
        if (string.IsNullOrEmpty(userId))
            return ServiceResult<ResolvedUserAccess>.Unauthorized("User not authenticated");

        var providerAuthority = identity.ProviderAuthority;
        if (string.IsNullOrEmpty(providerAuthority))
        {
            _logger.LogWarning("No provider authority for user {UserId}; cannot establish which provider " +
                "asserted this subject, so denying", LogSanitizer.RedactUserId(userId));
            return ServiceResult<ResolvedUserAccess>.Unauthorized("Identity provider could not be determined");
        }

        try
        {
            var existingUser = await _userRepository.GetByUserIdAsync(userId);
            if (existingUser == null)
            {
                // This path is reachable by anyone holding a token that validates against some
                // tenant's OIDC rules, so it must not be able to claim the first-user SysAdmin
                // bootstrap. That promotion stays reserved for the WebAPI operator sign-in flow.
                var created = await _userManagementService.CreateNewUser(
                    new UserDto
                    {
                        UserId = userId,
                        Email = identity.Email ?? string.Empty,
                        Name = identity.Name ?? string.Empty,
                        ProviderAuthority = providerAuthority
                    },
                    allowFirstUserSysAdminBootstrap: false);

                if (created.IsSuccess)
                {
                    _logger.LogInformation("Provisioned user {UserId} on first sign-in", LogSanitizer.RedactUserId(userId));
                    await RegisterMembershipAsync(userId, requestedTenantId, approveNewMembership);
                    return await GetAccessForUserId(userId);
                }

                if (created.StatusCode != StatusCode.Conflict)
                {
                    _logger.LogError("Failed to provision user {UserId}: {Error}",
                        LogSanitizer.RedactUserId(userId), LogSanitizer.Sanitize(created.ErrorMessage));
                    return ServiceResult<ResolvedUserAccess>.InternalServerError("Failed to provision user");
                }

                // Either a concurrent request created it first, in which case re-reading finds it
                // and it is held to the same checks as any other existing record...
                existingUser = await _userRepository.GetByUserIdAsync(userId);
                if (existingUser == null)
                {
                    // ...or the conflict was on the email. A second account at another provider is
                    // allowed — including one whose address a system administrator holds, which is
                    // created disabled instead — so reaching here means the address is
                    // indistinguishable from an existing one: the same provider, or a provider that
                    // cannot be identified.
                    _logger.LogWarning(
                        "Refusing to provision {UserId} from {Authority}: the email in this token is " +
                        "already registered to a different account.",
                        LogSanitizer.RedactUserId(userId), LogSanitizer.Sanitize(providerAuthority));
                    return ServiceResult<ResolvedUserAccess>.Unauthorized(
                        "This email is already registered to a different account");
                }
            }

            if (!await IsSameProviderAsync(existingUser, providerAuthority))
            {
                return ServiceResult<ResolvedUserAccess>.Unauthorized(
                    "This subject is registered to a different identity provider");
            }

            await BackfillMissingProfileAsync(existingUser, identity);

            // Also for an existing record: a known user reaching a tenant they have never been a
            // member of is the same situation as a brand new one, and needs the same visibility.
            await RegisterMembershipAsync(userId, requestedTenantId, approveNewMembership);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error provisioning user {UserId}", LogSanitizer.RedactUserId(userId));
            return ServiceResult<ResolvedUserAccess>.InternalServerError("Error provisioning user");
        }

        return await GetAccessForUserId(userId);
    }

    /// <summary>
    /// Pairs the account id with the tenants it is approved for.
    /// </summary>
    private async Task<ServiceResult<ResolvedUserAccess>> GetAccessForUserId(string userId)
    {
        var tenants = await GetApprovedTenantsForUserId(userId);
        if (!tenants.IsSuccess || tenants.Data == null)
        {
            // Carries the original status through: a disabled account is refused there, and
            // flattening that to a 500 would tell the caller the server broke.
            return ServiceResult<ResolvedUserAccess>.Failure(
                tenants.ErrorMessage ?? "Error getting tenants for user",
                tenants.StatusCode);
        }

        var user = await _userRepository.GetByUserIdAsync(userId);

        return ServiceResult<ResolvedUserAccess>.Success(
            new ResolvedUserAccess
            {
                UserId = userId,
                Tenants = tenants.Data,
                ConversationEmail = await ConversationIdentityEmailAsync(user),
                AccountEmail = NormalizeEmail(user?.Email)
            });
    }

    /// <summary>
    /// The address this account may be named by in conversations, or null when it may not be.
    ///
    /// The caller uses this as the participant id, which is the namespace their message threads live
    /// in, so an address two accounts answer to would put both in one namespace and let either read
    /// the other's conversations. Where the address does not name a single account, the account
    /// falls back to being named by its provider subject, which always does.
    /// </summary>
    private async Task<string?> ConversationIdentityEmailAsync(User? user)
    {
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            return null;
        }

        if (await _userRepository.IsEmailSharedAsync(user.Email, user.UserId))
        {
            _logger.LogWarning(
                "Not using the email of {UserId} as their conversation identity: another account holds " +
                "the same address, and sharing one would merge their message threads",
                LogSanitizer.RedactUserId(user.UserId));
            return null;
        }

        return NormalizeEmail(user.Email);
    }

    private static string? NormalizeEmail(string? email)
    {
        return string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Fills in an address or a name the record is missing, from what the token says.
    ///
    /// Records provisioned before the address was stored have neither, and nothing else ever
    /// revisits them — the create path only runs on a first sign-in. Only blanks are filled, because
    /// a value already on the record may have been set by an admin.
    /// </summary>
    private async Task BackfillMissingProfileAsync(User user, SignInIdentity identity)
    {
        var fillEmail = string.IsNullOrWhiteSpace(user.Email) && !string.IsNullOrWhiteSpace(identity.Email);
        var fillName = string.IsNullOrWhiteSpace(user.Name) && !string.IsNullOrWhiteSpace(identity.Name);

        if (!fillEmail && !fillName)
        {
            return;
        }

        try
        {
            // Written field by field rather than as a whole record, so a membership another request
            // is appending right now cannot be undone by writing back the copy read a moment ago.
            await _userRepository.UpdateProfileFieldsAsync(
                user.UserId,
                email: fillEmail ? identity.Email : null,
                name: fillName ? identity.Name : null);

            _logger.LogInformation(
                "Filled in missing profile fields for {UserId} from their token (email: {FilledEmail}, name: {FilledName})",
                LogSanitizer.RedactUserId(user.UserId), fillEmail, fillName);
        }
        catch (Exception ex)
        {
            // Cosmetic next to the reason the caller is here, so a failure must not deny the sign-in.
            _logger.LogWarning(ex, "Could not fill in missing profile fields for {UserId}",
                LogSanitizer.RedactUserId(user.UserId));
        }
    }

    /// <summary>
    /// Registers the user on the tenant they tried to reach, so that its admins can see them.
    ///
    /// Unapproved, the membership grants no access and exists only for an admin to approve or
    /// ignore. Approved — see <c>approveNewMembership</c> on the caller — it admits them as a
    /// participant, which is what lets an agent hold a conversation with them without also handing
    /// them the WebAPI console.
    ///
    /// Only ever adds a missing membership. An existing row is left as it stands: an admin may have
    /// deliberately withheld approval, and there is no separate state that says so.
    ///
    /// Skipped when the tenant does not exist or is disabled. The tenant id arrives from the caller,
    /// so without that check anyone holding a valid token could append a row to their own user
    /// record for every name they cared to try.
    /// </summary>
    private async Task RegisterMembershipAsync(string userId, string? requestedTenantId, bool approve)
    {
        if (string.IsNullOrWhiteSpace(requestedTenantId))
        {
            return;
        }

        try
        {
            var tenant = await _tenantRepository.GetByTenantIdAsync(requestedTenantId);
            if (tenant == null || !tenant.Enabled)
            {
                return;
            }

            // Participant rather than TenantUser: the WebAPI console admits SysAdmin, TenantAdmin and
            // TenantUser, so granting TenantUser here would hand the console to everyone who ever
            // signed in to a tenant's own app.
            var roles = approve ? new[] { SystemRoles.TenantParticipant } : Array.Empty<string>();

            // Uses the stored casing so the membership matches how the tenant is recorded elsewhere.
            var added = await _userRepository.AddTenantRoleIfAbsentAsync(userId, tenant.TenantId, approve, roles);
            if (added)
            {
                _logger.LogInformation(
                    approve
                        ? "User {UserId} joined tenant {TenantId} as a participant"
                        : "User {UserId} requested access to tenant {TenantId} and is awaiting approval",
                    LogSanitizer.RedactUserId(userId), LogSanitizer.Sanitize(tenant.TenantId));
            }
        }
        catch (Exception ex)
        {
            // Visibility for admins is a convenience; failing to record it must not turn an ordinary
            // "not a member" refusal into an error.
            _logger.LogWarning(ex, "Could not record membership of {TenantId} for {UserId}",
                LogSanitizer.Sanitize(requestedTenantId), LogSanitizer.RedactUserId(userId));
        }
    }

    /// <summary>
    /// Whether the provider that authenticated this request is the one the stored record belongs to.
    ///
    /// Records that predate pinning carry no authority, so the first sign-in to reach one claims it.
    /// Existing clients therefore keep working untouched, and the record is protected from every
    /// later provider. A SysAdmin adopting a pin is logged as a warning rather than refused: there is
    /// no sign-in path that pins them ahead of time, so refusing would lock them out with no way back
    /// in, and the log is what makes an unexpected one visible.
    /// </summary>
    private async Task<bool> IsSameProviderAsync(User user, string providerAuthority)
    {
        if (!string.IsNullOrEmpty(user.ProviderAuthority))
        {
            if (string.Equals(user.ProviderAuthority, providerAuthority, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            _logger.LogWarning(
                "Rejecting token for {UserId}: subject is pinned to provider {Pinned} but was asserted by {Presented}",
                LogSanitizer.RedactUserId(user.UserId),
                LogSanitizer.Sanitize(user.ProviderAuthority),
                LogSanitizer.Sanitize(providerAuthority));
            return false;
        }

        var pinned = await _userRepository.PinProviderAuthorityIfUnsetAsync(user.UserId, providerAuthority);
        if (!string.Equals(pinned, providerAuthority, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Rejecting token for {UserId}: a concurrent sign-in pinned the subject to provider {Pinned}",
                LogSanitizer.RedactUserId(user.UserId), LogSanitizer.Sanitize(pinned));
            return false;
        }

        if (user.IsSysAdmin)
        {
            _logger.LogWarning("Pinned SysAdmin {UserId} to provider {Authority} on first use",
                LogSanitizer.RedactUserId(user.UserId), LogSanitizer.Sanitize(providerAuthority));
        }
        else
        {
            _logger.LogInformation("Pinned user {UserId} to provider {Authority} on first use",
                LogSanitizer.RedactUserId(user.UserId), LogSanitizer.Sanitize(providerAuthority));
        }

        return true;
    }

    /// <summary>
    /// Returns the enabled tenants the given user is an approved member of, without requiring the
    /// caller to already have a tenant context. Authentication handlers use this to verify that a
    /// caller-supplied tenant id actually belongs to the authenticated user.
    /// </summary>
    public async Task<ServiceResult<List<TenantInfoDto>>> GetApprovedTenantsForUserId(string userId)
    {
        try
        {
            if (string.IsNullOrEmpty(userId))
                return ServiceResult<List<TenantInfoDto>>.Unauthorized("User not authenticated");

            var user = await _userRepository.GetByUserIdAsync(userId);

            // Every sign-in door reaches its tenants through here, so this is where a disabled
            // account is turned away. Certificates were checking the flag and nothing else was, so
            // an account disabled pending review could still sign in and be handed its tenants.
            if (user != null && user.IsLockedOut)
            {
                _logger.LogWarning(
                    "Refusing sign-in for {UserId}: the account is disabled. {Reason}",
                    LogSanitizer.RedactUserId(userId),
                    LogSanitizer.Sanitize(user.LockedOutReason ?? "(no reason recorded)"));
                return ServiceResult<List<TenantInfoDto>>.Unauthorized("This account is disabled");
            }

            if (user != null && user.IsSysAdmin)
            {
                var allTenants = await _tenantRepository.GetAllAsync();
                var enabledTenants = allTenants
                    .Where(t => t.Enabled)
                    .Select(t => new TenantInfoDto { TenantId = t.TenantId, Name = t.Name })
                    .ToList();
                return ServiceResult<List<TenantInfoDto>>.Success(enabledTenants);
            }

            var tenants = await _userRepository.GetUserTenantsAsync(userId);

            return ServiceResult<List<TenantInfoDto>>.Success(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenants for user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<List<TenantInfoDto>>.InternalServerError("Error getting tenants for user");
        }
    }

    public async Task<ServiceResult<List<TenantInfoDto>>> GetTenantsForUser(string userId)
    {
        try
        {
            var validationResult = ValidateTenantAccess("get user tenant", null); // only sysadmin can access all user tenants
            if (!validationResult.IsSuccess)
                return ServiceResult<List<TenantInfoDto>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            var isSysAdmin = await _userRepository.IsSysAdmin(userId);

            if (isSysAdmin)
            {
                var allTenants = await _tenantRepository.GetAllAsync();
                var enabledTenants = allTenants
                    .Where(t => t.Enabled)
                    .Select(t => new TenantInfoDto { TenantId = t.TenantId, Name = t.Name })
                    .ToList();
                return ServiceResult<List<TenantInfoDto>>.Success(enabledTenants);
            }

            var tenants = await _userRepository.GetUserTenantsAsync(userId);
            return ServiceResult<List<TenantInfoDto>>.Success(tenants);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting tenants for user {UserId}", LogSanitizer.Sanitize(userId));
            return ServiceResult<List<TenantInfoDto>>.InternalServerError("Error getting tenants for user");
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
                    Roles = new List<string>(),
                    IsApproved = false
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
            _logger.LogError(ex, "Error adding tenant {TenantId} to user {UserId}", LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(userId));
            return ServiceResult<bool>.InternalServerError("Error adding tenant to user");
        }
    }

    public async Task<ServiceResult<bool>> AddTenantToUserIfExist(string email)
    {

        try
        {
            var validationResult = ValidateTenantAccess("add user tenant", _tenantContext.TenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            // This grants an approved membership, which names one person. An address can answer to
            // more than one account, so it is refused rather than resolved to whichever record came
            // back first — that would put a different account in the tenant than the admin meant.
            var lookup = EmailAccountLookup.From(await _userRepository.GetAllByUserEmailAsync(email));
            if (lookup.IsAmbiguous)
            {
                _logger.LogWarning(
                    "Refusing to add {Email} to tenant {TenantId}: it matches {Count} accounts",
                    LogSanitizer.RedactEmail(email), LogSanitizer.Sanitize(_tenantContext.TenantId),
                    lookup.MatchCount);
                return ServiceResult<bool>.Conflict(EmailAccountLookup.AmbiguousError);
            }

            var user = lookup.Account;
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");

            if (user.IsLockedOut)
            {
                _logger.LogWarning(
                    "Refusing to add disabled account {UserId} to tenant {TenantId}",
                    LogSanitizer.RedactUserId(user.UserId), LogSanitizer.Sanitize(_tenantContext.TenantId));
                return ServiceResult<bool>.Conflict(
                    "This account is disabled, so it cannot be given a tenant membership. Enable it first.");
            }

            var tenantEntry = user.TenantRoles.FirstOrDefault(tr => tr.Tenant == _tenantContext.TenantId);

            if(tenantEntry != null && tenantEntry.IsApproved)
            {
                return ServiceResult<bool>.Conflict("Tenant already assigned to user");
            }

            if (tenantEntry == null)
            {
                tenantEntry = new TenantRole
                {
                    Tenant = _tenantContext.TenantId,
                    Roles = new List<string> { SystemRoles.TenantUser},
                    IsApproved = true
                };
                user.TenantRoles.Add(tenantEntry);
            }

            var result = await _userRepository.UpdateAsync(user.UserId, user);
            return ServiceResult<bool>.Success(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding user with email {Email} to tenant {TenantId}",
                LogSanitizer.RedactEmail(email), LogSanitizer.Sanitize(_tenantContext.TenantId));
            return ServiceResult<bool>.InternalServerError("Error adding tenant to user with this email");
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
            _logger.LogError(ex, "Error removing tenant {TenantId} from user {UserId}", LogSanitizer.Sanitize(tenantId), LogSanitizer.Sanitize(userId));
            return ServiceResult<bool>.InternalServerError("Error removing tenant from user");
        }
    }

    public async Task<ServiceResult<List<User>>> GetUnapprovedUsers()
    {
        try
        {
            // Determine which tenant to query based on user role
            string? tenantIdToQuery = null;
            
            // System admin sees unapproved users for the current tenant (from X-Tenant-Id / organisation dropdown)
            if (_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                tenantIdToQuery = _tenantContext.TenantId;
            }
            // Tenant admin can only see unapproved users for their tenant
            else if (_tenantContext.UserRoles.Contains(SystemRoles.TenantAdmin))
            {
                var validationResult = ValidateTenantAccess("get unapproved users", _tenantContext.TenantId);
                if (!validationResult.IsSuccess)
                    return ServiceResult<List<User>>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);
                    
                tenantIdToQuery = _tenantContext.TenantId;
            }
            else
            {
                _logger.LogWarning("User {UserId} attempted to get unapproved users without proper permissions",
                    LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
                return ServiceResult<List<User>>.Forbidden("Insufficient permissions to view unapproved users");
            }
            
            var users = await _userRepository.GetUsersWithUnapprovedTenantAsync(tenantIdToQuery);
            return ServiceResult<List<User>>.Success(users);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unapproved users");
            return ServiceResult<List<User>>.InternalServerError("Error getting unapproved users");
        }
    }

    public async Task<ServiceResult<bool>> ApproveUser(string userId, string tenantId, bool approve)
    {
        try
        {
            var validationResult = ValidateTenantAccess("approve user", tenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            var user = await _userRepository.GetByUserIdAsync(userId);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");
            if (user.TenantRoles.Any(tr => tr.Tenant == tenantId))
            {
                if (user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId)?.IsApproved == true)
                {
                    _logger.LogWarning("User {UserId} already approved for tenant {TenantId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));
                    return ServiceResult<bool>.Conflict("User already approved for this tenant");
                }

                if (approve && user.TenantRoles.FirstOrDefault(tr => tr.Tenant == tenantId) is TenantRole tenantRole)
                {
                    tenantRole.IsApproved = true;
                    tenantRole.Roles = tenantRole.Roles.Count > 0 ? tenantRole.Roles : new List<string> { SystemRoles.TenantUser };
                }
                else
                {
                    user.TenantRoles.RemoveAll(tr => tr.Tenant == tenantId);
                }
            }
            else
            {
                var tenantEntry = new TenantRole
                {
                    Tenant = tenantId,
                    Roles = new List<string> { SystemRoles.TenantUser }, // Default role on approval
                    IsApproved = true
                };
                user.TenantRoles.Add(tenantEntry);
            }

            var result = await _userRepository.UpdateAsync(userId, user);
            return result
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.InternalServerError("Failed to approve user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving user {UserId} for tenant {TenantId}", LogSanitizer.Sanitize(userId), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("Error approving user");
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
                    LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(operation), LogSanitizer.Sanitize(tenantId));
                return ServiceResult.Failure("Tenant admins can only access their own tenant", StatusCode.Forbidden);
            }
            return ServiceResult.Success();
        }

        // Regular users need to be at least tenant admin
        _logger.LogWarning("User {UserId} attempted to {Operation} without proper permissions",
            LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(operation));
        return ServiceResult.Failure("Insufficient permissions to manage roles", StatusCode.Forbidden);
    }

    public async Task<ServiceResult<bool>> RequestToJoinTenant(string tenantId)
    {
        try
        {
            var user = await _userRepository.GetByUserIdAsync(_tenantContext.LoggedInUser);
            if (user == null)
                return ServiceResult<bool>.NotFound("User not found");
            if (user.TenantRoles.Any(tr => tr.Tenant == tenantId))
            {
                _logger.LogWarning("User {UserId} in tenant {TenantId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(tenantId));
                return ServiceResult<bool>.Conflict("User already in this tenant");
            }
            var tenantEntry = new TenantRole
            {
                Tenant = tenantId,
                Roles = new List<string> { SystemRoles.TenantUser },
                IsApproved = false
            };
            user.TenantRoles.Add(tenantEntry);
            var result = await _userRepository.UpdateAsync(user.Id, user);
            return result
                ? ServiceResult<bool>.Success(true)
                : ServiceResult<bool>.InternalServerError("Failed to approve user");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error approving user {UserId} for tenant {TenantId}", LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<bool>.InternalServerError("Error approving user");
        }
    }

    public async Task<ServiceResult<PagedUserResult>> GetTenantUsers(UserFilter filter)
    {
        var validationResult = ValidateTenantAccess("get tenant users", _tenantContext.TenantId);
        if (!validationResult.IsSuccess)
            return ServiceResult<PagedUserResult>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

        filter.Tenant = _tenantContext.TenantId;
        var users = await _userRepository.GetAllUsersByTenantAsync(filter);
        return ServiceResult<PagedUserResult>.Success(users);
    }

    public async Task<ServiceResult<bool>> UpdateTenantUser(EditUserDto user)
    {
        // Security: Validate tenant access
        var validationResult = ValidateTenantAccess("update tenant user", _tenantContext.TenantId);
        if (!validationResult.IsSuccess)
            return ServiceResult<bool>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

        var existingUser = await _userRepository.GetByUserIdAsync(user.UserId);
        if (existingUser == null)
        {
            return ServiceResult<bool>.NotFound("User not found");
        }

        // Security: Prevent modification of system admin status via tenant endpoint
        if (user.IsSysAdmin != existingUser.IsSysAdmin)
        {
            _logger.LogWarning("Attempt to modify system admin status for user {UserId} via tenant endpoint by {LoggedInUser}", 
                LogSanitizer.Sanitize(user.UserId), LogSanitizer.Sanitize(_tenantContext.LoggedInUser));
            return ServiceResult<bool>.Forbidden("Cannot modify system admin status via this endpoint");
        }

        // Security: Validate that only current tenant roles are being modified
        var rolesForOtherTenants = user.TenantRoles.Where(x => x.Tenant != _tenantContext.TenantId).ToList();
        if (rolesForOtherTenants.Any())
        {
            _logger.LogWarning("Attempt to modify roles for other tenants by user {LoggedInUser} in tenant {TenantId}", 
                LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(_tenantContext.TenantId));
            return ServiceResult<bool>.Forbidden("Can only modify roles for the current tenant");
        }

        // Update allowed user properties: Name, Email (do not change global lockout from this endpoint)
        existingUser.Email = user.Email;
        existingUser.Name = user.Name;

        // Tenant approval and roles for the current tenant only (from TenantRoles[].IsApproved)
        var currentTenantRoles = existingUser.TenantRoles.Where(x => x.Tenant == _tenantContext.TenantId).ToList();
        var currentTenantRoleDto = user.TenantRoles.FirstOrDefault(x => x.Tenant == _tenantContext.TenantId);

        if (currentTenantRoleDto != null)
        {
            var updatedRoles = currentTenantRoleDto.Roles ?? new List<string>();
            var isApprovedForTenant = currentTenantRoleDto.IsApproved;

            // Security: Prevent tenant admin from assigning system-wide roles
            var systemWideRoles = new[] { SystemRoles.SysAdmin };
            var hasSystemRoles = updatedRoles.Any(r => systemWideRoles.Contains(r));
            if (hasSystemRoles && !_tenantContext.UserRoles.Contains(SystemRoles.SysAdmin))
            {
                _logger.LogWarning("Attempt to assign system-wide roles by non-sysadmin user {LoggedInUser} in tenant {TenantId}",
                    LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(_tenantContext.TenantId));
                return ServiceResult<bool>.Forbidden("Cannot assign system-wide roles");
            }

            if (currentTenantRoles.Count > 0)
            {
                currentTenantRoles[0].Roles = updatedRoles;
                currentTenantRoles[0].IsApproved = isApprovedForTenant;
            }
            else
            {
                existingUser.TenantRoles.Add(new TenantRole
                {
                    Tenant = _tenantContext.TenantId,
                    Roles = updatedRoles,
                    IsApproved = isApprovedForTenant
                });
            }
        }

        await _userRepository.UpdateAsync(existingUser.UserId, existingUser);

        _logger.LogInformation("User {UserId} updated by {LoggedInUser} in tenant {TenantId}", 
            LogSanitizer.Sanitize(user.UserId), LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(_tenantContext.TenantId));

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<User>> CreateNewUserInTenant(CreateNewUserDto dto, string tenantId)
    {
        try
        {
            // Security: Validate tenant access
            var validationResult = ValidateTenantAccess("create user in tenant", tenantId);
            if (!validationResult.IsSuccess)
                return ServiceResult<User>.Forbidden(validationResult.ErrorMessage!, validationResult.StatusCode);

            // Validate input
            if (string.IsNullOrWhiteSpace(dto.Email))
                return ServiceResult<User>.BadRequest("Email is required and must be valid");

            if (string.IsNullOrWhiteSpace(dto.Name))
                return ServiceResult<User>.BadRequest("Name is required");

            if (dto.TenantRoles.Count == 0)
                return ServiceResult<User>.BadRequest("At least one tenant role is required");

            // Validate roles are from allowed list
            var allowedRoles = new[] { SystemRoles.TenantAdmin, SystemRoles.TenantUser, SystemRoles.TenantParticipant, SystemRoles.TenantParticipantAdmin };
            var invalidRoles = dto.TenantRoles.Where(r => !allowedRoles.Contains(r)).ToList();
            if (invalidRoles.Any())
                return ServiceResult<User>.BadRequest($"Invalid roles: {string.Join(", ", invalidRoles)}");

            // Check if user with this email already exists
            var existingUser = await _userRepository.GetByUserEmailAsync(dto.Email);
            if (existingUser != null)
                return ServiceResult<User>.Conflict("A user with this email already exists in the system.");

            // Generate unique userId
            var userId = Guid.NewGuid().ToString();

            // Create new user
            var newUser = new User
            {
                UserId = userId,
                Email = dto.Email,
                Name = dto.Name,
                IsSysAdmin = false,
                IsLockedOut = false,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                TenantRoles = new List<TenantRole>
                {
                    new TenantRole
                    {
                        Tenant = tenantId,
                        Roles = dto.TenantRoles,
                        IsApproved = true
                    }
                }
            };

            // Save to database
            var created = await _userRepository.CreateAsync(newUser);
            if (!created)
            {
                _logger.LogError("Failed to create user {Email} in database", LogSanitizer.RedactEmail(dto.Email));
                return ServiceResult<User>.InternalServerError("Failed to create user");
            }

            _logger.LogInformation("User {UserId} ({Email}) created by {CreatedBy} in tenant {TenantId}",
                LogSanitizer.Sanitize(userId), LogSanitizer.RedactEmail(dto.Email), LogSanitizer.Sanitize(_tenantContext.LoggedInUser), LogSanitizer.Sanitize(tenantId));

            // Retrieve the created user to return complete data
            var createdUser = await _userRepository.GetByUserIdAsync(userId);
            if (createdUser == null)
            {
                _logger.LogError("Created user {UserId} but failed to retrieve it", LogSanitizer.Sanitize(userId));
                return ServiceResult<User>.InternalServerError("User created but failed to retrieve");
            }

            return ServiceResult<User>.Success(createdUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating new user in tenant {TenantId}", LogSanitizer.Sanitize(tenantId));
            return ServiceResult<User>.InternalServerError("An error occurred while creating the new user");
        }
    }

    private async Task SyncSysAdminFromGroupClaimsAsync(string userId, string token)
    {
        var adminGroupIds = _configuration["Oidc:AdminGroupIds"];
        if (string.IsNullOrEmpty(adminGroupIds)) return;

        var configuredIds = adminGroupIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var tokenGroupIds = _jwtClaimsExtractor.ExtractClaims(token, "groups")
            .Concat(_jwtClaimsExtractor.ExtractClaims(token, "roles"));

        var isSysAdmin = tokenGroupIds.Any(id => configuredIds.Contains(id));

        // Nobody decides this per user — it follows from a group claim on every login — so without
        // the check a second account at another directory would be promoted merely for being in
        // that directory's admin group, which is the escalation this whole area exists to stop.
        // Accepting two accounts as the same person is an operator's decision, made through the
        // global user endpoint. Demotion is never blocked: taking the role away is always safe.
        if (isSysAdmin && await IsEmailSharedWithAnotherAccountAsync(userId))
        {
            _logger.LogError(
                "Not granting SysAdmin to {UserId} from group claims: another account holds the same " +
                "email. If they are the same person, grant it through global user administration",
                LogSanitizer.RedactUserId(userId));
            return;
        }

        await _userRepository.SetSysAdminAsync(userId, isSysAdmin);

        _logger.LogInformation("Azure AD group SysAdmin sync: user={UserId} isSysAdmin={IsSysAdmin}", userId, isSysAdmin);
    }

    private async Task<bool> IsEmailSharedWithAnotherAccountAsync(string userId)
    {
        var user = await _userRepository.GetByUserIdAsync(userId);
        if (user == null || string.IsNullOrWhiteSpace(user.Email))
        {
            return false;
        }

        return await _userRepository.IsEmailSharedAsync(user.Email, user.UserId);
    }

    /// <summary>
    /// Creates a user from a JWT token with proper validation using the centralized JWT utility.
    /// Returns null when the email already belongs to a different account — there is no record
    /// under this user id, so treating that conflict as success would leave the caller acting as
    /// an identity that was never written.
    /// SECURITY: Uses centralized JWT validation with JWKS before processing claims
    /// </summary>
    private async Task<UserDto?> createUserFromToken(string token)
    {
        // Validate and extract user information using the centralized JWT utility
        var jwtResult = await _jwtClaimsExtractor.ValidateAndExtractClaimsAsync(token);
        if (!jwtResult.IsValid || string.IsNullOrEmpty(jwtResult.UserId))
        {
            _logger.LogWarning("JWT token validation failed in createUserFromToken: {Error}", 
                LogSanitizer.Sanitize(jwtResult.ErrorMessage));
            throw new ArgumentException(jwtResult.ErrorMessage ?? "Invalid or expired token", nameof(token));
        }

        var newUser = new UserDto
        {
            UserId = jwtResult.UserId,
            Email = jwtResult.Email ?? string.Empty,
            Name = jwtResult.Name ?? string.Empty,
        };

        var createdUser = await _userManagementService.CreateNewUser(newUser);

        if (!createdUser.IsSuccess && createdUser.StatusCode == StatusCode.Conflict)
        {
            // "User already exists" is a genuine race — the record is there under this id.
            // "A user with this email already exists" is not: no record exists under this user id.
            var existingById = await _userRepository.GetByUserIdAsync(jwtResult.UserId);
            if (existingById != null)
            {
                _logger.LogInformation("User {UserId} already exists, returning existing user",
                    LogSanitizer.RedactUserId(jwtResult.UserId));
                return newUser;
            }

            _logger.LogWarning(
                "Refusing to provision portal user {UserId}: its email is already registered to a different account",
                LogSanitizer.RedactUserId(jwtResult.UserId));
            return null;
        }

        return createdUser.IsSuccess
            ? newUser
            : throw new Exception($"Failed to create user {jwtResult.UserId} from token. Error: {createdUser.ErrorMessage}");
    }

}