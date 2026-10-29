using Moq;
using Shared.Auth;
using Shared.Utils;
using Temporalio.Common;

namespace Tests.UnitTests.Shared.Utils;

/// <summary>
/// The workflow's user id decides whose logs and threads the run belongs to: an explicit
/// userId wins, otherwise the on-behalf-of participant (which defaults to LoggedInUser).
/// </summary>
public class NewWorkflowOptionsTests
{
    private const string TenantId = "acme";
    private const string WorkflowType = "My Agent:Router Bot";

    private static ITenantContext TenantContext(string? participantId)
    {
        var context = new Mock<ITenantContext>();
        context.Setup(x => x.TenantId).Returns(TenantId);
        context.Setup(x => x.ParticipantId).Returns(participantId!);
        return context.Object;
    }

    private static string? UserIdOf(NewWorkflowOptions options)
    {
        return options.TypedSearchAttributes!.Get(SearchAttributeKey.CreateKeyword(Constants.UserIdKey));
    }

    [Fact]
    public void Constructor_PrefersExplicitUserId_OverParticipantId()
    {
        var options = new NewWorkflowOptions("My Agent", false, WorkflowType, null, TenantContext("ui-user"), "explicit-user");

        Assert.Equal("explicit-user", UserIdOf(options));
    }

    [Fact]
    public void Constructor_UsesParticipantId_WhenUserIdIsAbsent()
    {
        var options = new NewWorkflowOptions("My Agent", false, WorkflowType, null, TenantContext("ui-user"));

        Assert.Equal("ui-user", UserIdOf(options));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_UsesParticipantId_WhenUserIdIsBlank(string userId)
    {
        var options = new NewWorkflowOptions("My Agent", false, WorkflowType, null, TenantContext("ui-user"), userId);

        Assert.Equal("ui-user", UserIdOf(options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_Throws_WhenNeitherUserIdNorParticipantIdIsSet(string? participantId)
    {
        Assert.Throws<InvalidOperationException>(
            () => new NewWorkflowOptions("My Agent", false, WorkflowType, null, TenantContext(participantId)));
    }
}
