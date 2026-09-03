using Features.AdminApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Utils.Services;
using Shared.Utils.Temporal;
using Temporalio.Client;

namespace XiansAi.Server.Tests.UnitTests.Features.AdminApi;

/// <summary>
/// Covers the guard rails and version-string handling of <see cref="WorkerDeploymentService"/>.
/// The Temporal calls themselves are exercised against a real server rather than mocked: the worker
/// deployment operations live on the raw <c>WorkflowService</c> gRPC surface, which has no seam a unit
/// test can substitute. What is tested here is everything that must hold <em>before</em> a Temporal
/// call is attempted, plus the canonical-version conversions that shape requests and responses.
/// </summary>
public class WorkerDeploymentServiceTests
{
    private const string TenantId = "acme";
    private const string DeploymentName = "lead-discovery";

    private static WorkerDeploymentService CreateService(Mock<ITemporalGatewayService>? gateway = null) =>
        new((gateway ?? new Mock<ITemporalGatewayService>()).Object,
            NullLogger<WorkerDeploymentService>.Instance);

    // --- validation: nothing should reach Temporal when the request is malformed ---

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ListDeployments_RejectsBlankTenant_WithoutCallingTemporal(string tenantId)
    {
        var gateway = new Mock<ITemporalGatewayService>();

        var result = await CreateService(gateway).ListDeploymentsAsync(tenantId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        gateway.Verify(g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DescribeDeployment_RejectsBlankDeploymentName_WithoutCallingTemporal(string deploymentName)
    {
        var gateway = new Mock<ITemporalGatewayService>();

        var result = await CreateService(gateway).DescribeDeploymentAsync(TenantId, deploymentName);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        gateway.Verify(g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SetCurrentVersion_RejectsBlankBuildId_WithoutCallingTemporal(string buildId)
    {
        var gateway = new Mock<ITemporalGatewayService>();

        var result = await CreateService(gateway)
            .SetCurrentVersionAsync(TenantId, DeploymentName, buildId, "operator", false);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        gateway.Verify(g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Theory]
    [InlineData(-0.1f)]
    [InlineData(100.1f)]
    [InlineData(500f)]
    public async Task SetRampingVersion_RejectsPercentageOutsideRange_WithoutCallingTemporal(float percentage)
    {
        var gateway = new Mock<ITemporalGatewayService>();

        var result = await CreateService(gateway)
            .SetRampingVersionAsync(TenantId, DeploymentName, "1.4.0", percentage, "operator", false);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        gateway.Verify(g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task SetRampingVersion_RejectsBlankBuildId_BeforeCheckingPercentage()
    {
        var gateway = new Mock<ITemporalGatewayService>();

        // A blank build ID with an otherwise valid percentage must still fail on the build ID,
        // so the caller is told the actual problem.
        var result = await CreateService(gateway)
            .SetRampingVersionAsync(TenantId, DeploymentName, "", 50f, "operator", false);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Contains("buildId", result.ErrorMessage);
    }

    // --- multi-cluster fan-out ---

    [Fact]
    public async Task ListDeployments_ReturnsEmptySuccess_WhenTenantHasNoTemporalClusters()
    {
        var gateway = new Mock<ITemporalGatewayService>();
        gateway.Setup(g => g.GetClientsAsync(TenantId)).Returns(NoClients());

        var result = await CreateService(gateway).ListDeploymentsAsync(TenantId);

        // No clusters is an empty result, not a failure: the tenant simply has no versioned workers.
        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task ListDeployments_FansOutAcrossClusters_RatherThanUsingTheDefaultClient()
    {
        var gateway = new Mock<ITemporalGatewayService>();
        gateway.Setup(g => g.GetClientsAsync(TenantId)).Returns(NoClients());

        await CreateService(gateway).ListDeploymentsAsync(TenantId);

        // Listing from a single client would hide deployments on an OriginTenant's cluster.
        gateway.Verify(g => g.GetClientsAsync(TenantId), Times.Once);
        gateway.Verify(g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task ListDeployments_ReturnsFailure_WhenClientResolutionThrows()
    {
        var gateway = new Mock<ITemporalGatewayService>();
        gateway.Setup(g => g.GetClientsAsync(TenantId)).Throws(new InvalidOperationException("no temporal config"));

        var result = await CreateService(gateway).ListDeploymentsAsync(TenantId);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
    }

    [Fact]
    public async Task DescribeDeployment_PassesDeploymentNameAsAgentHint()
    {
        var gateway = new Mock<ITemporalGatewayService>();
        gateway
            .Setup(g => g.GetClientAsync(TenantId, DeploymentName))
            .ThrowsAsync(new InvalidOperationException("stop here"));

        var result = await CreateService(gateway).DescribeDeploymentAsync(TenantId, DeploymentName);

        // Xians.Lib defaults the deployment name to the agent name, so the hint is what routes the call
        // to the right cluster for a tenant whose agents span more than one.
        Assert.False(result.IsSuccess);
        gateway.Verify(g => g.GetClientAsync(TenantId, DeploymentName), Times.Once);
        gateway.Verify(g => g.GetClientAsync(TenantId, null), Times.Never);
    }

#pragma warning disable CS1998 // async iterator with no await - test stub
    private static async IAsyncEnumerable<ITemporalClient> NoClients()
    {
        yield break;
    }
#pragma warning restore CS1998

    // --- version string handling ---

    [Fact]
    public void QualifyVersion_AddsDeploymentPrefix_ToBareBuildId()
    {
        Assert.Equal("lead-discovery.1.4.0", WorkerDeploymentService.QualifyVersion(DeploymentName, "1.4.0"));
    }

    [Fact]
    public void QualifyVersion_LeavesAlreadyQualifiedVersionUnchanged()
    {
        Assert.Equal(
            "lead-discovery.1.4.0",
            WorkerDeploymentService.QualifyVersion(DeploymentName, "lead-discovery.1.4.0"));
    }

    [Fact]
    public void StripDeploymentPrefix_ReducesQualifiedVersionToBareBuildId()
    {
        Assert.Equal("1.4.0", WorkerDeploymentService.StripDeploymentPrefix(DeploymentName, "lead-discovery.1.4.0"));
    }

    [Fact]
    public void StripDeploymentPrefix_LeavesBareBuildIdUnchanged()
    {
        Assert.Equal("1.4.0", WorkerDeploymentService.StripDeploymentPrefix(DeploymentName, "1.4.0"));
    }

    [Fact]
    public void StripDeploymentPrefix_OnlyStripsAnExactPrefix()
    {
        // A build ID that merely starts with similar text must survive intact, otherwise a version
        // belonging to another deployment would be silently rewritten.
        Assert.Equal(
            "lead-discovery-v2.1.4.0",
            WorkerDeploymentService.StripDeploymentPrefix(DeploymentName, "lead-discovery-v2.1.4.0"));
    }

    [Fact]
    public void StripDeploymentPrefix_AndQualifyVersion_RoundTrip()
    {
        var bare = WorkerDeploymentService.StripDeploymentPrefix(DeploymentName, "lead-discovery.1.4.0");
        Assert.Equal("lead-discovery.1.4.0", WorkerDeploymentService.QualifyVersion(DeploymentName, bare));
    }

    [Fact]
    public void Unversioned_MatchesTemporalSentinel()
    {
        // Temporal reports this exact value for a deployment with no promoted version; the API contract
        // documents it, so it must not drift.
        Assert.Equal("__unversioned__", WorkerDeploymentService.Unversioned);
    }
}
