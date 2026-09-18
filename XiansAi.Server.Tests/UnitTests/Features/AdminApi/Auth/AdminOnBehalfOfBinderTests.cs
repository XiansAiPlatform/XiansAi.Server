using Features.AdminApi.Auth;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Repositories;

namespace Tests.UnitTests.Features.AdminApi.Auth;

public class AdminOnBehalfOfBinderTests
{
    private const string KeyOwnerId = "11111111-1111-1111-1111-111111111111";
    private const string UiUserId = "auth0|64f2abcdef";

    [Fact]
    public void Apply_SetsParticipantId_FromValidHeader_ForApiKeyCallers()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);
        var request = RequestWithHeader(UiUserId);

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal(UiUserId, tenantContext.ParticipantId);
        Assert.Equal(KeyOwnerId, tenantContext.LoggedInUser);
    }

    [Fact]
    public void Apply_AcceptsEmailIdentities()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);
        var request = RequestWithHeader("jane+studio@example.com");

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal("jane+studio@example.com", tenantContext.ParticipantId);
    }

    [Fact]
    public void Apply_LeavesParticipantIdAsKeyOwner_WhenHeaderIsMissing()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);

        AdminOnBehalfOfBinder.Apply(new DefaultHttpContext().Request, tenantContext, NullLogger.Instance);

        Assert.Equal(KeyOwnerId, tenantContext.ParticipantId);
        Assert.Equal(KeyOwnerId, tenantContext.LoggedInUser);
    }

    [Fact]
    public void Apply_IgnoresHeader_WhenCallerIsNotAnApiKey()
    {
        var tenantContext = BuildContext(UserType.UserToken);
        var request = RequestWithHeader(UiUserId);

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal(KeyOwnerId, tenantContext.ParticipantId);
    }

    [Fact]
    public void Apply_TrimsAndStripsControlCharacters()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);
        var request = RequestWithHeader($"  {UiUserId}\u0001  ");

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal(UiUserId, tenantContext.ParticipantId);
    }

    [Fact]
    public void Apply_IgnoresHeader_WhenValueIsWhitespace()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);
        var request = RequestWithHeader("   ");

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal(KeyOwnerId, tenantContext.ParticipantId);
    }

    [Fact]
    public void Apply_IgnoresHeader_WhenValueExceedsMaxLength()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);
        var request = RequestWithHeader(new string('a', AdminOnBehalfOfBinder.MaxLength + 1));

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal(KeyOwnerId, tenantContext.ParticipantId);
    }

    [Fact]
    public void Apply_IgnoresHeader_WhenValueContainsDisallowedCharacters()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);
        var request = RequestWithHeader("<script>alert(1)</script>");

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal(KeyOwnerId, tenantContext.ParticipantId);
    }

    [Fact]
    public void Apply_DoesNothing_WhenRequestIsNull()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);

        AdminOnBehalfOfBinder.Apply(null, tenantContext, NullLogger.Instance);

        Assert.Equal(KeyOwnerId, tenantContext.ParticipantId);
    }

    [Fact]
    public void Apply_AcceptsValueAtMaxLength()
    {
        var tenantContext = BuildContext(UserType.UserApiKey);
        var identity = new string('a', AdminOnBehalfOfBinder.MaxLength);
        var request = RequestWithHeader(identity);

        AdminOnBehalfOfBinder.Apply(request, tenantContext, NullLogger.Instance);

        Assert.Equal(identity, tenantContext.ParticipantId);
    }

    private static TenantContext BuildContext(UserType userType) =>
        new(new ConfigurationBuilder().Build(), Mock.Of<ITenantTemporalConfigRepository>())
        {
            TenantId = "acme",
            LoggedInUser = KeyOwnerId,
            UserType = userType,
            UserRoles = [SystemRoles.TenantAdmin]
        };

    private static HttpRequest RequestWithHeader(string value)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers[AdminOnBehalfOfBinder.HeaderName] = value;
        return httpContext.Request;
    }
}
