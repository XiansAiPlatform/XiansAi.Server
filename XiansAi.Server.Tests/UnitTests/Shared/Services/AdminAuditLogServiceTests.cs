using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

public class AdminAuditLogServiceTests
{
    private readonly Mock<IAuditLogRepository> _repository = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly AdminAuditLogService _service;

    public AdminAuditLogServiceTests()
    {
        _tenantContext.SetupGet(c => c.ParticipantId).Returns("admin@localhost.local");
        _tenantContext.SetupGet(c => c.LoggedInUser).Returns("api-key-owner");
        _service = new AdminAuditLogService(
            _repository.Object,
            _tenantContext.Object,
            NullLogger<AdminAuditLogService>.Instance);
    }

    [Fact]
    public async Task CreateEntryAsync_PersistsViewAs_WithActorAndTargetSeparated()
    {
        AuditLogEntry? captured = null;
        _repository.Setup(r => r.FindRecentMatchingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync((AuditLogEntry?)null);
        _repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntry>()))
            .Callback<AuditLogEntry>(entry => captured = entry)
            .Returns(Task.CompletedTask);

        var result = await _service.CreateEntryAsync("acme", ViewAsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusCode.Created, result.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal("acme", captured.TenantId);
        Assert.Equal("admin@localhost.local", captured.ParticipantId);
        Assert.Equal("api-key-owner", captured.LoggedInUser);
        Assert.Equal(DomainEventTypes.ConversationViewAs, captured.Action);
        Assert.Equal("prod", captured.ActivationName);
        Assert.Equal("participant@localhost.local", captured.Details!["targetParticipantId"]);
        Assert.Equal("EmailDraftAgent", captured.Details["agentName"]);
        Assert.NotEqual("participant@localhost.local", captured.ParticipantId);
    }

    [Fact]
    public async Task CreateEntryAsync_StoresUnknownAction()
    {
        _repository.Setup(r => r.FindRecentMatchingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>()))
            .ReturnsAsync((AuditLogEntry?)null);

        var request = new AdminAuditLogCreateRequest
        {
            Action = "custom.audit.event",
            Description = "A new admin action",
            Details = new Dictionary<string, object?> { ["targetParticipantId"] = "user@example.com" }
        };

        var result = await _service.CreateEntryAsync("acme", request);

        Assert.True(result.IsSuccess);
        Assert.Equal("custom.audit.event", result.Data!.Action);
        _repository.Verify(r => r.CreateAsync(It.Is<AuditLogEntry>(e => e.Action == "custom.audit.event")), Times.Once);
    }

    [Fact]
    public async Task CreateEntryAsync_ReplaysRecentDuplicate_WithoutInsert()
    {
        var existing = new AuditLogEntry
        {
            Id = "507f1f77bcf86cd799439011",
            TenantId = "acme",
            ParticipantId = "admin@localhost.local",
            LoggedInUser = "old-key-owner",
            Action = DomainEventTypes.ConversationViewAs,
            CreatedAt = DateTime.UtcNow.AddMinutes(-10),
            Details = new Dictionary<string, object?> { ["targetParticipantId"] = "participant@localhost.local" }
        };
        _repository.Setup(r => r.FindRecentMatchingAsync(
                "acme",
                DomainEventTypes.ConversationViewAs,
                "admin@localhost.local",
                "participant@localhost.local",
                It.IsAny<DateTime>()))
            .ReturnsAsync(existing);

        var result = await _service.CreateEntryAsync("acme", ViewAsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusCode.Ok, result.StatusCode);
        Assert.Equal(existing.Id, result.Data!.Id);
        Assert.True(result.Data.CreatedAt > DateTime.UtcNow.AddMinutes(-1));
        _repository.Verify(r => r.ReplaceAsync(existing), Times.Once);
        _repository.Verify(r => r.CreateAsync(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateEntryAsync_ViewAs_RejectsMissingTarget(string? target)
    {
        var request = ViewAsRequest();
        request.Details = new Dictionary<string, object?>
        {
            ["targetParticipantId"] = target,
            ["agentName"] = "EmailDraftAgent"
        };

        var result = await _service.CreateEntryAsync("acme", request);

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal("details.targetParticipantId is required", result.ErrorMessage);
        _repository.Verify(r => r.CreateAsync(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task CreateEntryAsync_RejectsMissingAction()
    {
        var result = await _service.CreateEntryAsync("acme", new AdminAuditLogCreateRequest());

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Action is required", result.ErrorMessage);
    }

    [Fact]
    public async Task CreateEntryAsync_RejectsBlankTenant()
    {
        var result = await _service.CreateEntryAsync("  ", ViewAsRequest());

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Tenant ID is required", result.ErrorMessage);
    }

    private static AdminAuditLogCreateRequest ViewAsRequest() => new()
    {
        Action = DomainEventTypes.ConversationViewAs,
        Description = "System admin viewed conversations as participant@localhost.local",
        ActivationName = "prod",
        Details = new Dictionary<string, object?>
        {
            ["targetParticipantId"] = "participant@localhost.local",
            ["agentName"] = "EmailDraftAgent"
        }
    };
}
