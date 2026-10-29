using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

public class PrivacySubjectResolverTests
{
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IConversationRepository> _conversations = new();
    private readonly Mock<IAppIntegrationRepository> _integrations = new();
    private readonly List<User> _accounts = [];
    private readonly List<string> _defaultParticipantIds = [];

    private IReadOnlyCollection<string>? _workflowLookupTenants;
    private IReadOnlyCollection<string>? _workflowLookupParticipants;

    public PrivacySubjectResolverTests()
    {
        _users.Setup(u => u.GetByUserIdAsync(It.IsAny<string>()))
            .ReturnsAsync((string id) => _accounts.SingleOrDefault(a => a.UserId == id));
        _users.Setup(u => u.GetAllByUserEmailAsync(It.IsAny<string>()))
            .ReturnsAsync((string email) => _accounts.Where(a => a.Email == email.ToLowerInvariant()).ToList());

        _conversations
            .Setup(c => c.GetWorkflowIdsForParticipantsAsync(
                It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<IReadOnlyCollection<string>>()))
            .Callback<IReadOnlyCollection<string>, IReadOnlyCollection<string>>((tenants, participants) =>
            {
                _workflowLookupTenants = tenants;
                _workflowLookupParticipants = participants;
            })
            .ReturnsAsync(["workflow-1"]);

        _integrations.Setup(i => i.GetDefaultParticipantIdsAsync(It.IsAny<IReadOnlyCollection<string>>()))
            .ReturnsAsync(() => _defaultParticipantIds.ToList());
    }

    private PrivacySubjectResolver CreateResolver() => new(_users.Object, _tenants.Object, _conversations.Object, _integrations.Object);

    private User AddAccount(string userId, string email, params string[] tenants)
    {
        var account = new User
        {
            UserId = userId,
            Email = email,
            CreatedAt = DateTime.UtcNow.AddMinutes(_accounts.Count),
            TenantRoles = tenants.Select(t => new TenantRole { Tenant = t, Roles = ["TenantUser"], IsApproved = true }).ToList()
        };
        _accounts.Add(account);
        return account;
    }

    private Task<PrivacySubject> ResolveAsync(PrivacySubjectKind kind, string value, PrivacyScope scope) =>
        CreateResolver().ResolveAsync(new PrivacySubjectInput { Kind = kind, Value = value }, scope, CancellationToken.None);

    private static PrivacyScope Tenants(params string[] tenantIds) => new() { TenantIds = tenantIds.ToList() };

