using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
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

        var repository = new Mock<IAuditLogRepository>();
        repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLogEntry>()))
            .Returns(async () =>
            {
                writeStarted.TrySetResult();
                await writeMayFinish.Task;
            });

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns("test-tenant");
        tenantContext.Setup(c => c.ParticipantId).Returns("participant-1");
        tenantContext.Setup(c => c.LoggedInUser).Returns("user-1");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns((HttpContext?)null);

        var service = new AuditLogService(
            repository.Object,
            tenantContext.Object,
            accessor.Object,
            NullLogger<AuditLogService>.Instance);

        var result = await service.RecordEntryAsync(FallbackAction);

        Assert.True(result.IsSuccess);
        await writeStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(writeMayFinish.Task.IsCompleted);

        writeMayFinish.SetResult();
    }

    private static async Task<AuditLogEntry> RecordAsync(HttpContext? httpContext)
    {
        AuditLogEntry? captured = null;
        var repository = new Mock<IAuditLogRepository>();
        repository
            .Setup(r => r.CreateAsync(It.IsAny<AuditLogEntry>()))
            .Callback<AuditLogEntry>(entry => captured = entry)
            .Returns(Task.CompletedTask);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns("test-tenant");
        tenantContext.Setup(c => c.ParticipantId).Returns("participant-1");
        tenantContext.Setup(c => c.LoggedInUser).Returns("user-1");

        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(httpContext);

        var service = new AuditLogService(
            repository.Object,
            tenantContext.Object,
            accessor.Object,
            NullLogger<AuditLogService>.Instance);

        var result = await service.RecordEntryAsync(FallbackAction);

        Assert.True(result.IsSuccess);
        Assert.NotNull(captured);
        return captured!;
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
