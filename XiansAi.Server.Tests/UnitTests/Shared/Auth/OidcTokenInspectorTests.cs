using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shared.Auth;
using Shared.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Auth;

/// <summary>
/// Covers how a token becomes an identity.
///
/// Which claim the subject is read from decides which user record a sign-in lands on, so a change
/// here either orphans existing accounts or lets one user resolve to another's record. These tests
/// pin down both the current lenient behaviour and what strict mode changes about it.
/// </summary>
public class OidcTokenInspectorTests
{
    private static JsonWebToken TokenWith(params (string Claim, object Value)[] claims)
    {
        var payload = new JwtPayload();
        foreach (var (claim, value) in claims)
        {
            payload[claim] = value;
        }

        var jwt = new JwtSecurityToken(new JwtHeader(), payload);
        return new JsonWebToken(new JwtSecurityTokenHandler().WriteToken(jwt));
    }

    private static OidcProviderRule RuleWith(params (string Key, object Value)[] settings)
    {
        return new OidcProviderRule
        {
            ProviderSpecificSettings = settings.Length == 0
                ? null
                : settings.ToDictionary(s => s.Key, s => s.Value)
        };
    }

    [Fact]
    public void SubjectComesFromSubWhenPresent()
    {
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(), TokenWith(("sub", "user-1"), ("email", "a@b.com")), allowFallbackClaims: true);

