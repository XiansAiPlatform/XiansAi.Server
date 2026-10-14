using System.Net;
using System.Reflection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Clusters;
using MongoDB.Driver.Core.Connections;
using MongoDB.Driver.Core.Servers;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Knowledge creates must be idempotent: the pre-check and the duplicate-key recovery both use the
/// exact unique-index key, never "the latest version".
/// </summary>
public class KnowledgeServiceCreateTests
{
    private const string Tenant = "tenant-a";
    private const string AgentName = "Lead Discovery Agent";

    private readonly Mock<IKnowledgeRepository> _repo = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly KnowledgeService _sut;

    public KnowledgeServiceCreateTests()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(Tenant);
        _tenantContext.SetupGet(t => t.LoggedInUser).Returns("tester");
        _tenantContext.SetupGet(t => t.UserRoles).Returns(new[] { SystemRoles.SysAdmin });

        _sut = new KnowledgeService(
            _repo.Object,
            NullLogger<KnowledgeService>.Instance,
            _tenantContext.Object,
            new Mock<IAgentRepository>().Object,
            new Mock<IWebhookEventPublisher>().Object,
            new Mock<IAuditLogService>().Object);
    }

    private static KnowledgeRequest Request(string? activation = null, bool systemScoped = false) => new()
    {
        Name = "playbook",
        Content = "bundled content",
        Type = "markdown",
        Agent = AgentName,
        ActivationName = activation,
        SystemScoped = systemScoped
    };

    private static string Hash(string content, string type) => HashGenerator.GenerateContentHash(content + type);

    private static Knowledge Existing(string content = "bundled content", string? tenant = Tenant,
        string? activation = null, bool systemScoped = false) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Name = "playbook",
        Content = content,
        Type = "markdown",
        Version = Hash(content, "markdown"),
        TenantId = tenant,
        Agent = AgentName,
        ActivationName = activation,
        SystemScoped = systemScoped,
        CreatedBy = "seed"
    };

    private static MongoWriteException DuplicateKey()
    {
        var writeError = (WriteError)Activator.CreateInstance(
            typeof(WriteError),
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public,
            binder: null,
            args: new object?[] { ServerErrorCategory.DuplicateKey, 11000, "E11000 duplicate key", new BsonDocument() },
            culture: null)!;
        var connectionId = new ConnectionId(new ServerId(new ClusterId(1), new DnsEndPoint("localhost", 27017)));
        return new MongoWriteException(connectionId, writeError, null, null);
    }

    private void SetupExactLookup(Knowledge? result)
        => _repo.Setup(r => r.GetByUniqueKeyAsync<Knowledge>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync(result);

    // --- agent / web route: Create ---

    [Fact]
    public async Task Create_ExactRowExists_ReturnsExistingWithoutInsert()
    {
        var existing = Existing();
        existing.UpdatedAt = existing.CreatedAt = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        SetupExactLookup(existing);

        var result = await _sut.Create(Request());

        var ok = Assert.IsType<Ok<Knowledge>>(result);
        Assert.Same(existing, ok.Value);
        _repo.Verify(r => r.CreateAsync(It.IsAny<Knowledge>()), Times.Never);
        _repo.Verify(r => r.TouchUpdatedAtAsync(existing.Id, It.IsAny<DateTime>()), Times.Once);
        Assert.True(existing.UpdatedAt > existing.CreatedAt);
    }

    [Fact]
    public async Task Create_LooksUpExactKey_NotLatestVersion()
    {
        // A tenant edit exists (newer row), but the original bundled row still holds the key.
        // The lookup must be by (name, content hash, agent, tenant, activation, system scope).
        SetupExactLookup(Existing());

        await _sut.Create(Request(activation: "RegistryTest-AT"));

        _repo.Verify(r => r.GetByUniqueKeyAsync<Knowledge>(
            "playbook", Hash("bundled content", "markdown"), AgentName, Tenant, "RegistryTest-AT", false), Times.Once);
        _repo.Verify(r => r.GetLatestByNameAndTenantAsync<Knowledge>(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Create_SystemScoped_LooksUpNullTenantRow()
    {
        SetupExactLookup(Existing(tenant: null, systemScoped: true));

        await _sut.Create(Request(systemScoped: true));

        _repo.Verify(r => r.GetByUniqueKeyAsync<Knowledge>(
            "playbook", Hash("bundled content", "markdown"), AgentName, null, null, true), Times.Once);
    }

    [Fact]
    public async Task Create_NoExistingRow_Inserts()
    {
        SetupExactLookup(null);

        var result = await _sut.Create(Request());

        Assert.IsType<Ok<Knowledge>>(result);
        _repo.Verify(r => r.CreateAsync(It.Is<Knowledge>(k =>
            k.Version == Hash("bundled content", "markdown") && k.TenantId == Tenant)), Times.Once);
    }

    [Fact]
    public async Task Create_DuplicateKeyRace_ReturnsExistingFoundByExactKey()
    {
        var existing = Existing(activation: "RegistryTest-AT");
        _repo.SetupSequence(r => r.GetByUniqueKeyAsync<Knowledge>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync((Knowledge?)null)   // pre-check: not there yet
            .ReturnsAsync(existing);          // after the collision: exact re-read finds it
        _repo.Setup(r => r.CreateAsync(It.IsAny<Knowledge>())).ThrowsAsync(DuplicateKey());

        var result = await _sut.Create(Request(activation: "RegistryTest-AT"));

        var ok = Assert.IsType<Ok<Knowledge>>(result);
        Assert.Same(existing, ok.Value);
        _repo.Verify(r => r.TouchUpdatedAtAsync(existing.Id, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task CreateWithStatus_NewRecord_ReportsCreated()
    {
        SetupExactLookup(null);

        var (knowledge, created, error) = await _sut.CreateWithStatus(Request());

        Assert.NotNull(knowledge);
        Assert.True(created);
        Assert.Null(error);
        _repo.Verify(r => r.TouchUpdatedAtAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task CreateWithStatus_ExistingRecord_ReportsNotCreatedAndTouches()
    {
        var existing = Existing();
        SetupExactLookup(existing);

        var (knowledge, created, error) = await _sut.CreateWithStatus(Request());

        Assert.Same(existing, knowledge);
        Assert.False(created);
        Assert.Null(error);
        _repo.Verify(r => r.TouchUpdatedAtAsync(existing.Id, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task CreateWithStatus_NonSystemWithoutTenant_ReturnsErrorAndNoWrites()
    {
        _tenantContext.SetupGet(t => t.TenantId).Returns(string.Empty);

        var (knowledge, created, error) = await _sut.CreateWithStatus(Request());

        Assert.Null(knowledge);
        Assert.False(created);
        Assert.NotNull(error);
        _repo.Verify(r => r.CreateAsync(It.IsAny<Knowledge>()), Times.Never);
        _repo.Verify(r => r.TouchUpdatedAtAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task Create_DuplicateKeyButRowNotFound_Rethrows()
    {
        SetupExactLookup(null);
        _repo.Setup(r => r.CreateAsync(It.IsAny<Knowledge>())).ThrowsAsync(DuplicateKey());

        await Assert.ThrowsAsync<MongoWriteException>(() => _sut.Create(Request()));
    }

    // --- admin route: CreateOrGetForTenantAsync ---

    [Fact]
    public async Task CreateOrGetForTenant_SameContent_IsNoOp()
    {
        var existing = Existing(activation: "RegistryTest-AT");
        SetupExactLookup(existing);

        var (knowledge, created) = await _sut.CreateOrGetForTenantAsync(
            "playbook", "bundled content", "markdown", Tenant, "admin", AgentName, activationName: "RegistryTest-AT");

        Assert.False(created);
        Assert.Same(existing, knowledge);
        _repo.Verify(r => r.CreateAsync(It.IsAny<Knowledge>()), Times.Never);
        _repo.Verify(r => r.TouchUpdatedAtAsync(existing.Id, It.IsAny<DateTime>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrGetForTenant_SameVersionDifferentContent_Conflicts()
    {
        SetupExactLookup(Existing(content: "old content"));

        await Assert.ThrowsAsync<KnowledgeConflictException>(() => _sut.CreateOrGetForTenantAsync(
            "playbook", "new content", "markdown", Tenant, "admin", AgentName, version: Hash("old content", "markdown")));
        _repo.Verify(r => r.CreateAsync(It.IsAny<Knowledge>()), Times.Never);
        _repo.Verify(r => r.TouchUpdatedAtAsync(It.IsAny<string>(), It.IsAny<DateTime>()), Times.Never);
    }

    [Fact]
    public async Task CreateOrGetForTenant_New_Inserts()
    {
        SetupExactLookup(null);

        var (_, created) = await _sut.CreateOrGetForTenantAsync(
            "playbook", "bundled content", "markdown", Tenant, "admin", AgentName);

        Assert.True(created);
        _repo.Verify(r => r.CreateAsync(It.IsAny<Knowledge>()), Times.Once);
    }

    [Fact]
    public async Task CreateOrGetForTenant_DuplicateKeyRace_ReturnsExisting()
    {
        var existing = Existing();
        _repo.SetupSequence(r => r.GetByUniqueKeyAsync<Knowledge>(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<bool>()))
            .ReturnsAsync((Knowledge?)null)
            .ReturnsAsync(existing);
        _repo.Setup(r => r.CreateAsync(It.IsAny<Knowledge>())).ThrowsAsync(DuplicateKey());

        var (knowledge, created) = await _sut.CreateOrGetForTenantAsync(
            "playbook", "bundled content", "markdown", Tenant, "admin", AgentName);

        Assert.False(created);
        Assert.Same(existing, knowledge);
        _repo.Verify(r => r.TouchUpdatedAtAsync(existing.Id, It.IsAny<DateTime>()), Times.Once);
    }
}
