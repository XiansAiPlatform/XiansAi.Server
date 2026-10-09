using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Unit tests for TenantAgentDeactivationService.ProcessAsync. The scope's services are mocked.
/// </summary>
public class TenantAgentDeactivationServiceTests
{
    private const string TenantId = "test-tenant";
    private const string Admin = "unit-test-admin";

    private readonly Mock<ITenantRepository> _tenants = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IActivationService> _activationService = new();
    private readonly TenantAgentDeactivationService _service;

    private static readonly TenantAgentDeactivationRequest Request =
        new(TenantId);

    public TenantAgentDeactivationServiceTests()
    {
        _tenants.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tenant(enabled: false));
        _activationService.Setup(x => x.DeactivateAgentAsync(It.IsAny<string>(), TenantId))
            .ReturnsAsync((string id, string _) => ServiceResult<AgentActivation>.Success(Activation(id, active: false)));

        var services = new ServiceCollection();
        services.AddSingleton(_tenants.Object);
        services.AddSingleton(_activations.Object);
        services.AddSingleton(_activationService.Object);

        _service = new TenantAgentDeactivationService(
            services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            NullLogger<TenantAgentDeactivationService>.Instance);
    }

    private static Tenant Tenant(bool enabled) => new()
    {
        Id = "id",
        TenantId = TenantId,
        Name = "Test Tenant",
        CreatedAt = DateTime.UtcNow,
        CreatedBy = Admin,
        Enabled = enabled
    };

    private static AgentActivation Activation(string id, bool active) => new()
    {
        Id = id,
        Name = id,
        AgentName = "agent",
        CreatedBy = Admin,
        TenantId = TenantId,
        Active = active
    };

    private void SetActivations(params AgentActivation[] activations) =>
        _activations.Setup(x => x.GetActiveActivationsAsync(TenantId)).ReturnsAsync(activations.ToList());

    [Fact]
    public async Task DeactivatesEveryActiveAgent()
    {
        SetActivations(Activation("a1", true), Activation("a2", true));

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync("a1", TenantId), Times.Once);
        _activationService.Verify(x => x.DeactivateAgentAsync("a2", TenantId), Times.Once);
        _activations.Verify(x => x.GetByTenantIdAsync(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SkipsWhenTenantWasReEnabled()
    {
        _tenants.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tenant(enabled: true));
        SetActivations(Activation("a1", true));

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SkipsWhenTenantWasDeleted()
    {
        _tenants.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Tenant?)null);
        SetActivations(Activation("a1", true));

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task NoActiveAgents_DoesNothing()
    {
        SetActivations();

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task FailedResult_ContinuesWithRemainingAgents()
    {
        SetActivations(Activation("a1", true), Activation("a2", true));
        _activationService.Setup(x => x.DeactivateAgentAsync("a1", TenantId))
            .ReturnsAsync(ServiceResult<AgentActivation>.InternalServerError("temporal down"));

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync("a2", TenantId), Times.Once);
    }

    [Fact]
    public async Task Exception_ContinuesWithRemainingAgents()
    {
        SetActivations(Activation("a1", true), Activation("a2", true));
        _activationService.Setup(x => x.DeactivateAgentAsync("a1", TenantId))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync("a2", TenantId), Times.Once);
    }

    [Fact]
    public void Enqueue_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _service.Enqueue(null!));
    }

    [Fact]
    public async Task QueuedRequest_IsProcessedByBackgroundLoop()
    {
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        SetActivations(Activation("a1", true));
        _activationService.Setup(x => x.DeactivateAgentAsync("a1", TenantId))
            .Callback(() => done.TrySetResult())
            .ReturnsAsync(ServiceResult<AgentActivation>.Success(Activation("a1", false)));

        await _service.StartAsync(CancellationToken.None);
        try
        {
            _service.Enqueue(Request);
            // Generous ceiling: completes as soon as the agent is deactivated, and only guards against a hang.
            await done.Task.WaitAsync(TimeSpan.FromSeconds(30));
        }
        finally
        {
            await _service.StopAsync(CancellationToken.None);
        }
    }
}
