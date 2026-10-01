using Features.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Unit tests for queueing agent deactivation when a tenant is disabled via TenantService.UpdateTenant.
/// </summary>
public class TenantServiceDisableTests
{
    private const string TenantId = "test-tenant";
    private const string Admin = "unit-test-admin";

    private readonly Mock<ITenantRepository> _repo = new();
    private readonly Mock<ITenantContext> _context = new();
    private readonly Mock<IWebhookEventPublisher> _webhooks = new();
    private readonly Mock<IAuditLogService> _audit = new();
    private readonly Mock<ITenantAgentDeactivationService> _deactivationService = new();
    private readonly Tenant _stored;
    private readonly TenantService _service;

    public TenantServiceDisableTests()
    {
        _context.Setup(x => x.UserRoles).Returns(new[] { SystemRoles.SysAdmin });
        _context.Setup(x => x.TenantId).Returns(TenantId);
        _context.Setup(x => x.LoggedInUser).Returns(Admin);
        _context.Setup(x => x.UserType).Returns(UserType.UserToken);

        _stored = new Tenant
        {
            Id = ObjectId.GenerateNewId().ToString(),
            TenantId = TenantId,
            Name = "Test Tenant",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = Admin
        };
        _repo.Setup(x => x.GetByIdAsync(_stored.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_stored);
        _repo.Setup(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<Tenant>())).ReturnsAsync(true);
        _webhooks.Setup(x => x.PublishAsync(It.IsAny<string>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .Returns(Task.CompletedTask);
        _audit.Setup(x => x.RecordEntryAsync(
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<object?>(), It.IsAny<string?>()))
            .ReturnsAsync(ServiceResult<AuditLogEntry>.Success(new AuditLogEntry
            {
                TenantId = TenantId,
                ParticipantId = "p",
                LoggedInUser = "u",
                Action = "tenant.updated"
            }));

        _service = new TenantService(
            _repo.Object,
            Mock.Of<ITenantCacheService>(),
            NullLogger<TenantService>.Instance,
            _context.Object,
            Mock.Of<IRoleManagementService>(),
            _webhooks.Object,
            Mock.Of<ITenantMetadataProtector>(),
            Mock.Of<IActivationRepository>(),
            Mock.Of<IActivationService>(),
            Mock.Of<IKnowledgeRepository>(),
            _audit.Object,
            _deactivationService.Object);
    }

    [Fact]
    public async Task Disable_QueuesDeactivationAsCaller()
    {
        var result = await _service.UpdateTenant(_stored.Id, new UpdateTenantRequest { Enabled = false });

        Assert.True(result.IsSuccess);
        _deactivationService.Verify(x => x.Enqueue(It.Is<TenantAgentDeactivationRequest>(r =>
            r.TenantId == TenantId &&
            r.RequestedBy == Admin &&
            r.UserRoles.Contains(SystemRoles.SysAdmin) &&
            r.UserType == UserType.UserToken)), Times.Once);
    }

    [Fact]
    public async Task Disable_AlreadyDisabledTenant_QueuesAgainForRetry()
    {
        _stored.Enabled = false;

        var result = await _service.UpdateTenant(_stored.Id, new UpdateTenantRequest { Enabled = false });

        Assert.True(result.IsSuccess);
        _deactivationService.Verify(x => x.Enqueue(It.IsAny<TenantAgentDeactivationRequest>()), Times.Once);
    }

    [Fact]
    public async Task Enable_DoesNotQueue()
    {
        _stored.Enabled = false;

        await _service.UpdateTenant(_stored.Id, new UpdateTenantRequest { Enabled = true });

        _deactivationService.Verify(x => x.Enqueue(It.IsAny<TenantAgentDeactivationRequest>()), Times.Never);
    }

    [Fact]
    public async Task ProfileUpdateWithoutEnabled_DoesNotQueue()
    {
        await _service.UpdateTenant(_stored.Id, new UpdateTenantRequest { Name = "Renamed" });

        _deactivationService.Verify(x => x.Enqueue(It.IsAny<TenantAgentDeactivationRequest>()), Times.Never);
    }

    [Fact]
    public async Task Disable_WhenPersistFails_DoesNotQueue()
    {
        _repo.Setup(x => x.UpdateAsync(It.IsAny<string>(), It.IsAny<Tenant>())).ReturnsAsync(false);

        var result = await _service.UpdateTenant(_stored.Id, new UpdateTenantRequest { Enabled = false });

        Assert.False(result.IsSuccess);
        _deactivationService.Verify(x => x.Enqueue(It.IsAny<TenantAgentDeactivationRequest>()), Times.Never);
    }

    [Fact]
    public async Task Disable_WithoutAccess_DoesNotQueue()
    {
        _context.Setup(x => x.UserRoles).Returns(new[] { SystemRoles.TenantAdmin });
        _context.Setup(x => x.TenantId).Returns("other-tenant");

        var result = await _service.UpdateTenant(_stored.Id, new UpdateTenantRequest { Enabled = false });

        Assert.False(result.IsSuccess);
        _deactivationService.Verify(x => x.Enqueue(It.IsAny<TenantAgentDeactivationRequest>()), Times.Never);
    }
}
