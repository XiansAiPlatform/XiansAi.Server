using Features.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Repositories;
using Shared.Utils.Services;
using Shared.Utils.Temporal;

namespace XiansAi.Server.Tests.UnitTests.Features.WebApi.Services;

public class WorkflowCancelServiceTests
{
    private const string TenantId = "acme";
    private const string OwnWorkflowId = "acme:My Agent:Router Bot:run-1";
    private const string OtherTenantWorkflowId = "victim:My Agent:Router Bot:run-1";

    private readonly Mock<ITemporalGatewayService> _temporalGateway = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly WorkflowCancelService _service;

    public WorkflowCancelServiceTests()
    {
        _tenantContext.Setup(c => c.TenantId).Returns(TenantId);
        _service = new WorkflowCancelService(
            _temporalGateway.Object,
            _tenantContext.Object,
            Mock.Of<IAgentRepository>(),
            NullLogger<WorkflowCancelService>.Instance);
    }

    [Fact]
    public async Task CancelWorkflow_RejectsAnotherTenantsWorkflowId_WithoutContactingTemporal()
    {
        var result = await _service.CancelWorkflow(OtherTenantWorkflowId, force: true);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Forbidden, result.StatusCode);
        _temporalGateway.Verify(
            g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelWorkflow_RejectsPrefixCollision_WithoutContactingTemporal()
    {
        _tenantContext.Setup(c => c.TenantId).Returns("ac");

        var result = await _service.CancelWorkflow(OwnWorkflowId, force: false);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Forbidden, result.StatusCode);
        _temporalGateway.Verify(
            g => g.GetClientAsync(It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task CancelWorkflow_AllowsTheCallersOwnWorkflowId_ToReachTemporal()
    {
        _temporalGateway
            .Setup(g => g.GetClientAsync(TenantId, "My Agent"))
            .ThrowsAsync(new InvalidOperationException("Temporal is not available in this test"));

        var result = await _service.CancelWorkflow(OwnWorkflowId, force: false);

        _temporalGateway.Verify(g => g.GetClientAsync(TenantId, "My Agent"), Times.Once);
        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
    }
}
