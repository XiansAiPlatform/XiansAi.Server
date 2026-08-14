using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Providers.Auth;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Shared.Utils.Services;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// The two paths where an admin names a person by email and that person is given a tenant
/// membership.
///
/// An address does not always name one account — two providers can each hold a record for the same
/// address — so resolving it to whichever record came back first would put a different account in
/// the tenant than the admin meant. Both paths refuse an ambiguous address instead.
/// </summary>
public class TenantAssignmentByEmailTests
{
    private const string TenantId = "acme";
    private const string Email = "shared@example.com";

    private readonly Mock<IUserRepository> _userRepo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();

    public TenantAssignmentByEmailTests()
    {
        _userRepo.Setup(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>())).ReturnsAsync(true);
        _tenantContext.Setup(x => x.LoggedInUser).Returns("the-admin");
        _tenantContext.Setup(x => x.TenantId).Returns(TenantId);
        _tenantContext.Setup(x => x.UserRoles).Returns(new[] { SystemRoles.TenantAdmin });
    }

    private void EmailIsHeldBy(params User[] owners)
    {
        _userRepo.Setup(x => x.GetAllByUserEmailAsync(Email)).ReturnsAsync(owners.ToList());
    }

    private void AccountExists(User user)
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(user.UserId)).ReturnsAsync(user);
    }

    private static User Account(string userId, bool isLockedOut = false) => new()
    {
        UserId = userId,
        Email = Email,
        Name = "Test User",
        IsLockedOut = isLockedOut,
        TenantRoles = new List<TenantRole>()
    };

    // The shape this exists for: one person signed in through two directories.
    private static User[] TwoAccountsForOnePerson() =>
        new[] { Account("directory-a-subject"), Account("directory-b-subject") };

    private UserTenantService BuildUserTenantService() => new(
        _userRepo.Object,
        NullLogger<UserTenantService>.Instance,
        _tenantContext.Object,
        Mock.Of<IAuthMgtConnect>(),
        new ConfigurationBuilder().Build(),
        Mock.Of<IUserManagementService>(),
        Mock.Of<ITenantRepository>(),
        Mock.Of<IJwtClaimsExtractor>());

    private TenantParticipantUserService BuildParticipantService() => new(
        _userRepo.Object,
        Mock.Of<IUserTenantService>(),
        Mock.Of<IRoleCacheService>(),
        Mock.Of<ITokenValidationCache>(),
        Mock.Of<IWebhookEventPublisher>(),
        NullLogger<TenantParticipantUserService>.Instance);

    [Fact]
    public async Task AddTenantToUserIfExist_RefusesAnAddressHeldByMoreThanOneAccount()
    {
        EmailIsHeldBy(TwoAccountsForOnePerson());

        var result = await BuildUserTenantService().AddTenantToUserIfExist(Email);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task AddTenantToUserIfExist_AddsTheMembership_WhenTheAddressNamesOneAccount()
    {
        EmailIsHeldBy(Account("the-only-subject"));

        var result = await BuildUserTenantService().AddTenantToUserIfExist(Email);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(
            x => x.UpdateAsync("the-only-subject", It.Is<User>(u =>
                u.TenantRoles.Any(tr => tr.Tenant == TenantId && tr.IsApproved))),
            Times.Once);
    }

    [Fact]
    public async Task AddTenantToUserIfExist_RefusesADisabledAccount()
    {
        EmailIsHeldBy(Account("the-only-subject", isLockedOut: true));

        var result = await BuildUserTenantService().AddTenantToUserIfExist(Email);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task AddTenantToUserIfExist_ReportsAnAddressNobodyHolds()
    {
        EmailIsHeldBy();

        var result = await BuildUserTenantService().AddTenantToUserIfExist(Email);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task ParticipantCreate_AddsTheRole_WhenTheAddressNamesOneAccount()
    {
        // One account holding the address settles which is meant, so there is nothing for a user
        // id to disambiguate: an operator sent to find one would search by this same address and
        // arrive at this same record.
        EmailIsHeldBy(Account("the-only-subject"));

        var result = await BuildParticipantService()
            .CreateAsync(TenantId, Email, "Test User", SystemRoles.TenantParticipant);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(
            x => x.UpdateAsync("the-only-subject", It.Is<User>(u =>
                u.TenantRoles.Any(tr =>
                    tr.Tenant == TenantId && tr.Roles.Contains(SystemRoles.TenantParticipant)))),
            Times.Once);
    }

    [Fact]
    public async Task ParticipantCreate_AddsAnExistingAccountWithoutRequiringAName()
    {
        // The name on the record stands; only a new account needs one supplied.
        EmailIsHeldBy(Account("the-only-subject"));

        var result = await BuildParticipantService()
            .CreateAsync(TenantId, Email, name: null, SystemRoles.TenantParticipant);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ParticipantCreate_RefusesAnAddressHeldByMoreThanOneAccount()
    {
        // The role granted here can be TenantAdmin, so landing on the wrong record matters more.
        EmailIsHeldBy(TwoAccountsForOnePerson());

        var result = await BuildParticipantService()
            .CreateAsync(TenantId, Email, "Test User", SystemRoles.TenantAdmin);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ParticipantCreate_RefusesADisabledAccountFoundByAddress()
    {
        EmailIsHeldBy(Account("the-only-subject", isLockedOut: true));

        var result = await BuildParticipantService()
            .CreateAsync(TenantId, Email, "Test User", SystemRoles.TenantParticipant);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task ParticipantCreate_AddsTheRole_ToTheAccountNamedByUserId()
    {
        AccountExists(Account("the-only-subject"));

        var result = await BuildParticipantService().CreateAsync(
            TenantId, email: null, name: null, SystemRoles.TenantParticipant,
            userId: "the-only-subject");

        Assert.True(result.IsSuccess);
        _userRepo.Verify(
            x => x.UpdateAsync("the-only-subject", It.Is<User>(u =>
                u.TenantRoles.Any(tr =>
                    tr.Tenant == TenantId && tr.Roles.Contains(SystemRoles.TenantParticipant)))),
            Times.Once);
    }

    [Fact]
    public async Task ParticipantCreate_RefusesAnAddressInPlaceOfAUserId()
    {
        var result = await BuildParticipantService().CreateAsync(
            TenantId, email: null, name: null, SystemRoles.TenantParticipant, userId: Email);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _userRepo.Verify(x => x.GetAllByUserEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task ParticipantCreate_ReportsAUserIdNobodyHolds()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync("no-such-subject")).ReturnsAsync((User?)null);

        var result = await BuildParticipantService().CreateAsync(
            TenantId, email: null, name: null, SystemRoles.TenantParticipant,
            userId: "no-such-subject");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task ParticipantCreate_RefusesADisabledAccount()
    {
        AccountExists(Account("the-only-subject", isLockedOut: true));

        var result = await BuildParticipantService().CreateAsync(
            TenantId, email: null, name: null, SystemRoles.TenantParticipant,
            userId: "the-only-subject");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<User>()), Times.Never);
    }
}
