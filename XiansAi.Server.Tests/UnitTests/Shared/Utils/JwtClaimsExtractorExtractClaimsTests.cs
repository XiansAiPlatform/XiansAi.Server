using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Shared.Providers.Auth;
using Shared.Utils;

namespace Tests.UnitTests.Shared.Utils;

public class JwtClaimsExtractorExtractClaimsTests
{
    private readonly JwtClaimsExtractor _extractor;

    public JwtClaimsExtractorExtractClaimsTests()
    {
        var factory = new Mock<IAuthProviderFactory>();
        _extractor = new JwtClaimsExtractor(factory.Object, NullLogger<JwtClaimsExtractor>.Instance);
    }

    [Fact]
    public void ExtractClaims_ReturnsAllValues_ForMultiValueClaim()
    {
        var token = BuildToken(
            new Claim("groups", "group-id-1"),
            new Claim("groups", "group-id-2"),
            new Claim("groups", "group-id-3"));

        var result = _extractor.ExtractClaims(token, "groups").ToList();

        Assert.Equal(3, result.Count);
        Assert.Contains("group-id-1", result);
        Assert.Contains("group-id-2", result);
        Assert.Contains("group-id-3", result);
    }

    [Fact]
    public void ExtractClaims_ReturnsEmpty_WhenClaimTypeNotPresent()
    {
        var token = BuildToken(new Claim("sub", "user-123"));

        var result = _extractor.ExtractClaims(token, "groups").ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractClaims_ReturnsEmpty_ForInvalidToken()
    {
        var result = _extractor.ExtractClaims("not.a.valid.jwt.token", "groups").ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractClaims_ReturnsSingleValue_WhenOnlyOneClaim()
    {
        var token = BuildToken(new Claim("groups", "only-group-id"));

        var result = _extractor.ExtractClaims(token, "groups").ToList();

        Assert.Single(result);
        Assert.Equal("only-group-id", result[0]);
    }

    [Fact]
    public void ExtractClaims_DoesNotReturnOtherClaimTypes()
    {
        var token = BuildToken(
            new Claim("groups", "group-id-1"),
            new Claim("roles", "role-id-1"));

        var groups = _extractor.ExtractClaims(token, "groups").ToList();
        var roles = _extractor.ExtractClaims(token, "roles").ToList();

        Assert.Single(groups);
        Assert.Equal("group-id-1", groups[0]);
        Assert.Single(roles);
        Assert.Equal("role-id-1", roles[0]);
    }

    [Fact]
    public void ExtractClaims_ReturnsEmpty_WhenTokenIsEmpty()
    {
        var result = _extractor.ExtractClaims(string.Empty, "groups").ToList();

        Assert.Empty(result);
    }

    private static string BuildToken(params Claim[] claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("test-signing-key-32-chars-minimum!"));
        var token = new JwtSecurityToken(
            claims: claims,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
