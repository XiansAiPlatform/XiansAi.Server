using System.Text.Json;
using Features.AgentApi.Repositories;
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

public class AdminDataServiceTests
{
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly AdminDataService _service;

    public AdminDataServiceTests()
    {
        _tenantContext.SetupGet(c => c.LoggedInUser).Returns("admin-user");
        _service = new AdminDataService(
            _documents.Object,
            _agents.Object,
            _tenantContext.Object,
            NullLogger<AdminDataService>.Instance);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateDataAsync_Rejects_Empty_Tenant(string tenantId)
    {
        var result = await _service.CreateDataAsync(tenantId, ValidCreateRequest());

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal("TenantId is required", result.ErrorMessage);
        _documents.Verify(r => r.CreateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Theory]
    [InlineData("undefined")]
    [InlineData("null")]
    public async Task CreateDataAsync_Rejects_Placeholder_Tenant(string tenantId)
    {
        var result = await _service.CreateDataAsync(tenantId, ValidCreateRequest());

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal("Invalid TenantId provided", result.ErrorMessage);
    }

    [Theory]
    [InlineData(null, "Companies", "key-1", true, "AgentName is required")]
    [InlineData("Agent", null, "key-1", true, "DataType is required")]
    [InlineData("Agent", "Companies", null, true, "Key is required")]
    [InlineData("Agent", "Companies", "key-1", false, "Content is required")]
    public async Task CreateDataAsync_Rejects_Missing_Fields(
        string? agentName,
        string? dataType,
        string? key,
        bool includeContent,
        string expectedError)
    {
        var request = new AdminDataCreateRequest
        {
            AgentName = agentName ?? string.Empty,
            DataType = dataType ?? string.Empty,
            Key = key ?? string.Empty,
            Content = includeContent ? JsonSerializer.SerializeToElement(new { status = "ok" }) : default
        };

        var result = await _service.CreateDataAsync("tenant-a", request);

        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Equal(expectedError, result.ErrorMessage);
        _documents.Verify(r => r.CreateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task CreateDataAsync_Returns_NotFound_When_Agent_Missing()
    {
        _agents.Setup(r => r.GetByNameInternalAsync("CustomerSupportAgent", "tenant-a"))
            .ReturnsAsync((Agent?)null);

        var result = await _service.CreateDataAsync("tenant-a", ValidCreateRequest());

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        _documents.Verify(r => r.CreateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task CreateDataAsync_Returns_Conflict_On_Duplicate_Type_And_Key()
    {
        SetupAgent("tenant-a", "CustomerSupportAgent");
        _documents.Setup(r => r.GetByKeyAsync("Companies", "acme-2026-01", "tenant-a"))
            .ReturnsAsync(new Document { Id = ObjectId.GenerateNewId().ToString(), TenantId = "tenant-a" });

        var result = await _service.CreateDataAsync("tenant-a", ValidCreateRequest());

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _documents.Verify(r => r.CreateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task CreateDataAsync_Persists_Route_Tenant_And_Actor()
    {
        SetupAgent("tenant-a", "CustomerSupportAgent");
        _documents.Setup(r => r.GetByKeyAsync("Companies", "acme-2026-01", "tenant-a"))
            .ReturnsAsync((Document?)null);

        Document? captured = null;
        _documents.Setup(r => r.CreateAsync(It.IsAny<Document>()))
            .Callback<Document>(d => captured = d)
            .ReturnsAsync((Document d) =>
            {
                d.Id = ObjectId.GenerateNewId().ToString();
                d.CreatedAt = DateTime.UtcNow;
                return d;
            });

        var result = await _service.CreateDataAsync("tenant-a", ValidCreateRequest());

        Assert.True(result.IsSuccess);
        Assert.Equal(StatusCode.Created, result.StatusCode);
        Assert.NotNull(captured);
        Assert.Equal("tenant-a", captured!.TenantId);
        Assert.Equal("CustomerSupportAgent", captured.AgentId);
        Assert.Equal("Companies", captured.Type);
        Assert.Equal("acme-2026-01", captured.Key);
        Assert.Equal("admin-user", captured.CreatedBy);
        Assert.Equal("admin-user", captured.UpdatedBy);
        Assert.Equal("CustomerSupportAgent", result.Data!.AgentName);
        Assert.Equal("Companies", result.Data.Type);
    }

    [Fact]
    public async Task GetRecordAsync_Returns_NotFound_For_Other_Tenant()
    {
        var recordId = ObjectId.GenerateNewId().ToString();
        _documents.Setup(r => r.GetByIdAsync(recordId))
            .ReturnsAsync(new Document
            {
                Id = recordId,
                TenantId = "tenant-b",
                AgentId = "Agent",
                Type = "Companies",
                Key = "k1"
            });

        var result = await _service.GetRecordAsync("tenant-a", recordId);

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
    }

    [Fact]
    public async Task UpdateDataAsync_Updates_Content_And_Metadata_Without_Changing_Identity()
    {
        var recordId = ObjectId.GenerateNewId().ToString();
        var existing = new Document
        {
            Id = recordId,
            TenantId = "tenant-a",
            AgentId = "CustomerSupportAgent",
            Type = "Companies",
            Key = "acme-2026-01",
            CreatedBy = "original-user",
            CreatedAt = DateTime.UtcNow.AddDays(-1)
        };

        _documents.Setup(r => r.GetByIdAsync(recordId)).ReturnsAsync(existing);

        Document? captured = null;
        _documents.Setup(r => r.UpdateAsync(It.IsAny<Document>()))
            .Callback<Document>(d => captured = d)
            .ReturnsAsync(true);

        var request = new AdminDataUpdateRequest
        {
            Content = JsonSerializer.SerializeToElement(new { status = "Updated" }),
            Metadata = new Dictionary<string, object> { ["city"] = "Bergen" }
        };

        var result = await _service.UpdateDataAsync("tenant-a", recordId, request);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("tenant-a", captured!.TenantId);
        Assert.Equal("CustomerSupportAgent", captured.AgentId);
        Assert.Equal(recordId, captured.Id);
        Assert.Equal("original-user", captured.CreatedBy);
        Assert.Equal("admin-user", captured.UpdatedBy);
        Assert.NotNull(captured.UpdatedAt);
        Assert.Equal("Updated", result.Data!.Content.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UpdateDataAsync_Returns_NotFound_For_Unknown_Id()
    {
        var recordId = ObjectId.GenerateNewId().ToString();
        _documents.Setup(r => r.GetByIdAsync(recordId)).ReturnsAsync((Document?)null);

        var result = await _service.UpdateDataAsync("tenant-a", recordId, new AdminDataUpdateRequest());

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        _documents.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDataAsync_Returns_NotFound_For_Other_Tenant()
    {
        var recordId = ObjectId.GenerateNewId().ToString();
        _documents.Setup(r => r.GetByIdAsync(recordId))
            .ReturnsAsync(new Document { Id = recordId, TenantId = "tenant-b" });

        var result = await _service.UpdateDataAsync("tenant-a", recordId, new AdminDataUpdateRequest());

        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        _documents.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDataAsync_Returns_Conflict_When_Type_Key_Collides()
    {
        var recordId = ObjectId.GenerateNewId().ToString();
        var otherId = ObjectId.GenerateNewId().ToString();
        _documents.Setup(r => r.GetByIdAsync(recordId))
            .ReturnsAsync(new Document
            {
                Id = recordId,
                TenantId = "tenant-a",
                AgentId = "CustomerSupportAgent",
                Type = "Companies",
                Key = "original-key"
            });
        _documents.Setup(r => r.GetByKeyAsync("Companies", "taken-key", "tenant-a"))
            .ReturnsAsync(new Document { Id = otherId, TenantId = "tenant-a" });

        var result = await _service.UpdateDataAsync("tenant-a", recordId, new AdminDataUpdateRequest
        {
            Key = "taken-key"
        });

        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        _documents.Verify(r => r.UpdateAsync(It.IsAny<Document>()), Times.Never);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateDataAsync_Does_Not_Blank_Existing_Key(string blankKey)
    {
        var recordId = ObjectId.GenerateNewId().ToString();
        var existing = new Document
        {
            Id = recordId,
            TenantId = "tenant-a",
            AgentId = "CustomerSupportAgent",
            Type = "Companies",
            Key = "acme-2026-01"
        };

        _documents.Setup(r => r.GetByIdAsync(recordId)).ReturnsAsync(existing);

        Document? captured = null;
        _documents.Setup(r => r.UpdateAsync(It.IsAny<Document>()))
            .Callback<Document>(d => captured = d)
            .ReturnsAsync(true);

        var result = await _service.UpdateDataAsync("tenant-a", recordId, new AdminDataUpdateRequest
        {
            Key = blankKey,
            Content = JsonSerializer.SerializeToElement(new { status = "Updated" })
        });

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("acme-2026-01", captured!.Key);
        _documents.Verify(r => r.GetByKeyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>()), Times.Never);
    }

    private void SetupAgent(string tenantId, string agentName)
    {
        _agents.Setup(r => r.GetByNameInternalAsync(agentName, tenantId))
            .ReturnsAsync(new Agent
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = agentName,
                Tenant = tenantId,
                CreatedBy = "admin-user"
            });
    }

    private static AdminDataCreateRequest ValidCreateRequest()
    {
        return new AdminDataCreateRequest
        {
            AgentName = "CustomerSupportAgent",
            DataType = "Companies",
            Key = "acme-2026-01",
            Content = JsonSerializer.SerializeToElement(new { status = "Completed" }),
            Metadata = new Dictionary<string, object> { ["city"] = "Oslo" }
        };
    }
}
