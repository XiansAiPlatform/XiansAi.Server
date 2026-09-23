using System.Text.Json;
using Features.AgentApi.Models;
using Features.AgentApi.Repositories;
using Features.AgentApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Features.AgentApi;

/// <summary>
/// A <c>useKeyAsIdentifier</c> save resolves the document to replace by
/// tenant + agent + type + key + activation + participant, the fields the client stamps and reads by.
/// </summary>
public class DocumentServiceIdentityTests
{
    private const string Tenant = "tenant-a";
    private readonly Mock<IDocumentRepository> _repository = new();
    private readonly Mock<IAgentPermissionRepository> _permissions = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly DocumentService _service;

    public DocumentServiceIdentityTests()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns(Tenant);
        _tenantContext.SetupGet(c => c.LoggedInUser).Returns("agent-user");
        _tenantContext.SetupGet(c => c.UserRoles).Returns([SystemRoles.SysAdmin]);
        _repository.Setup(r => r.CreateAsync(It.IsAny<Document>()))
            .ReturnsAsync((Document d) =>
            {
                d.Id = ObjectId.GenerateNewId().ToString();
                return d;
            });
        _repository.Setup(r => r.UpdateAsync(It.IsAny<Document>())).ReturnsAsync(true);
        _service = new DocumentService(
            _repository.Object,
            _tenantContext.Object,
            NullLogger<DocumentService>.Instance,
            _permissions.Object);
    }

    private static DocumentRequest<JsonElement> SaveRequest(
        string agentId, string? activationName, string? participantId = null, bool overwrite = true)
    {
        return new DocumentRequest<JsonElement>
        {
            Document = new DocumentDto<JsonElement>
            {
                AgentId = agentId,
                ActivationName = activationName,
                ParticipantId = participantId,
                Type = "DailyTaskCounter",
                Key = "2026-09-20",
                Content = JsonSerializer.SerializeToElement(new { count = 1 })
            },
            Options = new DocumentOptions { UseKeyAsIdentifier = true, Overwrite = overwrite }
        };
    }

    private static Document Existing(string agentId, string? activationName, string? participantId = null) => new()
    {
        Id = ObjectId.GenerateNewId().ToString(),
        TenantId = Tenant,
        AgentId = agentId,
        ActivationName = activationName,
        ParticipantId = participantId,
        Type = "DailyTaskCounter",
        Key = "2026-09-20",
        CreatedAt = new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public async Task Save_ResolvesByTenantAgentTypeKeyActivationAndParticipant()
    {
        DocumentIdentity? identity = null;
        _repository.Setup(r => r.GetByIdentityAsync(It.IsAny<DocumentIdentity>()))
            .Callback<DocumentIdentity>(i => identity = i)
            .ReturnsAsync((Document?)null);

        var result = await _service.SaveAsync(SaveRequest("Agent A", "front-desk", "user@example.com"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new DocumentIdentity(Tenant, "Agent A", "DailyTaskCounter", "2026-09-20", "front-desk", "user@example.com"), identity);
        _repository.Verify(r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task Save_NeverReplacesAnotherAgentsDocumentWithSameTypeAndKey()
    {
        var agentADocument = Existing("Agent A", "front-desk");
        _repository.Setup(r => r.GetByIdentityAsync(It.Is<DocumentIdentity>(i => i.AgentId == "Agent A")))
            .ReturnsAsync(agentADocument);
        _repository.Setup(r => r.GetByIdentityAsync(It.Is<DocumentIdentity>(i => i.AgentId != "Agent A")))
            .ReturnsAsync((Document?)null);

        var result = await _service.SaveAsync(SaveRequest("Agent B", "front-desk"));

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
        _repository.Verify(r => r.CreateAsync(It.Is<Document>(d => d.AgentId == "Agent B")), Times.Once);
        Assert.NotEqual(agentADocument.Id, result.Data.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Save_TwoActivationsOfOneAgent_GetSeparateDocuments()
    {
        _repository.Setup(r => r.GetByIdentityAsync(It.Is<DocumentIdentity>(i => i.ActivationName == "front-desk")))
            .ReturnsAsync(Existing("Agent A", "front-desk"));
        _repository.Setup(r => r.GetByIdentityAsync(It.Is<DocumentIdentity>(i => i.ActivationName == "back-office")))
            .ReturnsAsync((Document?)null);

        var result = await _service.SaveAsync(SaveRequest("Agent A", "back-office"));

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
        _repository.Verify(r => r.CreateAsync(It.Is<Document>(d => d.ActivationName == "back-office")), Times.Once);
    }

    [Fact]
    public async Task Save_TwoParticipants_GetSeparateDocuments()
    {
        _repository.Setup(r => r.GetByIdentityAsync(It.Is<DocumentIdentity>(i => i.ParticipantId == "alice")))
            .ReturnsAsync(Existing("Agent A", "front-desk", "alice"));
        _repository.Setup(r => r.GetByIdentityAsync(It.Is<DocumentIdentity>(i => i.ParticipantId == "bob")))
            .ReturnsAsync((Document?)null);

        var result = await _service.SaveAsync(SaveRequest("Agent A", "front-desk", "bob"));

        Assert.True(result.IsSuccess);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
        _repository.Verify(r => r.CreateAsync(It.Is<Document>(d => d.ParticipantId == "bob")), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Save_BlankActivationAndParticipant_ResolveToNullSlots(string? blank)
    {
        DocumentIdentity? identity = null;
        _repository.Setup(r => r.GetByIdentityAsync(It.IsAny<DocumentIdentity>()))
            .Callback<DocumentIdentity>(i => identity = i)
            .ReturnsAsync((Document?)null);

        var result = await _service.SaveAsync(SaveRequest("Agent A", blank, blank));

        Assert.True(result.IsSuccess);
        Assert.Null(identity!.ActivationName);
        Assert.Null(identity.ParticipantId);
        _repository.Verify(r => r.CreateAsync(It.Is<Document>(d => d.ActivationName == null && d.ParticipantId == null)), Times.Once);
    }

    [Fact]
    public async Task Save_OverwriteFalse_OnOwnExistingDocument_RejectsAndWritesNothing()
    {
        _repository.Setup(r => r.GetByIdentityAsync(It.IsAny<DocumentIdentity>()))
            .ReturnsAsync(Existing("Agent A", "front-desk"));

        var result = await _service.SaveAsync(SaveRequest("Agent A", "front-desk", overwrite: false));

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _repository.Verify(r => r.CreateAsync(It.IsAny<Document>()), Times.Never);
        _repository.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task Save_OverwriteTrue_ReplacesOwnDocumentInPlace()
    {
        var existing = Existing("Agent A", "front-desk", "alice");
        _repository.Setup(r => r.GetByIdentityAsync(It.IsAny<DocumentIdentity>())).ReturnsAsync(existing);
        Document? replaced = null;
        _repository.Setup(r => r.UpdateAsync(It.IsAny<Document>()))
            .Callback<Document>(d => replaced = d)
            .ReturnsAsync(true);

        var result = await _service.SaveAsync(SaveRequest("Agent A", "front-desk", "alice"));

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, replaced!.Id);
        Assert.Equal(existing.CreatedAt, replaced.CreatedAt);
        _repository.Verify(r => r.CreateAsync(It.IsAny<Document>()), Times.Never);
    }
}
