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
    public void ExtractClaims_ReturnsOnlyMatchingValues_ForMultiValueClaim()
    {
        var token = BuildToken(
            new Claim("groups", "group-id-1"),
            new Claim("groups", "group-id-2"),
            new Claim("roles", "role-id-1"));

        var groups = _extractor.ExtractClaims(token, "groups").ToList();

        Assert.Equal(2, groups.Count);
        Assert.Contains("group-id-1", groups);
        Assert.Contains("group-id-2", groups);
        Assert.DoesNotContain("role-id-1", groups);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not.a.valid.jwt.token")]
    public void ExtractClaims_ReturnsEmpty_ForEmptyOrInvalidToken(string token)
    {
        var result = _extractor.ExtractClaims(token, "groups").ToList();

        Assert.Empty(result);
    }

    [Fact]
    public void ExtractClaims_ReturnsEmpty_WhenClaimTypeMissing()
    {
        var token = BuildToken(new Claim("sub", "user-123"));

        var result = _extractor.ExtractClaims(token, "groups").ToList();

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
