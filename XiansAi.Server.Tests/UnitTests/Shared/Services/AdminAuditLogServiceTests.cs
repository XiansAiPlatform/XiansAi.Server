using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Driver;
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
        SetupNoRecentMatch();
    }

    [Fact]
    public async Task CreateEntryAsync_PersistsViewAs_WithActorAndTargetSeparated()
    {
        AuditLogEntry? captured = null;
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
        Assert.Equal(1, captured.AccessCount);
        Assert.Equal(captured.CreatedAt, captured.LastSeenAt);
        Assert.False(string.IsNullOrWhiteSpace(captured.IdempotencyKey));
    }

    [Fact]
    public async Task CreateEntryAsync_StoresUnknownAction()
    {
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
        var originalCreatedAt = DateTime.UtcNow.AddMinutes(-10);
        var existing = new AuditLogEntry
        {
            Id = "507f1f77bcf86cd799439011",
            TenantId = "acme",
            ParticipantId = "admin@localhost.local",
            LoggedInUser = "old-key-owner",
            Action = DomainEventTypes.ConversationViewAs,
            Description = "original description",
            ActivationName = "original-activation",
            CreatedAt = originalCreatedAt,
            LastSeenAt = DateTime.UtcNow.AddMinutes(-1),
            AccessCount = 2,
            Details = new Dictionary<string, object?> { ["targetParticipantId"] = "participant@localhost.local" }
        };
        _repository.Setup(r => r.TouchRecentMatchingAsync(
                "acme",
                DomainEventTypes.ConversationViewAs,
                "admin@localhost.local",
                "participant@localhost.local",
                It.Is<DateTime>(cutoff => IsOneHourCutoff(cutoff)),
                It.IsAny<DateTime>()))
            .ReturnsAsync(existing);

        var result = await _service.CreateEntryAsync("acme", ViewAsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusCode.Ok, result.StatusCode);
        Assert.Equal(existing.Id, result.Data!.Id);
        Assert.Equal(originalCreatedAt, result.Data.CreatedAt);
        Assert.Equal("original description", result.Data.Description);
        Assert.Equal("original-activation", result.Data.ActivationName);
        Assert.Equal("old-key-owner", result.Data.LoggedInUser);
        _repository.Verify(r => r.CreateAsync(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Fact]
    public async Task CreateEntryAsync_AfterIdempotencyWindow_InsertsNewRow()
    {
        SetupNoRecentMatch();

        var result = await _service.CreateEntryAsync("acme", ViewAsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusCode.Created, result.StatusCode);
        _repository.Verify(r => r.TouchRecentMatchingAsync(
            "acme",
            DomainEventTypes.ConversationViewAs,
            "admin@localhost.local",
            "participant@localhost.local",
            It.Is<DateTime>(cutoff => IsOneHourCutoff(cutoff)),
            It.IsAny<DateTime>()), Times.Once);
        _repository.Verify(r => r.CreateAsync(It.IsAny<AuditLogEntry>()), Times.Once);
    }

    [Fact]
    public async Task CreateEntryAsync_DuplicateKey_ReturnsTouchedRow()
    {
        var existing = new AuditLogEntry
        {
            Id = "507f1f77bcf86cd799439012",
            TenantId = "acme",
            ParticipantId = "admin@localhost.local",
            LoggedInUser = "api-key-owner",
            Action = DomainEventTypes.ConversationViewAs,
            CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            Details = new Dictionary<string, object?> { ["targetParticipantId"] = "participant@localhost.local" }
        };
        _repository.SetupSequence(r => r.TouchRecentMatchingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync((AuditLogEntry?)null)
            .ReturnsAsync(existing);
        _repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntry>()))
            .ThrowsAsync(CreateDuplicateKeyException());

        var result = await _service.CreateEntryAsync("acme", ViewAsRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Data!.Id);
        Assert.Equal(StatusCode.Ok, result.StatusCode);
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

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateEntryAsync_RejectsBlankPerformedBy(string? performedBy)
    {
        _tenantContext.SetupGet(c => c.ParticipantId).Returns(performedBy!);

        var result = await _service.CreateEntryAsync("acme", ViewAsRequest());

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Performed-by identity is required", result.ErrorMessage);
        _repository.Verify(r => r.CreateAsync(It.IsAny<AuditLogEntry>()), Times.Never);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateEntryAsync_RejectsBlankLoggedInUser(string? loggedInUser)
    {
        _tenantContext.SetupGet(c => c.LoggedInUser).Returns(loggedInUser!);

        var result = await _service.CreateEntryAsync("acme", ViewAsRequest());

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Logged-in user is required", result.ErrorMessage);
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

    [Fact]
    public async Task CreateEntryAsync_NormalizesJsonElementDetails()
    {
        AuditLogEntry? captured = null;
        _repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntry>()))
            .Callback<AuditLogEntry>(entry => captured = entry)
            .Returns(Task.CompletedTask);

        using var document = JsonDocument.Parse(
            """{"targetParticipantId":"user@example.com","count":42,"ok":true,"ratio":1.5,"nested":{"a":1}}""");
        var request = new AdminAuditLogCreateRequest
        {
            Action = "custom.audit.event",
            Details = new Dictionary<string, object?>
            {
                ["targetParticipantId"] = document.RootElement.GetProperty("targetParticipantId").Clone(),
                ["count"] = document.RootElement.GetProperty("count").Clone(),
                ["ok"] = document.RootElement.GetProperty("ok").Clone(),
                ["ratio"] = document.RootElement.GetProperty("ratio").Clone(),
                ["nested"] = document.RootElement.GetProperty("nested").Clone()
            }
        };

        var result = await _service.CreateEntryAsync("acme", request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("user@example.com", captured.Details!["targetParticipantId"]);
        Assert.Equal(42L, captured.Details["count"]);
        Assert.Equal(true, captured.Details["ok"]);
        Assert.Equal(1.5, captured.Details["ratio"]);
        Assert.Contains("a", captured.Details["nested"]?.ToString(), StringComparison.Ordinal);
    }

    private void SetupNoRecentMatch()
    {
        _repository.Setup(r => r.TouchRecentMatchingAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTime>(), It.IsAny<DateTime>()))
            .ReturnsAsync((AuditLogEntry?)null);
        _repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntry>()))
            .Returns(Task.CompletedTask);
    }

    private static bool IsOneHourCutoff(DateTime cutoff)
    {
        var expected = DateTime.UtcNow.Subtract(AdminAuditLogService.ViewAsIdempotencyWindow);
        return cutoff >= expected.AddSeconds(-5) && cutoff <= expected.AddSeconds(5);
    }

    private static MongoWriteException CreateDuplicateKeyException()
    {
        var writeError = new WriteError(ServerErrorCategory.DuplicateKey, 11000, "duplicate key", new MongoDB.Bson.BsonDocument());
        return new MongoWriteException(null, writeError, null, null);
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
