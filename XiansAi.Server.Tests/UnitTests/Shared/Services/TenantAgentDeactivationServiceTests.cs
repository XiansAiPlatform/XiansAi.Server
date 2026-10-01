using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
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
    private readonly Mock<ITenantContext> _context = new();
    private readonly TenantAgentDeactivationService _service;

    private static readonly TenantAgentDeactivationRequest Request =
        new(TenantId, Admin, new[] { SystemRoles.SysAdmin }, UserType.UserToken);

    public TenantAgentDeactivationServiceTests()
    {
        _context.SetupAllProperties();
        _tenants.Setup(x => x.GetByTenantIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Tenant(enabled: false));
        _activationService.Setup(x => x.DeactivateAgentAsync(It.IsAny<string>(), TenantId))
            .ReturnsAsync((string id, string _) => ServiceResult<AgentActivation>.Success(Activation(id, active: false)));

        var services = new ServiceCollection();
        services.AddSingleton(_tenants.Object);
        services.AddSingleton(_activations.Object);
        services.AddSingleton(_activationService.Object);
        services.AddSingleton(_context.Object);

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
        _activations.Setup(x => x.GetByTenantIdAsync(TenantId)).ReturnsAsync(activations.ToList());

    [Fact]
    public async Task DeactivatesOnlyActiveAgents()
    {
        SetActivations(Activation("a1", true), Activation("a2", true), Activation("a3", false));

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync("a1", TenantId), Times.Once);
        _activationService.Verify(x => x.DeactivateAgentAsync("a2", TenantId), Times.Once);
        _activationService.Verify(x => x.DeactivateAgentAsync("a3", It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task InfersActiveFromTimestampsWhenFlagMissing()
    {
        var legacy = Activation("legacy", true);
        legacy.Active = null;
        legacy.ActivatedAt = DateTime.UtcNow;
        SetActivations(legacy);

        await _service.ProcessAsync(Request, CancellationToken.None);

        _activationService.Verify(x => x.DeactivateAgentAsync("legacy", TenantId), Times.Once);
    }

    [Fact]
    public async Task RunsAsTheRequestingAdmin()
    {
        SetActivations(Activation("a1", true));

        await _service.ProcessAsync(Request, CancellationToken.None);

        Assert.Equal(TenantId, _context.Object.TenantId);
        Assert.Equal(Admin, _context.Object.LoggedInUser);
        Assert.Equal(new[] { SystemRoles.SysAdmin }, _context.Object.UserRoles);
        Assert.Equal(UserType.UserToken, _context.Object.UserType);
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
        SetActivations(Activation("a1", false));

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
    public async Task QueuedRequest_IsProcessedByBackgroundLoop()
    {
        var done = new TaskCompletionSource();
        SetActivations(Activation("a1", true));
        _activationService.Setup(x => x.DeactivateAgentAsync("a1", TenantId))
            .Callback(() => done.TrySetResult())
            .ReturnsAsync(ServiceResult<AgentActivation>.Success(Activation("a1", false)));

        await _service.StartAsync(CancellationToken.None);
        try
        {
            _service.Enqueue(Request);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            await _service.StopAsync(CancellationToken.None);
        }
    }
}