    [Fact]
    public async Task ByUserId_ResolvesEmailProviderSubjectAndLegacyId()
    {
        AddAccount("keycloak|ABC-123", "alice@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "keycloak|ABC-123", Tenants("t1"));

        Assert.Equal(["keycloak|ABC-123"], subject.UserIds);
        Assert.Equal(["alice@example.com"], subject.Emails);
        Assert.Equal(["abc-123", "alice@example.com", "keycloak|abc-123"], subject.ParticipantIds);
        Assert.Equal(["t1"], subject.MembershipTenantIds);
        Assert.Equal(["workflow-1"], subject.WorkflowIds);
        Assert.True(subject.HasAccount);
    }

    [Fact]
    public async Task ProviderSubject_SplitsAtFirstSeparator()
    {
        AddAccount("auth0-provider|auth0|xyz", "", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "auth0-provider|auth0|xyz", Tenants("t1"));

        Assert.Contains("auth0|xyz", subject.ParticipantIds);
    }

    [Fact]
    public async Task UniqueEmail_StillIncludesTheProviderSubject()
    {
        // Threads written while the email was shared live under the subject, even after it stops being shared.
        AddAccount("keycloak|abc", "alice@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "keycloak|abc", Tenants("t1"));

        Assert.Contains("abc", subject.ParticipantIds);
    }

    [Fact]
    public async Task NoEmail_AddsTheProviderSubject()
    {
        AddAccount("keycloak|abc", "", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "keycloak|abc", Tenants("t1"));

        Assert.Equal(["abc", "keycloak|abc"], subject.ParticipantIds);
        Assert.Empty(subject.Emails);
    }

    [Fact]
    public async Task GuidUserId_HasNoProviderSubject()
    {
        AddAccount("6f1c2b0e-guid", "carol@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "6f1c2b0e-guid", Tenants("t1"));

        Assert.Equal(["6f1c2b0e-guid", "carol@example.com"], subject.ParticipantIds);
    }

    [Fact]
    public async Task ByEmail_ResolvesTheAccount()
    {
        AddAccount("keycloak|abc", "alice@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.Email, "Alice@Example.com", Tenants("t1"));

        Assert.Equal(["keycloak|abc"], subject.UserIds);
    }

    [Fact]
    public async Task ParticipantIdEqualToEmail_ResolvesTheAccount()
    {
        AddAccount("keycloak|abc", "alice@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.ParticipantId, "alice@example.com", Tenants("t1"));

        Assert.Equal(["keycloak|abc"], subject.UserIds);
    }

    [Theory]
    [InlineData("webhook")]
    [InlineData("Unknown")]
    public async Task BuiltInSharedParticipantId_IsRejected(string value)
    {
        await Assert.ThrowsAsync<PrivacyRequestException>(() =>
            ResolveAsync(PrivacySubjectKind.ParticipantId, value, Tenants("t1")));
    }

    [Fact]
    public async Task ConfiguredDefaultParticipantId_IsRejected()
    {
        _defaultParticipantIds.Add("support-bot");

        var error = await Assert.ThrowsAsync<PrivacyRequestException>(() =>
            ResolveAsync(PrivacySubjectKind.ParticipantId, "Support-Bot", Tenants("t1")));

        Assert.DoesNotContain("support-bot", error.Message, StringComparison.OrdinalIgnoreCase);
        _integrations.Verify(i => i.GetDefaultParticipantIdsAsync(
            It.Is<IReadOnlyCollection<string>>(t => t.SequenceEqual(new[] { "t1" }))));
    }

    [Fact]
    public async Task AccountWhoseEmailIsAConfiguredDefault_IsRejected()
    {
        AddAccount("keycloak|abc", "shared@example.com", "t1");
        _defaultParticipantIds.Add("shared@example.com");

        await Assert.ThrowsAsync<PrivacyRequestException>(() =>
            ResolveAsync(PrivacySubjectKind.UserId, "keycloak|abc", Tenants("t1")));
    }

    [Fact]
    public async Task ParticipantIdThatIsNotAnEmail_IsBareEvenIfItIsAProviderSubject()
    {
        AddAccount("keycloak|abc", "alice@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.ParticipantId, "abc", Tenants("t1"));

        Assert.False(subject.HasAccount);
        Assert.Equal(["abc"], subject.ParticipantIds);
        _users.Verify(u => u.GetAllByUserEmailAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SharedEmail_ByEmail_ResolvesEveryAccountAndKeepsTheEmail()
    {
        AddAccount("google|g1", "shared@example.com", "t1");
        AddAccount("entra|e1", "shared@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.Email, "shared@example.com", Tenants("t1"));

        Assert.Equal(["google|g1", "entra|e1"], subject.UserIds);
        Assert.Equal(["shared@example.com"], subject.Emails);
        Assert.Contains("shared@example.com", subject.ParticipantIds);
        Assert.Contains("g1", subject.ParticipantIds);
        Assert.Contains("e1", subject.ParticipantIds);
    }

    [Fact]
    public async Task SharedEmail_ByEmail_LooksUpTheHoldersOnce()
    {
        AddAccount("google|g1", "shared@example.com", "t1");
        AddAccount("entra|e1", "shared@example.com", "t1");

        await ResolveAsync(PrivacySubjectKind.Email, "shared@example.com", Tenants("t1"));

        _users.Verify(u => u.GetAllByUserEmailAsync(It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task SharedEmail_ByUserId_LeavesOutTheEmailTheOtherAccountHolds()
    {
        AddAccount("google|g1", "shared@example.com", "t1");
        AddAccount("entra|e1", "shared@example.com", "t1");

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "google|g1", Tenants("t1"));

        Assert.Equal(["google|g1"], subject.UserIds);
        Assert.Empty(subject.Emails);
        Assert.Equal(["g1", "google|g1"], subject.ParticipantIds);
    }

    [Theory]
    [InlineData(PrivacySubjectKind.ParticipantId, "U024BE7LH", new[] { "u024be7lh" }, new string[0])]
    [InlineData(PrivacySubjectKind.Email, "Guest@Example.com", new[] { "guest@example.com" }, new[] { "guest@example.com" })]
    [InlineData(PrivacySubjectKind.UserId, "unknown|user", new[] { "unknown|user" }, new string[0])]
    public async Task NoAccount_UsesTheInputAsABareParticipantId(
        PrivacySubjectKind kind, string value, string[] participantIds, string[] emails)
    {
        var subject = await ResolveAsync(kind, value, Tenants("t1"));

        Assert.False(subject.HasAccount);
        Assert.Equal(participantIds, subject.ParticipantIds);
        Assert.Equal(emails, subject.Emails);
        Assert.Equal(["t1"], subject.TenantIds);
        Assert.Empty(subject.MembershipTenantIds);
    }

    [Fact]
    public async Task TenantScope_NeverReachesOtherTenants()
    {
        AddAccount("keycloak|abc", "alice@example.com", "t1", "t2");

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "keycloak|abc", Tenants("t1"));

        Assert.Equal(["t1"], subject.TenantIds);
        Assert.Equal(["t1"], subject.MembershipTenantIds);
        Assert.Equal(["t1"], _workflowLookupTenants);
        _tenants.Verify(t => t.GetAllTenantIdsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task AllTenants_SearchesEveryTenantOnThePlatform()
    {
        AddAccount("keycloak|abc", "alice@example.com", "t1");
        _tenants.Setup(t => t.GetAllTenantIdsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(["t1", "t2", "t3"]);

        var subject = await ResolveAsync(PrivacySubjectKind.UserId, "keycloak|abc", new PrivacyScope { AllTenants = true });

        Assert.Equal(["t1", "t2", "t3"], subject.TenantIds.Order());
        Assert.Equal(["t1"], subject.MembershipTenantIds);
    }

    [Fact]
    public async Task WorkflowIds_AreLookedUpForEveryParticipantId()
    {
        AddAccount("keycloak|abc", "alice@example.com", "t1");

        await ResolveAsync(PrivacySubjectKind.UserId, "keycloak|abc", Tenants("t1"));

        Assert.Equal(["abc", "alice@example.com", "keycloak|abc"], _workflowLookupParticipants);
    }

    [Fact]
    public async Task ScopeWithNoTenant_Throws()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ResolveAsync(PrivacySubjectKind.Email, "alice@example.com", Tenants()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public async Task BlankInput_Throws(string? value)
    {
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            CreateResolver().ResolveAsync(
                new PrivacySubjectInput { Kind = PrivacySubjectKind.Email, Value = value }, Tenants("t1"), CancellationToken.None));
    }
}
