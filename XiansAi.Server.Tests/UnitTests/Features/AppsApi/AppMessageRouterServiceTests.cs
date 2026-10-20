using Features.AppsApi.Services;
using Features.UserApi.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Auth;
using Shared.Data.Models;
using Shared.Repositories;
using Xunit;

namespace Tests.UnitTests.Features.AppsApi;

public class AppMessageRouterServiceTests
{
    private readonly Mock<ILogger<AppMessageRouterService>> _logger = new();
    private readonly Mock<IServiceScopeFactory> _scopeFactory = new();
    private readonly Mock<IAppIntegrationRepository> _repository = new();

    // RouteMessageAsync is private: drive it through the real publisher the service subscribes to.
    private async Task RouteAsync(MessageStreamEvent messageEvent)
    {
        var publisher = new MessageEventPublisher(NullLogger<MessageEventPublisher>.Instance);
        var service = new AppMessageRouterService(_logger.Object, _scopeFactory.Object, publisher);

        await service.StartAsync(CancellationToken.None);
        try
        {
            await publisher.PublishMessageAsync(messageEvent);
        }
        finally
        {
            await service.StopAsync(CancellationToken.None);
        }
    }

    private static MessageStreamEvent CreateEvent(string? origin, string? scope) => new()
    {
        GroupId = "group",
        Message = new ConversationMessage
        {
            ThreadId = "thread",
            TenantId = "tenant",
            CreatedAt = DateTime.UtcNow,
            CreatedBy = "agent",
            ParticipantId = "participant",
            WorkflowId = "workflow",
            WorkflowType = "workflow-type",
            Direction = MessageDirection.Outgoing,
            Origin = origin,
            Scope = scope
        }
    };

    private void VerifyNoScopeOrRepositoryAccess()
    {
        _scopeFactory.Verify(f => f.CreateScope(), Times.Never);
        _repository.VerifyNoOtherCalls();
    }

    private void VerifyNoWarningOrError() =>
        _logger.Verify(l => l.Log(
            It.Is<LogLevel>(level => level >= LogLevel.Warning),
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Acme AS")]
    [InlineData("Acme: Nordic")]
    [InlineData("slack:")]
    [InlineData(":general")]
    public async Task AgentInitiated_NonAppScope_IsSkippedSilently(string? scope)
    {
        await RouteAsync(CreateEvent("agent-initiated", scope));

        VerifyNoScopeOrRepositoryAccess();
        VerifyNoWarningOrError();
    }

    [Theory]
    [InlineData("slack:general")]
    [InlineData("Slack:general")]
    [InlineData("msteams:team:channel")]
    [InlineData("outlook:inbox")]
    public async Task AgentInitiated_SupportedPlatformScope_IsRouted(string scope)
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ITenantContext))).Returns(Mock.Of<ITenantContext>());
        serviceProvider.Setup(p => p.GetService(typeof(IAppIntegrationRepository))).Returns(_repository.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);
        _repository.Setup(r => r.GetByActivationAsync("tenant", It.IsAny<string>())).ReturnsAsync(new List<AppIntegration>());
        _repository.Setup(r => r.GetByIdAsync(It.IsAny<string>())).ReturnsAsync((AppIntegration?)null);

        await RouteAsync(CreateEvent("agent-initiated", scope));

        _scopeFactory.Verify(f => f.CreateScope(), Times.Once);
        _repository.Verify(r => r.GetByActivationAsync("tenant", It.IsAny<string>()), Times.Once);
    }

    [Theory]
    [InlineData("app:slack")]
    [InlineData("app:slack:int-1:extra")]
    public async Task AppOrigin_Malformed_LogsWarningAndSkips(string origin)
    {
        await RouteAsync(CreateEvent(origin, null));

        VerifyNoScopeOrRepositoryAccess();
        _logger.Verify(l => l.Log(
            LogLevel.Warning,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception?>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [Fact]
    public async Task AppOrigin_Valid_IsRouted()
    {
        var serviceProvider = new Mock<IServiceProvider>();
        serviceProvider.Setup(p => p.GetService(typeof(ITenantContext))).Returns(Mock.Of<ITenantContext>());
        serviceProvider.Setup(p => p.GetService(typeof(IAppIntegrationRepository))).Returns(_repository.Object);
        var serviceScope = new Mock<IServiceScope>();
        serviceScope.Setup(s => s.ServiceProvider).Returns(serviceProvider.Object);
        _scopeFactory.Setup(f => f.CreateScope()).Returns(serviceScope.Object);
        _repository.Setup(r => r.GetByIdAsync("int-1")).ReturnsAsync((AppIntegration?)null);

        await RouteAsync(CreateEvent("app:slack:int-1", null));

        _repository.Verify(r => r.GetByIdAsync("int-1"), Times.Once);
    }
}
