using System.Text.Json;
using Features.AgentApi.Models;
using Features.AgentApi.Repositories;
using Features.Mcp.Tools;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Microsoft.Extensions.Logging;
using DocumentService = Features.AgentApi.Services.IDocumentService;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class DataToolsTests
{
    private McpTarget _target = new("tenant", "agent", "activation");
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IPermissionsService> _permissions = new();
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IAdminDataService> _data = new();
    private readonly Mock<DocumentService> _storage = new();
    private readonly Mock<ILogger<DataTools>> _logger = new();
    private DataTools Tools(IAdminDataService? service = null) => new( _tenant.Object,
        _permissions.Object, _agents.Object, _activations.Object, service ?? _data.Object, _documents.Object, _storage.Object, _logger.Object);

    public DataToolsTests()
    {
        _tenant.SetupGet(x => x.TenantId).Returns("tenant");
        _tenant.SetupGet(x => x.LoggedInUser).Returns("user");
        _tenant.SetupGet(x => x.UserRoles).Returns([]);
        _permissions.Setup(x => x.HasReadPermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(true));
        _permissions.Setup(x => x.HasWritePermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(true));
        _agents.Setup(x => x.GetByNameAsync("agent", "tenant", "user", It.IsAny<string[]>()))
            .ReturnsAsync(new Agent { Id = "id", Name = "agent", Tenant = "tenant", CreatedBy = "user" });
        _activations.Setup(x => x.GetByNameAndAgentAsync("tenant", "agent", "activation"))
            .ReturnsAsync(new AgentActivation { Id = "id", Name = "activation", AgentName = "agent", TenantId = "tenant", CreatedBy = "user" });
    }

    [Fact]
    public async Task DiscoveryRejectsOtherTenant()
    {
        _target = _target with { TenantId = "other" };
        await Assert.ThrowsAsync<McpException>(() => Tools().ListDataTypes(_target));
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SaveRequiresWritePermission()
    {
        _permissions.Setup(x => x.HasWritePermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().SaveDataRecord(_target, "reports", "{\"value\":1}"));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DiscoveryRequiresReadPermission()
    {
        _permissions.Setup(x => x.HasReadPermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().ListDataTypes(_target));
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAllowsConfirmedRecordInScope()
    {
        const string id = "0123456789abcdef01234567";
        _data.Setup(x => x.DeleteRecordAsync(It.IsAny<AdminDataDeleteRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AdminDataDeleteRecordResponse>.Success(new() { Deleted = true, RecordId = id }));
        Assert.True((await Tools().DeleteDataRecord(_target, id, true)).Deleted);
        _data.Verify(x => x.DeleteRecordAsync(It.Is<AdminDataDeleteRecordRequest>(r => r.TenantId == "tenant" &&
            r.RecordId == id && r.AgentName == "agent" && r.ActivationName == "activation"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveSetsRouteScopeAndDoesNotOverwrite()
    {
        _storage.Setup(x => x.SaveAsync(It.IsAny<DocumentRequest<JsonElement>>()))
            .ReturnsAsync(ServiceResult<JsonElement>.Success(JsonSerializer.SerializeToElement(new { id = "saved" })));
        await Tools().SaveDataRecord(_target, "reports", "{\"value\":1}");
        _storage.Verify(x => x.SaveAsync(It.Is<DocumentRequest<JsonElement>>(r =>
            r.Document.AgentId == "agent" && r.Document.ActivationName == "activation" && r.Document.Id == null &&
            r.Document.Type == "reports" && r.Options == null &&
            r.Document.Content.ValueKind == JsonValueKind.Object && r.Document.Content.GetProperty("value").GetInt32() == 1)), Times.Once);
        _permissions.Verify(x => x.HasReadPermission(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task SaveAcceptsEmptyObjectText()
    {
        _storage.Setup(x => x.SaveAsync(It.IsAny<DocumentRequest<JsonElement>>()))
            .ReturnsAsync(ServiceResult<JsonElement>.Success(JsonSerializer.SerializeToElement(new { id = "saved" })));
        await Tools().SaveDataRecord(_target, "reports", "{}");
        _storage.Verify(x => x.SaveAsync(It.Is<DocumentRequest<JsonElement>>(r =>
            r.Document.Content.ValueKind == JsonValueKind.Object && r.Document.Content.GetRawText() == "{}")), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{invalid}")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("42")]
    [InlineData("true")]
    [InlineData("\"text\"")]
    public async Task SaveRejectsInvalidOrNonObjectText(string content)
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().SaveDataRecord(_target, "reports", content));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ListUsesRouteScope()
    {
        _data.Setup(x => x.GetDataAsync(It.IsAny<AdminDataListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AdminDataListResponse>.Success(new()));
        await Tools().ListDataRecords(_target, "reports", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
        _data.Verify(x => x.GetDataAsync(It.Is<AdminDataListRequest>(r => r.TenantId == "tenant" &&
            r.AgentName == "agent" && r.ActivationName == "activation" && r.Limit == 20), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("reports", -1, 20)]
    [InlineData("reports", 0, 0)]
    [InlineData("reports", 0, -1)]
    [InlineData("reports", 0, 101)]
    [InlineData("", 0, 20)]
    [InlineData(" ", 0, 20)]
    public async Task ListRejectsInvalidInputs(string dataType, int skip, int limit)
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().ListDataRecords(_target, dataType,
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, skip, limit));
        _data.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task SaveRequiresDataType(string dataType)
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().SaveDataRecord(_target, dataType, "{}"));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeletionRequiresConfirmation()
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecord(_target, "0123456789abcdef01234567"));
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecords(_target, "reports", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
        _documents.VerifyNoOtherCalls();
        _data.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("other", "agent", "activation")]
    [InlineData("tenant", "other", "activation")]
    [InlineData("tenant", "agent", "other")]
    public async Task DeleteRejectsRecordOutsideScope(string tenant, string agent, string activation)
    {
        const string id = "0123456789abcdef01234567";
        var record = new Document { Id = id, TenantId = tenant, AgentId = agent, ActivationName = activation };
        _documents.Setup(x => x.QueryAsync("tenant", It.IsAny<DocumentQueryFilter>()))
            .ReturnsAsync((string requestedTenant, DocumentQueryFilter filter) =>
            {
                if (record.TenantId == requestedTenant && record.AgentId == filter.AgentId && record.ActivationName == filter.ActivationName)
                    return [record];
                return [];
            });
        var service = new AdminDataService(_documents.Object, Mock.Of<ILogger<AdminDataService>>());
        await Assert.ThrowsAsync<McpException>(() => Tools(service).DeleteDataRecord(_target, id, true));
        _documents.Verify(x => x.QueryAsync("tenant", It.Is<DocumentQueryFilter>(f =>
            f.AgentId == "agent" && f.ActivationName == "activation" && f.Ids!.Single() == id)), Times.Once);
        _documents.Verify(x => x.GetByIdAsync(It.IsAny<string>()), Times.Never);
        _documents.Verify(x => x.DeleteByFilterAsync(It.IsAny<string>(), It.IsAny<DocumentQueryFilter>()), Times.Never);
    }

    [Fact]
    public async Task DataTypeDiscoveryReturnsActivationTypes()
    {
        _documents.Setup(x => x.GetDistinctTypesAsync("tenant", "agent", "activation")).ReturnsAsync(["reports"]);
        Assert.Equal(["reports"], await Tools().ListDataTypes(_target));
    }

    [Fact]
    public async Task DeleteRejectsInvalidObjectIdBeforeQuery()
    {
        var error = await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecord(_target, "invalid", true));
        Assert.Equal("Invalid record ID.", error.Message);
        _documents.VerifyNoOtherCalls();
        _data.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteQueriesAndDeletesWithExactScope()
    {
        const string id = "0123456789abcdef01234567";
        _documents.Setup(x => x.QueryAsync("tenant", It.IsAny<DocumentQueryFilter>())).ReturnsAsync([
            new Document { Id = id, TenantId = "tenant", AgentId = "agent", ActivationName = "activation" }
        ]);
        _documents.Setup(x => x.DeleteByFilterAsync("tenant", It.IsAny<DocumentQueryFilter>())).ReturnsAsync(1);
        var service = new AdminDataService(_documents.Object, Mock.Of<ILogger<AdminDataService>>());
        Assert.True((await Tools(service).DeleteDataRecord(_target, id, true)).Deleted);
        _documents.Verify(x => x.QueryAsync("tenant", It.Is<DocumentQueryFilter>(f =>
            f.Ids!.Single() == id && f.AgentId == "agent" && f.ActivationName == "activation" && f.Limit == 1)), Times.Once);
        _documents.Verify(x => x.DeleteByFilterAsync("tenant", It.Is<DocumentQueryFilter>(f =>
            f.Ids!.Single() == id && f.AgentId == "agent" && f.ActivationName == "activation")), Times.Once);
        _documents.Verify(x => x.GetByIdAsync(It.IsAny<string>()), Times.Never);
        _documents.Verify(x => x.DeleteAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    public async Task BulkDeleteRestrictsDeletionToPreviewedIdsAndAudits(int count)
    {
        var ids = Enumerable.Range(1, count).Select(n => n.ToString("x24")).ToList();
        _data.Setup(x => x.GetDataAsync(It.IsAny<AdminDataListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AdminDataListResponse>.Success(new()
                { Total = count, Data = ids.Select(id => new AdminDataItemResponse { Id = id }).ToList() }));
        _data.Setup(x => x.DeleteDataAsync(It.IsAny<AdminDataDeleteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AdminDataDeleteResponse>.Success(new() { DeletedCount = count }));
        var start = DateTimeOffset.UtcNow.AddDays(-1);
        var end = DateTimeOffset.UtcNow;
        Assert.Equal(count, (await Tools().DeleteDataRecords(_target, "reports", start, end, true)).DeletedCount);
        _data.Verify(x => x.GetDataAsync(It.Is<AdminDataListRequest>(r => r.Limit == 100 &&
            r.TenantId == "tenant" && r.AgentName == "agent" && r.ActivationName == "activation" && r.DataType == "reports"), It.IsAny<CancellationToken>()), Times.Once);
        _data.Verify(x => x.DeleteDataAsync(It.Is<AdminDataDeleteRequest>(r => r.RecordIds!.SequenceEqual(ids) &&
            r.TenantId == "tenant" && r.AgentName == "agent" && r.ActivationName == "activation" && r.DataType == "reports" &&
            r.StartDate == start.UtcDateTime && r.EndDate == end.UtcDateTime), It.IsAny<CancellationToken>()), Times.Once);
        VerifyAudit(true, count);
    }

    [Fact]
    public async Task BulkDeleteRejectsMoreThan100RecordsAndAudits()
    {
        _data.Setup(x => x.GetDataAsync(It.IsAny<AdminDataListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AdminDataListResponse>.Success(new() { Total = 101 }));
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecords(_target, "reports",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow, true));
        _data.Verify(x => x.DeleteDataAsync(It.IsAny<AdminDataDeleteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        VerifyAudit(false, 0);
    }

    [Fact]
    public async Task BulkDeleteAuditsMissingConfirmation()
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecords(_target, "reports",
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
        _data.VerifyNoOtherCalls();
        VerifyAudit(false, 0);
    }

    private void VerifyAudit(bool completed, int count) => _logger.Verify(x => x.Log(LogLevel.Information,
        It.IsAny<EventId>(), It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains("MCP bulk-delete audit") &&
            state.ToString()!.Contains("User=user") && state.ToString()!.Contains("Tenant=tenant") &&
            state.ToString()!.Contains("Completed=" + completed) && state.ToString()!.Contains("DeletedCount=" + count)),
        It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
}
