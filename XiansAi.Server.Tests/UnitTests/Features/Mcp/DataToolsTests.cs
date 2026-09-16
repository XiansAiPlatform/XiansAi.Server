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
using DocumentService = Features.AgentApi.Services.IDocumentService;

namespace XiansAi.Server.Tests.UnitTests.Features.Mcp;

public class DataToolsTests
{
    private readonly DefaultHttpContext _http = new();
    private readonly Mock<ITenantContext> _tenant = new();
    private readonly Mock<IPermissionsService> _permissions = new();
    private readonly Mock<IAgentRepository> _agents = new();
    private readonly Mock<IActivationRepository> _activations = new();
    private readonly Mock<IDocumentRepository> _documents = new();
    private readonly Mock<IAdminDataService> _data = new();
    private readonly Mock<DocumentService> _storage = new();
    private DataTools Tools() => new(new HttpContextAccessor { HttpContext = _http }, _tenant.Object,
        _permissions.Object, _agents.Object, _activations.Object, _data.Object, _documents.Object, _storage.Object);

    public DataToolsTests()
    {
        _http.Request.RouteValues["tenantId"] = "tenant";
        _http.Request.RouteValues["agentName"] = "agent";
        _http.Request.RouteValues["activationName"] = "activation";
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
        _http.Request.RouteValues["tenantId"] = "other";
        await Assert.ThrowsAsync<McpException>(() => Tools().ListDataTypes());
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task SaveRequiresWritePermission()
    {
        _permissions.Setup(x => x.HasWritePermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().SaveDataRecord("reports", JsonSerializer.SerializeToElement(new { value = 1 })));
        _storage.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DiscoveryRequiresReadPermission()
    {
        _permissions.Setup(x => x.HasReadPermission("agent")).ReturnsAsync(ServiceResult<bool>.Success(false));
        await Assert.ThrowsAsync<McpException>(() => Tools().ListDataTypes());
        _documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task DeleteAllowsConfirmedRecordInScope()
    {
        const string id = "0123456789abcdef01234567";
        _documents.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new Document
            { Id = id, TenantId = "tenant", AgentId = "agent", ActivationName = "activation" });
        _data.Setup(x => x.DeleteRecordAsync(It.IsAny<AdminDataDeleteRecordRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AdminDataDeleteRecordResponse>.Success(new() { Deleted = true, RecordId = id }));
        Assert.True((await Tools().DeleteDataRecord(id, true)).Deleted);
        _data.Verify(x => x.DeleteRecordAsync(It.Is<AdminDataDeleteRecordRequest>(r => r.TenantId == "tenant" &&
            r.RecordId == id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SaveSetsRouteScopeAndDoesNotOverwrite()
    {
        _storage.Setup(x => x.SaveAsync(It.IsAny<DocumentRequest<JsonElement>>()))
            .ReturnsAsync(ServiceResult<JsonElement>.Success(JsonSerializer.SerializeToElement(new { id = "saved" })));
        await Tools().SaveDataRecord("reports", JsonSerializer.SerializeToElement(new { value = 1 }));
        _storage.Verify(x => x.SaveAsync(It.Is<DocumentRequest<JsonElement>>(r =>
            r.Document.AgentId == "agent" && r.Document.ActivationName == "activation" && r.Document.Id == null &&
            r.Document.Type == "reports" && r.Options == null)), Times.Once);
    }

    [Fact]
    public async Task ListUsesRouteScope()
    {
        _data.Setup(x => x.GetDataAsync(It.IsAny<AdminDataListRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ServiceResult<AdminDataListResponse>.Success(new()));
        await Tools().ListDataRecords("reports", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow);
        _data.Verify(x => x.GetDataAsync(It.Is<AdminDataListRequest>(r => r.TenantId == "tenant" &&
            r.AgentName == "agent" && r.ActivationName == "activation" && r.Limit == 20), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeletionRequiresConfirmation()
    {
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecord("0123456789abcdef01234567"));
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecords("reports", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow));
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
        _documents.Setup(x => x.GetByIdAsync(id)).ReturnsAsync(new Document
            { Id = id, TenantId = tenant, AgentId = agent, ActivationName = activation });
        await Assert.ThrowsAsync<McpException>(() => Tools().DeleteDataRecord(id, true));
        _data.VerifyNoOtherCalls();
    }
}
