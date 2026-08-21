using Features.UserApi.Utils;
using Moq;
using Shared.Auth;

namespace Tests.UnitTests.Features.UserApi.Utils;

public class ParticipantIdResolverTests
{
    private const string ProviderUserId = "abc123";
    private const string CanonicalUserId = "keycloak|abc123";

    private static ITenantContext TenantContext(
        string loggedInUser,
        string participantId,
        UserType userType = UserType.UserToken,
        string? email = null,
        string? providerSubject = null)
    {
        var context = new Mock<ITenantContext>();
        context.Setup(x => x.LoggedInUser).Returns(loggedInUser);
        context.Setup(x => x.ParticipantId).Returns(participantId);
        context.Setup(x => x.UserType).Returns(userType);
        context.Setup(x => x.Email).Returns(email);
        context.Setup(x => x.ProviderSubject).Returns(providerSubject);
        return context.Object;
    }

    [Fact]
    public void Resolve_PrefersAnExplicitlySuppliedParticipantId()
    {
        var resolved = ParticipantIdResolver.Resolve("someone-else", TenantContext(CanonicalUserId, ProviderUserId));

        Assert.Equal("someone-else", resolved.ParticipantId);
    }

    [Fact]
    public void Resolve_DoesNotOfferALegacyFallback_ForAnExplicitParticipantId()
    {
        var resolved = ParticipantIdResolver.Resolve("someone-else", TenantContext(CanonicalUserId, ProviderUserId));

        Assert.Null(resolved.LegacyParticipantId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Resolve_DefaultsToTheCallersOwnParticipantId(string? requested)
    {
        var resolved = ParticipantIdResolver.Resolve(requested, TenantContext(CanonicalUserId, ProviderUserId));

        Assert.Equal(ProviderUserId, resolved.ParticipantId);
    }

    [Fact]
    public void Resolve_OffersTheCanonicalLoginIdAsTheLegacyFallback_WhenDefaulted()
    {
        var resolved = ParticipantIdResolver.Resolve(null, TenantContext(CanonicalUserId, ProviderUserId));

        Assert.Equal(CanonicalUserId, resolved.LegacyParticipantId);
    }

    [Fact]
    public void Resolve_OffersNoFallback_WhenTheParticipantAndLoginIdsAlreadyMatch()
    {
        // The API key flows, where the participant id is the key's creator.
        var resolved = ParticipantIdResolver.Resolve(null, TenantContext("service-account", "service-account"));

        Assert.Equal("service-account", resolved.ParticipantId);
        Assert.Null(resolved.LegacyParticipantId);
    }

    [Fact]
    public void Resolve_LowercasesAnExplicitParticipantId()
    {
        var resolved = ParticipantIdResolver.Resolve("User@Example.COM", TenantContext(CanonicalUserId, ProviderUserId));

        Assert.Equal("user@example.com", resolved.ParticipantId);
    }

    [Fact]
    public void Resolve_LowercasesBothTheDefaultedAndLegacyIds()
    {
        var resolved = ParticipantIdResolver.Resolve(null, TenantContext("Keycloak|ABC123", "ABC123"));

        Assert.Equal("abc123", resolved.ParticipantId);
        Assert.Equal("keycloak|abc123", resolved.LegacyParticipantId);
    }

    [Fact]
    public void Resolve_OffersNoFallback_WhenCasingIsTheOnlyDifference()
    {
        var resolved = ParticipantIdResolver.Resolve(null, TenantContext("ABC123", "abc123"));

        Assert.Null(resolved.LegacyParticipantId);
    }

    [Fact]
    public void Resolve_StillOffersTheLegacyFallback_WhenTheCallerNamesTheirOwnIdExplicitly()
    {
        var resolved = ParticipantIdResolver.Resolve(ProviderUserId, TenantContext(CanonicalUserId, ProviderUserId));

        Assert.Equal(ProviderUserId, resolved.ParticipantId);
        Assert.Equal(CanonicalUserId, resolved.LegacyParticipantId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void TryResolve_AllowsADefaultedParticipantId(string? requested)
    {
        var allowed = ParticipantIdResolver.TryResolve(
            requested, TenantContext(CanonicalUserId, ProviderUserId), out var participant);

        Assert.True(allowed);
        Assert.Equal(ProviderUserId, participant.ParticipantId);
    }

    [Theory]
    [InlineData(ProviderUserId)]
    [InlineData("ABC123")]
    [InlineData(CanonicalUserId)]
    public void TryResolve_AllowsATokenHolderToNameTheirOwnId(string requested)
    {
        var allowed = ParticipantIdResolver.TryResolve(
            requested, TenantContext(CanonicalUserId, ProviderUserId), out _);

        Assert.True(allowed);
    }

    [Theory]
    [InlineData("user@example.com")]
    [InlineData("User@Example.COM")]
    public void TryResolve_AllowsATokenHolderToNameTheirAccountEmail(string requested)
    {
        var allowed = ParticipantIdResolver.TryResolve(
            requested,
            TenantContext(CanonicalUserId, ProviderUserId, email: "user@example.com", providerSubject: ProviderUserId),
            out _);

        Assert.True(allowed);
    }

    [Fact]
    public void TryResolve_AllowsATokenHolderToNameTheirProviderSubject_WhenParticipantPrefersEmail()
    {
        var allowed = ParticipantIdResolver.TryResolve(
            ProviderUserId,
            TenantContext(
                CanonicalUserId,
                participantId: "user@example.com",
                email: "user@example.com",
                providerSubject: ProviderUserId),
            out _);

        Assert.True(allowed);
    }

    [Fact]
    public void TryResolve_RejectsATokenHolderNamingSomeoneElse()
    {
        var allowed = ParticipantIdResolver.TryResolve(
            "someone-else", TenantContext(CanonicalUserId, ProviderUserId), out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_RejectsATokenHolderNamingAnotherEmail()
    {
        var allowed = ParticipantIdResolver.TryResolve(
            "other@example.com",
            TenantContext(CanonicalUserId, ProviderUserId, email: "user@example.com"),
            out _);

        Assert.False(allowed);
    }

    [Fact]
    public void TryResolve_AllowsAnApiKeyToNameAnyParticipant()
    {
        var tenantContext = TenantContext("service-account", "service-account", UserType.UserApiKey);

        var allowed = ParticipantIdResolver.TryResolve("any-customer", tenantContext, out var participant);

        Assert.True(allowed);
        Assert.Equal("any-customer", participant.ParticipantId);
    }

    [Theory]
    [InlineData(UserType.UserToken)]
    [InlineData(UserType.Unknown)]
    [InlineData(UserType.DevToken)]
    public void CanActAs_OnlyExemptsApiKeys(UserType userType)
    {
        var tenantContext = TenantContext(CanonicalUserId, ProviderUserId, userType);

        Assert.False(ParticipantIdResolver.CanActAs("someone-else", tenantContext));
    }

    [Fact]
    public void CanActAs_RejectsAnEmptyParticipantId_ForATokenHolder()
    {
        Assert.False(ParticipantIdResolver.CanActAs("", TenantContext(CanonicalUserId, ProviderUserId)));
    }

    /// <summary>
    /// The address is held by a second account, so it namespaces nothing and the caller was issued
    /// their provider subject instead. Naming themselves by it must still land on their own threads.
    /// </summary>
    private static ITenantContext WithheldEmailContext() =>
        TenantContext(
            CanonicalUserId,
            participantId: ProviderUserId,
            email: "shared@example.com",
            providerSubject: ProviderUserId);

    [Theory]
    [InlineData("shared@example.com")]
    [InlineData("Shared@Example.COM")]
    public void Resolve_ResolvesAWithheldEmailToTheCallersOwnParticipantId(string requested)
    {
        var resolved = ParticipantIdResolver.Resolve(requested, WithheldEmailContext());

        Assert.Equal(ProviderUserId, resolved.ParticipantId);
    }

    [Fact]
    public void TryResolve_AllowsAWithheldEmail_WithoutPuttingTheCallerInTheSharedNamespace()
    {
        var allowed = ParticipantIdResolver.TryResolve(
            "shared@example.com", WithheldEmailContext(), out var participant);

        Assert.True(allowed);
        Assert.Equal(ProviderUserId, participant.ParticipantId);
    }

    [Fact]
    public void Resolve_KeepsTheEmailAsTheNamespace_WhenItNamesOnlyTheCaller()
    {
        var context = TenantContext(
            CanonicalUserId,
            participantId: "user@example.com",
            email: "user@example.com",
            providerSubject: ProviderUserId);

        var resolved = ParticipantIdResolver.Resolve("user@example.com", context);

        Assert.Equal("user@example.com", resolved.ParticipantId);
    }

    [Fact]
    public void Resolve_ResolvesTheCanonicalLoginIdToTheCallersOwnParticipantId()
    {
        var resolved = ParticipantIdResolver.Resolve(
            CanonicalUserId, TenantContext(CanonicalUserId, ProviderUserId));

        Assert.Equal(ProviderUserId, resolved.ParticipantId);
        Assert.Equal(CanonicalUserId, resolved.LegacyParticipantId);
    }

    [Fact]
    public void Resolve_LeavesAnApiKeyNamingAnEndUserAlone()
    {
        var tenantContext = TenantContext("service-account", "service-account", UserType.UserApiKey);

        var resolved = ParticipantIdResolver.Resolve("shared@example.com", tenantContext);

        Assert.Equal("shared@example.com", resolved.ParticipantId);
    }
}
