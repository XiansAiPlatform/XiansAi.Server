using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

public class UserTenantServiceApprovedTenantsTests
{
    private const string UserId = "provider-subject-abc123";
    private const string Authority = "https://login.example.com";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly Mock<IAuthMgtConnect> _authMgtConnect = new();
    private readonly Mock<IUserManagementService> _userManagementService = new();
    private readonly Mock<ITenantRepository> _tenantRepo = new();
    private readonly Mock<IJwtClaimsExtractor> _jwtExtractor = new();

    private UserTenantService BuildService() =>
        new(
            _userRepo.Object,
            NullLogger<UserTenantService>.Instance,
            _tenantContext.Object,
            _authMgtConnect.Object,
            new ConfigurationBuilder().Build(),
            _userManagementService.Object,
            _tenantRepo.Object,
            _jwtExtractor.Object);

    private static Tenant BuildTenant(string id, string tenantId, string name, bool enabled) =>
        new()
        {
            Id = id,
            TenantId = tenantId,
            Name = name,
            Enabled = enabled,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "test-user"
        };

    private static SignInIdentity IdentityFor(
        string userId = UserId,
        string? email = "user@example.com",
        string? name = "Test User",
        string? authority = Authority) =>
        new() { UserId = userId, Email = email, Name = name, ProviderAuthority = authority };

    /// <summary>The pending membership every caller but the UserApi sign-in path records.</summary>
    private void VerifyPendingMembershipRecorded(string tenantId, Times times) =>
        _userRepo.Verify(
            x => x.AddTenantRoleIfAbsentAsync(UserId, tenantId, false, It.Is<IReadOnlyList<string>>(r => r.Count == 0)),
            times);

