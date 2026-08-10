using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;

namespace Tests.UnitTests.Shared.Auth;

/// <summary>
/// Pins the assumption <see cref="Shared.Auth.DynamicOidcValidator"/> relies on: it parses the
/// token once to read the issuer and then hands that already-parsed token to
/// <see cref="JsonWebTokenHandler.ValidateTokenAsync(SecurityToken, TokenValidationParameters)"/>,
/// which must still verify the signature rather than trusting the parse.
/// </summary>
public class JsonWebTokenHandlerContractTests
{
    private static readonly JsonWebTokenHandler Handler = new();

    private static readonly SymmetricSecurityKey SigningKey =
        new(Encoding.UTF8.GetBytes("a-signing-key-long-enough-for-hmac-sha256-abcdef"));

    private static readonly SymmetricSecurityKey OtherKey =
        new(Encoding.UTF8.GetBytes("a-different-key-long-enough-for-hmac-sha256-1234"));

    private static string CreateToken()
    {
        return Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = "https://login.example.com",
            Audience = "xians-api",
            Subject = new ClaimsIdentity([new Claim("sub", "provider-subject-abc123")]),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256)
        });
    }

    private static TokenValidationParameters ParametersFor(SecurityKey key) => new()
    {
        ValidateIssuerSigningKey = true,
        RequireSignedTokens = true,
        ValidateIssuer = true,
        ValidIssuer = "https://login.example.com",
        ValidateAudience = true,
        ValidAudiences = ["xians-api"],
        ValidateLifetime = true,
        RequireExpirationTime = true,
        IssuerSigningKeys = [key]
    };

    [Fact]
    public async Task ValidatingAPreParsedToken_SucceedsAgainstTheCorrectSigningKey()
    {
        var parsed = Handler.ReadJsonWebToken(CreateToken());

        var result = await Handler.ValidateTokenAsync(parsed, ParametersFor(SigningKey));

        Assert.True(result.IsValid);
        Assert.Equal("https://login.example.com", parsed.Issuer);
        Assert.True(parsed.TryGetClaim("sub", out var subject));
        Assert.Equal("provider-subject-abc123", subject.Value);
    }

    [Fact]
    public async Task ValidatingAPreParsedToken_FailsAgainstTheWrongSigningKey()
    {
        var parsed = Handler.ReadJsonWebToken(CreateToken());

        var result = await Handler.ValidateTokenAsync(parsed, ParametersFor(OtherKey));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidatingAPreParsedToken_FailsWhenThePayloadWasTamperedWith()
    {
        var parts = CreateToken().Split('.');
        var forgedPayload = Base64UrlEncoder.Encode(
            """{"iss":"https://login.example.com","aud":"xians-api","sub":"someone-else","exp":4102444800}""");
        var parsed = Handler.ReadJsonWebToken($"{parts[0]}.{forgedPayload}.{parts[2]}");

        var result = await Handler.ValidateTokenAsync(parsed, ParametersFor(SigningKey));

        Assert.False(result.IsValid);
    }
}
