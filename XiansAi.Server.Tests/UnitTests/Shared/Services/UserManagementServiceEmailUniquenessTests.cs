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
/// The single creation path everything funnels through, and where it is decided whether an address
/// may be held twice.
///
/// One person can legitimately hold an account at two identity providers, so an address shared
/// across providers is allowed. Two records under the same provider are not, since there the
/// address really does name one account. An address a system administrator holds is neither: the
/// record is created disabled, so the person is not turned away at the door and the role is not
/// taken from the account that has it, until somebody decides which of those should happen.
/// </summary>
public class UserManagementServiceEmailUniquenessTests
{
    private const string Provider = "https://login.example.com";
    private const string OtherProvider = "https://b2c.example.com";

    private readonly Mock<IUserRepository> _userRepo = new();

    private UserManagementService BuildService()
    {
        return new UserManagementService(
            _userRepo.Object,
            Mock.Of<ITenantContext>(),
            Mock.Of<IAuthMgtConnect>(),
            new ConfigurationBuilder().Build(),
            Mock.Of<IInvitationRepository>(),
            Mock.Of<IEmailService>(),
            Mock.Of<IJwtClaimsExtractor>(),
            Mock.Of<ITokenValidationCache>(),
            Mock.Of<IUserAuthorizationInvalidator>(),
            NullLogger<UserManagementService>.Instance);
    }

    private void ArrangeCreatable()
    {
        _userRepo.Setup(x => x.GetByUserIdAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
        _userRepo.Setup(x => x.GetAllByUserEmailAsync(It.IsAny<string>())).ReturnsAsync(new List<User>());
        _userRepo.Setup(x => x.GetAnyUserAsync()).ReturnsAsync(new User { UserId = "someone-else" });
        _userRepo.Setup(x => x.CreateAsync(It.IsAny<User>())).ReturnsAsync(true);
    }

    private void EmailIsHeldBy(string email, params User[] owners)
    {
        _userRepo.Setup(x => x.GetAllByUserEmailAsync(email)).ReturnsAsync(owners.ToList());
    }

    private Task<ServiceResult<bool>> Create(string email, string? providerAuthority = Provider) =>
        BuildService().CreateNewUser(new UserDto
        {
            UserId = "google-subject-123",
            Email = email,
            Name = "Test User",
            ProviderAuthority = providerAuthority
        });

    [Fact]
    public async Task CreateNewUser_RefusesAnEmailAlreadyHeldAtTheSameProvider()
    {
        ArrangeCreatable();
        EmailIsHeldBy("taken@example.com", new User
        {
            UserId = "some-other-subject",
            Email = "taken@example.com",
            ProviderAuthority = Provider
        });

        var result = await Create("taken@example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewUser_AcceptsAnEmailHeldAtADifferentProvider()
    {
        // The case this exists for: the same person signing in through a second directory. Their
        // two records resolve as one identity where a credential names only the address.
        ArrangeCreatable();
        EmailIsHeldBy("shared@example.com", new User
        {
            UserId = "some-other-subject",
            Email = "shared@example.com",
            ProviderAuthority = OtherProvider
        });

        var result = await Create("shared@example.com");

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.CreateAsync(It.Is<User>(u => u.UserId == "google-subject-123")), Times.Once);
    }

    /// <summary>An administrator at another directory already holding the address.</summary>
    private void EmailIsHeldByASystemAdministrator(string email)
    {
        EmailIsHeldBy(email, new User
        {
            UserId = "the-admin",
            Email = email,
            ProviderAuthority = OtherProvider,
            IsSysAdmin = true
        });
    }

    [Fact]
    public async Task CreateNewUser_CreatesADisabledRecord_WhenASystemAdministratorHoldsTheEmail()
    {
        // Refusing would leave a real person at a second directory unable to sign in at all, with
        // no way through that does not involve deleting somebody's account. Creating it enabled
        // would take SysAdmin off the account that has it. Disabled is neither, and waits.
        ArrangeCreatable();
        EmailIsHeldByASystemAdministrator("admin@example.com");

        var result = await Create("admin@example.com");

        Assert.True(result.IsSuccess);
        _userRepo.Verify(
            x => x.CreateAsync(It.Is<User>(u => u.IsLockedOut && !u.IsSysAdmin)),
            Times.Once);
    }

    [Fact]
    public async Task CreateNewUser_SaysWhyTheDisabledRecordIsDisabled()
    {
        // The consequence of enabling it lands on a different account than the one being enabled,
        // so it has to be written down where whoever enables it will read it.
        ArrangeCreatable();
        EmailIsHeldByASystemAdministrator("admin@example.com");

        await Create("admin@example.com");

        _userRepo.Verify(
            x => x.CreateAsync(It.Is<User>(u =>
                u.LockedOutReason != null
                && u.LockedOutReason.Contains("the-admin")
                && u.LockedOutAt != null)),
            Times.Once);
    }

    [Fact]
    public async Task CreateNewUser_StillRefusesASystemAdministratorsEmailAtTheSameProvider()
    {
        // Within one directory the address really does name one account, so this is a duplicate
        // rather than a second account for the same person, and there is nothing to review.
        ArrangeCreatable();
        EmailIsHeldBy("admin@example.com", new User
        {
            UserId = "the-admin",
            Email = "admin@example.com",
            ProviderAuthority = Provider,
            IsSysAdmin = true
        });

        var result = await Create("admin@example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _userRepo.Verify(x => x.CreateAsync(It.IsAny<User>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewUser_LeavesAnOrdinaryRecordEnabled()
    {
        ArrangeCreatable();

        await Create("fresh@example.com");

        _userRepo.Verify(
            x => x.CreateAsync(It.Is<User>(u => !u.IsLockedOut && u.LockedOutReason == null)),
            Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task CreateNewUser_RefusesWhenEitherSidesProviderIsUnknown(string? incomingAuthority)
    {
        // Records that cannot be told apart have to be assumed to be the same account, or an
        // unpinned record would be a way around the rule.
        ArrangeCreatable();
        EmailIsHeldBy("taken@example.com", new User
        {
            UserId = "some-other-subject",
            Email = "taken@example.com",
            ProviderAuthority = Provider
        });

        var result = await Create("taken@example.com", incomingAuthority);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
    }

    [Fact]
    public async Task CreateNewUser_RefusesWhenTheExistingRecordHasNoPinnedProvider()
    {
        ArrangeCreatable();
        EmailIsHeldBy("taken@example.com", new User
        {
            UserId = "legacy-subject",
            Email = "taken@example.com",
            ProviderAuthority = null
        });

        var result = await Create("taken@example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
    }

    [Fact]
    public async Task CreateNewUser_AcceptsAnUnusedEmail()
    {
        ArrangeCreatable();

        var result = await Create("fresh@example.com");

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.CreateAsync(It.Is<User>(u => u.UserId == "google-subject-123")), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateNewUser_DoesNotCompareRecordsThatHaveNoEmail(string email)
    {
        // Comparing blank emails would make every record with one collide with the first, which
        // would refuse creation for all of them.
        ArrangeCreatable();

        var result = await Create(email);

        Assert.True(result.IsSuccess);
        _userRepo.Verify(x => x.GetAllByUserEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task CreateNewUser_StillRefusesAUserIdThatAlreadyExists()
    {
        ArrangeCreatable();
        _userRepo.Setup(x => x.GetByUserIdAsync("google-subject-123"))
            .ReturnsAsync(new User { UserId = "google-subject-123" });

        var result = await Create("fresh@example.com");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
    }
}
