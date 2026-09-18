using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auditing;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

public class AuditLogServiceTests
{
    private const string FallbackAction = "TenantCreated";
    private const string EndpointAction = "CreateTenant";
    private const string EndpointSummary = "Creates a new tenant";

    [Fact]
    public async Task RecordEntryAsync_UsesEndpointMetadata_WhenHttpContextIsPresent()
    {
        var captured = await RecordAsync(httpContext: HttpContextWithEndpoint(EndpointAction, EndpointSummary));

        Assert.Equal(EndpointAction, captured.Action);
        Assert.Equal(EndpointSummary, captured.Description);
    }

    [Fact]
    public async Task RecordEntryAsync_UsesFallbackAction_WhenHttpContextIsNull()
    {
        var captured = await RecordAsync(httpContext: null);

        Assert.Equal(FallbackAction, captured.Action);
        Assert.Equal(string.Empty, captured.Description);
        Assert.Equal("participant-1", captured.ParticipantId);
        Assert.Equal("user-1", captured.LoggedInUser);
        Assert.Equal("test-tenant", captured.TenantId);
    }

    [Fact]
    public async Task RecordEntryAsync_UsesExplicitTenantId_InsteadOfAmbientContext()
    {
        var service = CreateService(httpContext: null);

        var result = await service.RecordEntryAsync(
            FallbackAction,
            tenantId: AuditLogTenants.Platform);

        Assert.True(result.IsSuccess);
        Assert.Equal(AuditLogTenants.Platform, result.Data!.TenantId);
        Assert.Equal("user-1", result.Data.LoggedInUser);
        Assert.NotEqual("test-tenant", result.Data.TenantId);
    }

    [Fact]
    public void Constructor_Throws_WhenRequiredDependenciesAreNull()
    {
        var repository = Mock.Of<IAuditLogRepository>();
        var tenantContext = Mock.Of<ITenantContext>();
        var accessor = Mock.Of<IHttpContextAccessor>();
        var logger = NullLogger<AuditLogService>.Instance;

        Assert.Throws<ArgumentNullException>(() =>
            new AuditLogService(null!, tenantContext, accessor, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new AuditLogService(repository, null!, accessor, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new AuditLogService(repository, tenantContext, null!, logger));
        Assert.Throws<ArgumentNullException>(() =>
            new AuditLogService(repository, tenantContext, accessor, null!));
    }

    [Fact]
    public async Task RecordEntryAsync_ReturnsBadRequest_WhenActionIsEmpty()
    {
        var service = CreateService(httpContext: null);

        var result = await service.RecordEntryAsync("   ");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
    }

    [Fact]
    public async Task RecordEntryAsync_SanitizesDetailKeysAndStringValues()
    {
        AuditLogEntry? captured = null;
        var service = CreateService(
            httpContext: null,
            onCreate: entry => captured = entry);

        var result = await service.RecordEntryAsync(
            FallbackAction,
            details: new Dictionary<string, object?>
            {
                ["  Tenant Id "] = " acme\u0001 ",
                ["Count"] = 3,
                ["\u0001"] = "dropped"
            });

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        Assert.Equal("acme", captured!.Details!["Tenant Id"]);
        Assert.Equal(3, captured.Details["Count"]);
        Assert.False(captured.Details.ContainsKey(""));
    }

    [Fact]
    public void ToDictionary_HumanizesSingleCharacterPropertyNames()
    {
        var details = AuditLogService.ToDictionary(new { A = 1, knowledgeId = 2 });

        Assert.Equal(1, details["A"]);
        Assert.Equal(2, details["Knowledge Id"]);
    }

    [Fact]
    public async Task RecordEntryAsync_DoesNotWaitForRepositoryWrite()
    {
        var writeStarted = new TaskCompletionSource();
        var writeMayFinish = new TaskCompletionSource();

        var service = CreateService(
            httpContext: null,
            createReturns: async () =>
            {
                writeStarted.TrySetResult();
                await writeMayFinish.Task;
            });

        var result = await service.RecordEntryAsync(FallbackAction);

        Assert.True(result.IsSuccess);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(writeMayFinish.Task.IsCompleted);

        writeMayFinish.SetResult();
    }

    private static async Task<AuditLogEntry> RecordAsync(HttpContext? httpContext)
    {
        AuditLogEntry? captured = null;
        var service = CreateService(httpContext, onCreate: entry => captured = entry);

        var result = await service.RecordEntryAsync(FallbackAction);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        return captured!;
    }

    private static AuditLogService CreateService(
        HttpContext? httpContext,
        Action<AuditLogEntry>? onCreate = null,
        Func<Task>? createReturns = null)
    {
        var repository = new Mock<IAuditLogRepository>();
        var setup = repository.Setup(r => r.CreateAsync(It.IsAny<AuditLogEntry>()));
        if (onCreate != null)
        {
            setup.Callback(onCreate);
        }

        setup.Returns((AuditLogEntry _) => createReturns != null ? createReturns() : Task.CompletedTask);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns("test-tenant");
        tenantContext.Setup(c => c.ParticipantId).Returns("participant-1");
        tenantContext.Setup(c => c.LoggedInUser).Returns("user-1");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        return new AuditLogService(
            repository.Object,
            tenantContext.Object,
            accessor.Object,
            NullLogger<AuditLogService>.Instance);
    }

    private static DefaultHttpContext HttpContextWithEndpoint(string name, string summary)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(new Endpoint(
            _ => Task.CompletedTask,
            new EndpointMetadataCollection(
                new NamedEndpoint(name),
                new SummarizedEndpoint(summary)),
            displayName: name));
        return httpContext;
    }

    private sealed class NamedEndpoint : IEndpointNameMetadata
    {
        public NamedEndpoint(string name) => EndpointName = name;
        public string EndpointName { get; }
    }

    private sealed class SummarizedEndpoint : IEndpointSummaryMetadata
    {
        public SummarizedEndpoint(string summary) => Summary = summary;
        public string Summary { get; }
    }
}