    private void VerifyNoMembershipRecorded() =>
        _userRepo.Verify(
            x => x.AddTenantRoleIfAbsentAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<IReadOnlyList<string>>()),
            Times.Never);

    /// <summary>
    /// A concurrent request that creates the record first: the lookup finds nothing until the
    /// creation attempt loses the race, and finds it from then on. Modelled as state rather than a
    /// fixed sequence of answers, so it does not depend on how many times the record is read.
    /// </summary>
    private void ArrangeLostRaceToCreate(User racedUser)
    {
        var exists = false;
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(() => exists ? racedUser : null);
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .Callback(() => exists = true)
            .ReturnsAsync(ServiceResult<bool>.Conflict("User already exists"));
    }

    /// <summary>Lets an unpinned record adopt whichever authority is presented.</summary>
    private void AllowPinAdoption()
    {
        _userRepo
            .Setup(x => x.PinProviderAuthorityIfUnsetAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync((string _, string authority) => authority);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsAnEmptyUserId()
    {
        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(userId: string.Empty, email: null, name: null));

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsATokenWithNoProviderAuthority()
    {
        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null, authority: null));

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        _userRepo.Verify(x => x.GetUserTenantsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_ReturnsNoTenants_ForAFirstTimeUser()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor());

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!.Tenants);
    }

    /// <summary>
    /// A first-time user arriving with a tenant id, where that tenant exists and is enabled.
    /// </summary>
    private void ArrangeFirstTimeUserRequesting(string tenantId, bool tenantEnabled = true)
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync(tenantId))
            .ReturnsAsync(BuildTenant("id-1", tenantId, "Tenant", tenantEnabled));
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersAFirstTimeUserAsPendingOnTheTenantTheyAskedFor()
    {
        // Without this the user is provisioned but belongs to nothing, and the tenant's own admins
        // cannot see them at all — both of their listings match on a TenantRoles entry.
        ArrangeFirstTimeUserRequesting("acme");

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme");

        VerifyPendingMembershipRecorded("acme", Times.Once());
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PendingMembershipGrantsNoAccess()
    {
        ArrangeFirstTimeUserRequesting("acme");

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme");

        // Being registered as pending must not put the tenant in the approved list, or the caller
        // would let them straight in.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!.Tenants);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_UsesTheStoredCasingOfTheTenantId()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync("ACME"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Tenant", true));

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "ACME");

        VerifyPendingMembershipRecorded("acme", Times.Once());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task EnsureUserAndGetApprovedTenants_RegistersNothingWhenNoTenantWasRequested(string? tenantId)
    {
        ArrangeFirstTimeUserRequesting("acme");

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), tenantId);

        VerifyNoMembershipRecorded();
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersNothingForATenantThatDoesNotExist()
    {
        // The tenant id comes from the caller, so without this check anyone with a valid token could
        // append a row to their own record for every name they cared to try.
        ArrangeFirstTimeUserRequesting("acme");
        _tenantRepo.Setup(x => x.GetByTenantIdAsync("does-not-exist")).ReturnsAsync((Tenant?)null);

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "does-not-exist");

        VerifyNoMembershipRecorded();
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersNothingForADisabledTenant()
    {
        ArrangeFirstTimeUserRequesting("acme", tenantEnabled: false);

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme");

        VerifyNoMembershipRecorded();
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RegistersAnExistingUserReachingATenantTheyDoNotBelongTo()
    {
        // A known user meeting a new tenant is the same situation as a brand new one.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            Email = "user@example.com",
            Name = "Test User",
            ProviderAuthority = Authority,
            TenantRoles = new List<TenantRole>()
        });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync("acme"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Tenant", true));

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme");

        VerifyPendingMembershipRecorded("acme", Times.Once());
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_StillReturnsTenantsWhenRecordingThePendingMembershipFails()
    {
        // Visibility for admins is a convenience. Losing it must not turn an ordinary sign-in into
        // an error for a user who is already approved elsewhere.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            ProviderAuthority = Authority,
            TenantRoles = new List<TenantRole>()
        });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "other", Name = "Other" } });
        _tenantRepo
            .Setup(x => x.GetByTenantIdAsync("acme"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Tenant", true));
        _userRepo
            .Setup(x => x.AddTenantRoleIfAbsentAsync(
                UserId, "acme", It.IsAny<bool>(), It.IsAny<IReadOnlyList<string>>()))
            .ThrowsAsync(new Exception("mongo unavailable"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme");

        Assert.True(result.IsSuccess);
        Assert.Equal("other", Assert.Single(result.Data!.Tenants).TenantId);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RefusesASubjectWhoseEmailBelongsToAnotherAccount()
    {
        // The token proves the provider says this person's email is that string. It does not prove
        // they are the account already holding it, and that account may carry far more access —
        // so this is refused, never merged.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Conflict("A user with this email already exists"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: "taken@example.com"), "acme");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        VerifyNoMembershipRecorded();
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_StillHandlesALostRaceToCreateTheSameSubject()
    {
        // Same Conflict from the creator, but here the record really is this subject's, so the
        // sign-in continues rather than being mistaken for an email collision.
        ArrangeLostRaceToCreate(
            new User { UserId = UserId, ProviderAuthority = Authority, Email = "user@example.com" });
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "acme", Name = "Acme" } });
        _tenantRepo.Setup(x => x.GetByTenantIdAsync("acme"))
            .ReturnsAsync(BuildTenant("id-1", "acme", "Acme", true));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme");

        Assert.True(result.IsSuccess);
        Assert.Equal("acme", Assert.Single(result.Data!.Tenants).TenantId);
        Assert.Equal("user@example.com", result.Data.ConversationEmail);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_ProvisionsAFirstTimeUserWithoutTheSysAdminBootstrap()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor());

        _userManagementService.Verify(
            x => x.CreateNewUser(It.Is<UserDto>(u => u.UserId == UserId), false),
            Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PinsANewUserToTheAuthenticatingProvider()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.Success(true));

        await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor(email: null, name: null));

        _userManagementService.Verify(
            x => x.CreateNewUser(It.Is<UserDto>(u => u.ProviderAuthority == Authority), false),
            Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_DoesNotProvisionAnExistingUser()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = Authority });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());

        await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor(email: null, name: null));

        _userManagementService.Verify(
            x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_ToleratesAConcurrentProvisioningConflict()
    {
        ArrangeLostRaceToCreate(new User { UserId = UserId, ProviderAuthority = Authority });
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "tenant-a", Name = "Tenant A" } });

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null));

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", Assert.Single(result.Data!.Tenants).TenantId);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_FailsWhenProvisioningFails()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync((User?)null);
        _userManagementService
            .Setup(x => x.CreateNewUser(It.IsAny<UserDto>(), It.IsAny<bool>()))
            .ReturnsAsync(ServiceResult<bool>.InternalServerError("database unavailable"));

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null));

        Assert.False(result.IsSuccess);
        _userRepo.Verify(x => x.GetUserTenantsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsASubjectAssertedByADifferentProvider()
    {
        // The same subject from a provider the user is not registered with is a different person.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = Authority });

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null, authority: "https://evil.example"));

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        _userRepo.Verify(x => x.GetUserTenantsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_MatchesThePinnedProviderCaseInsensitively()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = "https://Login.Example.com" });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null));

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PinsAnUnpinnedRecordOnFirstUse()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = null });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());
        AllowPinAdoption();

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null));

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.PinProviderAuthorityIfUnsetAsync(UserId, Authority), Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsTheLoserOfAConcurrentFirstUsePin()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = null });
        _userRepo
            .Setup(x => x.PinProviderAuthorityIfUnsetAsync(UserId, It.IsAny<string>()))
            .ReturnsAsync("https://someone-else.example");

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null));

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_PinsAnUnpinnedSysAdminOnFirstUse()
    {
        // Nothing pins a SysAdmin ahead of time, so refusing adoption would lock them out for good.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = null, IsSysAdmin = true });
        _tenantRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<Tenant>());
        AllowPinAdoption();

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null));

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.PinProviderAuthorityIfUnsetAsync(UserId, Authority), Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RejectsASysAdminAssertedByADifferentProvider_OncePinned()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, ProviderAuthority = Authority, IsSysAdmin = true });

        var result = await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(email: null, name: null, authority: "https://evil.example"));

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_CreatesAnApprovedParticipantMembership_WhenTheCallerAsksForOne()
    {
        // The UserApi path validated this token against the rules the tenant configured for its own
        // identity provider, so holding one is the tenant's own statement that this person belongs
        // to it — there is nothing further for an admin to decide.
        ArrangeFirstTimeUserRequesting("acme");

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme", approveNewMembership: true);

        _userRepo.Verify(
            x => x.AddTenantRoleIfAbsentAsync(UserId, "acme", true,
                It.Is<IReadOnlyList<string>>(r => r.Single() == SystemRoles.TenantParticipant)),
            Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_GrantsParticipant_NotTenantUser()
    {
        // TenantUser would admit them to the WebAPI console, which accepts SysAdmin, TenantAdmin and
        // TenantUser. UserApi authorizes on approved membership rather than on a role.
        ArrangeFirstTimeUserRequesting("acme");

        await BuildService().EnsureUserAndGetApprovedTenants(
            IdentityFor(), "acme", approveNewMembership: true);

        _userRepo.Verify(
            x => x.AddTenantRoleIfAbsentAsync(UserId, "acme", It.IsAny<bool>(),
                It.Is<IReadOnlyList<string>>(r => !r.Contains(SystemRoles.TenantUser))),
            Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_FillsInAnEmailAndNameTheRecordIsMissing()
    {
        // Records provisioned before the address was stored have neither, and nothing else ever
        // revisits them — the create path only runs on a first sign-in.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            ProviderAuthority = Authority,
            Email = string.Empty,
            Name = string.Empty
        });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());

        await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor());

        _userRepo.Verify(
            x => x.UpdateProfileFieldsAsync(UserId, "user@example.com", "Test User"),
            Times.Once);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_DoesNotOverwriteAnEmailTheRecordAlreadyHas()
    {
        // An admin may have set it deliberately, so a token claim must never replace it.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            ProviderAuthority = Authority,
            Email = "set-by-an-admin@example.com",
            Name = "Set By An Admin"
        });
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());

        await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor());

        _userRepo.Verify(
            x => x.UpdateProfileFieldsAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()),
            Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_WithholdsASharedEmailFromTheConversationIdentity()
    {
        // The caller uses this as the participant id, which is the namespace their message threads
        // live in. Two accounts sharing one would let either read the other's conversations.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            ProviderAuthority = Authority,
            Email = "shared@example.com",
            Name = "Test User"
        });
        _userRepo.Setup(x => x.IsEmailSharedAsync("shared@example.com", UserId)).ReturnsAsync(true);
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId)).ReturnsAsync(new List<TenantInfoDto>());

        var result = await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor());

        Assert.True(result.IsSuccess);
        Assert.Null(result.Data!.ConversationEmail);

        // Still reported as the account's address, so the caller stays recognisable when they name
        // themselves by it — it just does not become the namespace.
        Assert.Equal("shared@example.com", result.Data.AccountEmail);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_ReturnsOnlyTheTenantsTheUserIsApprovedFor()
    {
        _userRepo.Setup(x => x.IsSysAdmin(UserId)).ReturnsAsync(false);
        _userRepo.Setup(x => x.GetUserTenantsAsync(UserId))
            .ReturnsAsync(new List<TenantInfoDto> { new() { TenantId = "tenant-a", Name = "Tenant A" } });

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", Assert.Single(result.Data!).TenantId);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_ReturnsAllEnabledTenants_ForASysAdmin()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, IsSysAdmin = true });
        _tenantRepo.Setup(x => x.GetAllAsync()).ReturnsAsync(new List<Tenant>
        {
            BuildTenant("000000000000000000000001", "tenant-a", "Tenant A", enabled: true),
            BuildTenant("000000000000000000000002", "tenant-disabled", "Disabled", enabled: false)
        });

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("tenant-a", Assert.Single(result.Data!).TenantId);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_FailsClosed_WhenTheLookupThrows()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ThrowsAsync(new InvalidOperationException("mongo down"));

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.False(result.IsSuccess);
        Assert.Null(result.Data);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_RefusesADisabledAccount()
    {
        // Every sign-in door reaches its tenants through here. Only certificates were checking the
        // flag, so an account disabled pending review could sign in everywhere else.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            IsLockedOut = true,
            LockedOutReason = "Created disabled for review"
        });

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        _userRepo.Verify(x => x.GetUserTenantsAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetApprovedTenantsForUserId_RefusesADisabledSysAdmin()
    {
        // Being a system administrator does not survive the account being turned off, or disabling
        // one would leave them with every tenant.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId))
            .ReturnsAsync(new User { UserId = UserId, IsSysAdmin = true, IsLockedOut = true });

        var result = await BuildService().GetApprovedTenantsForUserId(UserId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
        _tenantRepo.Verify(x => x.GetAllAsync(), Times.Never);
    }

    [Fact]
    public async Task EnsureUserAndGetApprovedTenants_RefusesADisabledAccountAsUnauthorized()
    {
        // Not as a server error: the account being disabled is an answer, not a breakage.
        _userRepo.Setup(x => x.GetByUserIdAsync(UserId)).ReturnsAsync(new User
        {
            UserId = UserId,
            ProviderAuthority = Authority,
            Email = "user@example.com",
            Name = "Test User",
            IsLockedOut = true
        });

        var result = await BuildService().EnsureUserAndGetApprovedTenants(IdentityFor());

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Unauthorized, result.StatusCode);
    }
}
