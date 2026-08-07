using Moq;
using Shared.Auth;
using Shared.Exceptions;

namespace Tests.UnitTests.Shared.Utils;

public class WorkflowIdentifierTests
{
    private const string TenantId = "acme";

    private static ITenantContext TenantContext()
    {
        var context = new Mock<ITenantContext>();
        context.Setup(x => x.TenantId).Returns(TenantId);
        return context.Object;
    }

    [Fact]
    public void Constructor_ResolvesAWorkflowType_AgainstTheCallersTenant()
    {
        var identifier = new WorkflowIdentifier("My Agent:Router Bot", TenantContext());

        Assert.Equal("acme:My Agent:Router Bot", identifier.WorkflowId);
        Assert.Equal("My Agent:Router Bot", identifier.WorkflowType);
        Assert.Equal("My Agent", identifier.AgentName);
    }

    [Fact]
    public void Constructor_AcceptsAFullyQualifiedWorkflowId()
    {
        var identifier = new WorkflowIdentifier("acme:My Agent:Router Bot", TenantContext());

        Assert.Equal("acme:My Agent:Router Bot", identifier.WorkflowId);
        Assert.Equal("My Agent:Router Bot", identifier.WorkflowType);
        Assert.Equal("My Agent", identifier.AgentName);
    }

    [Fact]
    public void Constructor_AcceptsAWorkflowIdWithARunSuffix()
    {
        var identifier = new WorkflowIdentifier("acme:My Agent:Router Bot:run-42", TenantContext());

        Assert.Equal("acme:My Agent:Router Bot:run-42", identifier.WorkflowId);
        Assert.Equal("My Agent:Router Bot", identifier.WorkflowType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_ExplainsThatAnIdentifierIsRequired_WhenNoneIsSupplied(string? identifier)
    {
        var exception = Assert.Throws<InvalidWorkflowIdentifierException>(
            () => new WorkflowIdentifier(identifier, TenantContext()));

        Assert.Equal(identifier, exception.Identifier);
        Assert.Contains("A workflow identifier is required", exception.Message);
        Assert.Contains(TenantId, exception.Message);
    }

    [Fact]
    public void Constructor_RejectsAWorkflowIdBelongingToAnotherTenant()
    {
        var exception = Assert.Throws<InvalidWorkflowIdentifierException>(
            () => new WorkflowIdentifier("other-tenant:My Agent:Router Bot", TenantContext()));

        Assert.Equal("other-tenant:My Agent:Router Bot", exception.Identifier);
        Assert.Contains("other-tenant", exception.Message);
        Assert.Contains(TenantId, exception.Message);
    }

    [Fact]
    public void Constructor_SpellsOutBothReadings_WhenAThreeSegmentIdentifierIsAmbiguous()
    {
        var exception = Assert.Throws<InvalidWorkflowIdentifierException>(
            () => new WorkflowIdentifier("My Agent:Router Bot:run-42", TenantContext()));

        // Either the tenant prefix was left off, or the first segment was meant as a tenant id.
        Assert.Contains("acme:My Agent:Router Bot:run-42", exception.Message);
        Assert.Contains("My Agent:Router Bot", exception.Message);
    }

    [Fact]
    public void GetWorkflowType_RejectsAnIdentifierWithNoSeparator()
    {
        var exception = Assert.Throws<InvalidWorkflowIdentifierException>(
            () => WorkflowIdentifier.GetWorkflowType("My Agent"));

        Assert.Equal("My Agent", exception.Identifier);
        Assert.Contains("contains no `:`", exception.Message);
    }

    [Fact]
    public void BuildWorkflowId_AppendsTheRunSuffixOnlyWhenSupplied()
    {
        Assert.Equal("acme:My Agent:Router Bot",
            WorkflowIdentifier.BuildWorkflowId(TenantId, "My Agent", "Router Bot"));
        Assert.Equal("acme:My Agent:Router Bot:run-42",
            WorkflowIdentifier.BuildWorkflowId(TenantId, "My Agent", "Router Bot", "run-42"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("acme:My Agent:Router Bot")]
    [InlineData("My Agent:Router Bot")]
    public void GetIdPostfix_ReturnsNull_WhenThereIsNoPostfix(string? workflowId)
    {
        Assert.Null(WorkflowIdentifier.GetIdPostfix(workflowId!));
    }

    [Fact]
    public void GetIdPostfix_ReturnsTheSingleSegmentPostfix()
    {
        Assert.Equal("run-42",
            WorkflowIdentifier.GetIdPostfix("acme:My Agent:Router Bot:run-42"));
    }

    [Fact]
    public void GetIdPostfix_RejoinsMultiSegmentPostfixes()
    {
        Assert.Equal("env:prod:v2",
            WorkflowIdentifier.GetIdPostfix("acme:My Agent:Router Bot:env:prod:v2"));
    }
}
