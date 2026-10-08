using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Shared.Auth;
using Shared.Services;
using Xunit;

namespace XiansAi.Server.Tests.UnitTests.Shared.Auth;

public class EmailVerificationEvaluatorTests
{
    private const string TenantGuid = "11111111-2222-3333-4444-555555555555";

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

    private static EmailVerificationRule Require(params (string Claim, object Value)[] verifyClaims) => new()
    {
        VerifyClaims = verifyClaims.Select(c => new EmailVerificationClaim { Claim = c.Claim, Value = c.Value }).ToList()
    };

    [Fact]
    public void NoRule_AdmitsTheTokenEmail()
    {
        var result = EmailVerificationEvaluator.Evaluate(null, TokenWith(("email", "a@b.com")));

        Assert.True(result.Admitted);
        Assert.Equal("a@b.com", result.Email);
    }

    [Fact]
    public void AllowUnverifiedEmail_AdmitsWithoutChecks()
    {
        var rule = Require(("xms_edov", true));
        rule.AllowUnverifiedEmail = true;

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com")));

        Assert.True(result.Admitted);
        Assert.Equal("a@b.com", result.Email);
    }

    [Fact]
    public void VerifyClaims_AdmitsWhenAllMatch_AndNormalizesTheEmail()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("xms_edov", true)), TokenWith(("email", " A@B.com "), ("xms_edov", true)));

        Assert.True(result.Admitted);
        Assert.Equal("a@b.com", result.Email);
    }

    [Fact]
    public void VerifyClaims_RefusesWhenClaimIsMissing()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("xms_edov", true)), TokenWith(("email", "a@b.com")));

        Assert.False(result.Admitted);
        Assert.Null(result.Email);
        Assert.Contains("xms_edov", result.Reason);
    }

    [Fact]
    public void VerifyClaims_RefusesWhenClaimIsFalse()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("email_verified", true)), TokenWith(("email", "a@b.com"), ("email_verified", false)));

        Assert.False(result.Admitted);
    }

    [Fact]
    public void VerifyClaims_RequiresEveryEntryToMatch()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("email_verified", true), ("acr", "mfa")),
            TokenWith(("email", "a@b.com"), ("email_verified", true), ("acr", "pwd")));

        Assert.False(result.Admitted);
        Assert.Contains("acr", result.Reason);
    }

    [Theory]
    [InlineData("true")]
    [InlineData("1")]
    [InlineData("TRUE")]
    public void VerifyClaims_AcceptsStringSpellingsOfTrue(string tokenValue)
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("email_verified", "true")), TokenWith(("email", "a@b.com"), ("email_verified", tokenValue)));

        Assert.True(result.Admitted);
    }

    [Fact]
    public void VerifyClaims_AcceptsValueParsedFromJson()
    {
        var rule = JsonSerializer.Deserialize<EmailVerificationRule>(
            """{ "verifyClaims": [{ "claim": "xms_edov", "value": true }] }""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com"), ("xms_edov", true)));

        Assert.True(result.Admitted);
    }

    [Fact]
    public void VerifyClaims_ComparesNonBooleanValuesExactly()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("acr", "1")), TokenWith(("email", "a@b.com"), ("acr", "true")));

        Assert.False(result.Admitted);
    }

    [Fact]
    public void VerifyClaims_MatchesAnyElementOfAnArrayClaim()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("amr", "mfa")), TokenWith(("email", "a@b.com"), ("amr", new[] { "pwd", "mfa" })));

        Assert.True(result.Admitted);
    }

    [Fact]
    public void VerifyClaims_IgnoreUpnAndPreferredUsernameAsTheEmail()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("xms_edov", true)),
            TokenWith(("upn", "a@b.com"), ("preferred_username", "a@b.com"), ("xms_edov", true)));

        Assert.False(result.Admitted);
        Assert.Equal("no email claim in token", result.Reason);
    }

    [Fact]
    public void VerifyClaims_ReadB2CEmailsArray()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            Require(("email_verified", true)),
            TokenWith(("emails", new[] { "first@b.com", "second@b.com" }), ("email_verified", true)));

        Assert.True(result.Admitted);
        Assert.Equal("first@b.com", result.Email);
    }

    [Fact]
    public void TrustedClaim_AdmitsCaseInsensitively_WithTheTokenEmail()
    {
        var rule = new EmailVerificationRule { TrustedClaim = "tid", TrustedValues = [TenantGuid.ToUpperInvariant()] };

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com"), ("tid", TenantGuid)));

        Assert.True(result.Admitted);
        Assert.Equal("a@b.com", result.Email);
    }

    [Fact]
    public void TrustedClaim_RefusesAnUntrustedValue()
    {
        var rule = new EmailVerificationRule { TrustedClaim = "tid", TrustedValues = [TenantGuid] };

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com"), ("tid", "other-tenant")));

        Assert.False(result.Admitted);
        Assert.Equal("tid not trusted", result.Reason);
    }

    [Fact]
    public void TrustedClaim_AdmitsWhenVerifyClaimsFail()
    {
        var rule = Require(("xms_edov", true));
        rule.TrustedClaim = "tid";
        rule.TrustedValues = [TenantGuid];

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com"), ("tid", TenantGuid)));

        Assert.True(result.Admitted);
        Assert.Equal("trusted tid", result.Reason);
    }

    [Fact]
    public void BothPathsFailing_ExplainsBoth()
    {
        var rule = Require(("xms_edov", true));
        rule.TrustedClaim = "tid";
        rule.TrustedValues = [TenantGuid];

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com"), ("tid", "other")));

        Assert.False(result.Admitted);
        Assert.Equal("email not verified and tid not trusted", result.Reason);
    }

    [Fact]
    public void EmptyRule_RefusesAsInvalidConfig()
    {
        var result = EmailVerificationEvaluator.Evaluate(
            new EmailVerificationRule(), TokenWith(("email", "a@b.com"), ("email_verified", true)));

        Assert.False(result.Admitted);
        Assert.StartsWith("invalid emailVerification config", result.Reason);
    }

    [Fact]
    public void TrustedClaimWithoutValues_RefusesAsInvalidConfig()
    {
        var rule = new EmailVerificationRule { TrustedClaim = "tid", TrustedValues = [" "] };

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com"), ("tid", TenantGuid)));

        Assert.False(result.Admitted);
        Assert.StartsWith("invalid emailVerification config", result.Reason);
    }

    [Fact]
    public void VerifyClaimWithoutValue_RefusesAsInvalidConfig()
    {
        var rule = new EmailVerificationRule
        {
            VerifyClaims = [new EmailVerificationClaim { Claim = "xms_edov", Value = null }]
        };

        var result = EmailVerificationEvaluator.Evaluate(rule, TokenWith(("email", "a@b.com"), ("xms_edov", true)));

        Assert.False(result.Admitted);
        Assert.StartsWith("invalid emailVerification config", result.Reason);
    }
}