        Assert.Equal("user-1", subject.Value);
        Assert.True(subject.IsStableClaim);
    }

    [Fact]
    public void EntraObjectIdIsTreatedAsStable()
    {
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(), TokenWith(("oid", "object-1"), ("email", "a@b.com")), allowFallbackClaims: true);

        Assert.Equal("object-1", subject.Value);
        Assert.True(subject.IsStableClaim);
    }

    [Fact]
    public void FallbackClaimIsUsedButReportedAsUnstable()
    {
        // Email is not a stable identifier: a user who can change it at their provider can change
        // which record they resolve to here. Accepted for compatibility, but flagged so the caller
        // can warn about the tenant.
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(), TokenWith(("email", "a@b.com")), allowFallbackClaims: true);

        Assert.Equal("a@b.com", subject.Value);
        Assert.False(subject.IsStableClaim);
        Assert.Equal("email", subject.ClaimType);
    }

    [Fact]
    public void StrictModeRefusesToFallBackToAMutableClaim()
    {
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(), TokenWith(("email", "a@b.com"), ("name", "A B")), allowFallbackClaims: false);

        Assert.Null(subject.Value);
    }

    [Fact]
    public void StrictModeStillAcceptsAProviderNominatedClaim()
    {
        // Naming the claim explicitly is the escape hatch: a tenant that has always identified users
        // by email can keep doing so under strict mode by saying so, rather than by accident.
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(("userIdClaim", "email")), TokenWith(("email", "a@b.com")), allowFallbackClaims: false);

        Assert.Equal("a@b.com", subject.Value);
        Assert.True(subject.IsStableClaim);
    }

    [Fact]
    public void ProviderNominatedClaimWinsOverSub()
    {
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(("userIdClaim", "employee_id")),
            TokenWith(("sub", "user-1"), ("employee_id", "E42")),
            allowFallbackClaims: true);

        Assert.Equal("E42", subject.Value);
    }

    [Fact]
    public void ProviderMayNominateSeveralClaimsInPreferenceOrder()
    {
        var rule = RuleWith(("userIdClaims", "employee_id, sub"));

        Assert.Equal("E42", OidcTokenInspector.ResolveSubject(
            rule, TokenWith(("sub", "user-1"), ("employee_id", "E42")), allowFallbackClaims: true).Value);

        Assert.Equal("user-1", OidcTokenInspector.ResolveSubject(
            rule, TokenWith(("sub", "user-1")), allowFallbackClaims: true).Value);
    }

    [Fact]
    public void MissingSubjectIsReportedRatherThanGuessed()
    {
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(), TokenWith(("aud", "api")), allowFallbackClaims: true);

        Assert.Null(subject.Value);
    }

    [Fact]
    public void EmailAndNameAreReadFromTheUsualClaims()
    {
        var jwt = TokenWith(("sub", "user-1"), ("email", "a@b.com"), ("name", "A B"));

        Assert.Equal("a@b.com", OidcTokenInspector.GetEmail(jwt));
        Assert.Equal("A B", OidcTokenInspector.GetName(jwt));
    }

    [Fact]
    public void PreferredUsernameStandsInForAMissingEmail()
    {
        Assert.Equal("a@b.com", OidcTokenInspector.GetEmail(TokenWith(("preferred_username", "a@b.com"))));
    }

    [Fact]
    public void EmailIsReadFromTheB2CEmailsArray()
    {
        // Azure AD B2C issues no email, upn or preferred_username. Missing this claim leaves the
        // record with no address, which cannot be matched to an existing account and so quietly
        // becomes a second one for the same person.
        var jwt = TokenWith(("sub", "user-1"), ("emails", new[] { "a@b.com" }));

        Assert.Equal("a@b.com", OidcTokenInspector.GetEmail(jwt));
    }

    [Fact]
    public void TheB2CEmailsArrayIsNotTreatedAsVerified()
    {
        // B2C states nothing about ownership of the address, and it may have come from a social
        // account the directory never checked. Treating it as verified would let a sign-in attach
        // itself to an existing account on that basis alone.
        var jwt = TokenWith(("sub", "user-1"), ("emails", new[] { "a@b.com" }));

        Assert.False(OidcTokenInspector.IsEmailVerified(jwt));
    }

    [Fact]
    public void NoScopeRequirementAcceptsAnyToken()
    {
        Assert.Null(OidcTokenInspector.DescribeMissingScope(new OidcProviderRule(), TokenWith(("sub", "u"))));
    }

    [Fact]
    public void RequiredScopeIsMatchedAgainstEitherScopeClaimName()
    {
        var rule = new OidcProviderRule { Scope = "api.read" };

        Assert.Null(OidcTokenInspector.DescribeMissingScope(rule, TokenWith(("scope", "openid api.read"))));
        Assert.Null(OidcTokenInspector.DescribeMissingScope(rule, TokenWith(("scp", "openid api.read"))));
    }

    [Fact]
    public void MissingRequiredScopeIsReported()
    {
        var rule = new OidcProviderRule { Scope = "api.write" };

        Assert.NotNull(OidcTokenInspector.DescribeMissingScope(rule, TokenWith(("scope", "openid api.read"))));
        Assert.NotNull(OidcTokenInspector.DescribeMissingScope(rule, TokenWith(("sub", "u"))));
    }

    [Fact]
    public void ScopeMatchIsWholeValueNotSubstring()
    {
        // 'api.read' must not be satisfied by 'api.readonly', which is a different grant.
        var rule = new OidcProviderRule { Scope = "api.read" };

        Assert.NotNull(OidcTokenInspector.DescribeMissingScope(rule, TokenWith(("scope", "api.readonly"))));
    }

    [Fact]
    public void CustomClaimCheckIsApplied()
    {
        var rule = new OidcProviderRule
        {
            AdditionalClaims = new List<CustomClaimCheck>
            {
                new() { Claim = "tid", Op = "equals", Value = "expected-tenant" }
            }
        };

        Assert.Null(OidcTokenInspector.DescribeFailedClaimCheck(rule, TokenWith(("tid", "expected-tenant"))));
        Assert.NotNull(OidcTokenInspector.DescribeFailedClaimCheck(rule, TokenWith(("tid", "other-tenant"))));
        Assert.NotNull(OidcTokenInspector.DescribeFailedClaimCheck(rule, TokenWith(("sub", "u"))));
    }

    [Fact]
    public void ExpiryIsReadFromTheTokenSoCachingCannotOutliveIt()
    {
        var expires = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds();

        var expiresAt = OidcTokenInspector.ExpiresAt(TokenWith(("sub", "u"), ("exp", expires)));

        Assert.NotNull(expiresAt);
        Assert.Equal(expires, expiresAt!.Value.ToUnixTimeSeconds());
    }

    [Fact]
    public void TokenWithoutExpiryReportsNoExpiry()
    {
        Assert.Null(OidcTokenInspector.ExpiresAt(TokenWith(("sub", "u"))));
    }

    [Fact]
    public void NameIdentifierIsStillHonouredAsAFallback()
    {
        // Long-standing behaviour for providers that map their subject onto the SOAP-style claim URI.
        var subject = OidcTokenInspector.ResolveSubject(
            RuleWith(), TokenWith((ClaimTypes.NameIdentifier, "user-1")), allowFallbackClaims: true);

        Assert.Equal("user-1", subject.Value);
    }
}
