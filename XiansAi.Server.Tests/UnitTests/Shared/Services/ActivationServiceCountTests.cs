using Features.WebApi.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

/// <summary>
/// Unit tests for ActivationService.GetActivationCountsByAgentAsync: input normalisation, result shaping,
/// limits and error handling. The repository is mocked; the Mongo query itself is covered by
/// AdminAgentActivationEndpointsTests.
/// </summary>
public class ActivationServiceCountTests
{
    private const string TenantId = "test-tenant";

    private readonly Mock<IActivationRepository> _repository = new();
    private readonly ActivationService _service;

    /// <summary>Names the repository was last called with.</summary>
    private IReadOnlyCollection<string>? _queriedNames;

    public ActivationServiceCountTests()
    {
        SetupCounts(new Dictionary<string, AgentActivationCount>());

        _service = new ActivationService(
            _repository.Object,
            Mock.Of<IAgentRepository>(),
            Mock.Of<IFlowDefinitionRepository>(),
            Mock.Of<IWorkflowStarterService>(),
            Mock.Of<IActivationCleanupService>(),
            Mock.Of<IActivationValidationService>(),
            Mock.Of<IWebhookEventPublisher>(),
            Mock.Of<IAppIntegrationService>(),
            Mock.Of<IAdminDataService>(),
            Mock.Of<IKnowledgeService>(),
            Mock.Of<IAdminLogsService>(),
            Mock.Of<IMessageService>(),
            Mock.Of<IAdminMetricsService>(),
            Mock.Of<IFeedbackService>(),
            Mock.Of<IAuditLogService>(),
            NullLogger<ActivationService>.Instance);
    }

    private void SetupCounts(Dictionary<string, AgentActivationCount> counts) =>
        _repository
            .Setup(r => r.CountByAgentNamesAsync(TenantId, It.IsAny<IReadOnlyCollection<string>?>()))
            .Callback<string, IReadOnlyCollection<string>?>((_, names) => _queriedNames = names)
            .ReturnsAsync(counts);

    [Fact]
    public async Task WithNames_ReturnsOneEntryPerNameInRequestOrder_IncludingZeroCounts()
    {
        SetupCounts(new()
        {
            ["AgentA"] = new(3, 1),
            ["AgentB"] = new(1, 1),
        });

        var result = await _service.GetActivationCountsByAgentAsync(TenantId, ["AgentB", "Missing", "AgentA"]);

        Assert.True(result.IsSuccess);
        Assert.Collection(result.Data!,
            c => AssertCount(c, "AgentB", 1, 1),
            c => AssertCount(c, "Missing", 0, 0),
            c => AssertCount(c, "AgentA", 3, 1));
    }

    [Fact]
    public async Task WithNames_SplitsCommasTrimsAndDropsDuplicatesAndEmptySegments()
    {
        var result = await _service.GetActivationCountsByAgentAsync(
            TenantId, ["AgentA, AgentB", " AgentC ", "AgentA", ",,", "  ", "AgentB,"]);

        Assert.True(result.IsSuccess);
        Assert.Equal(["AgentA", "AgentB", "AgentC"], _queriedNames);
        Assert.Equal(["AgentA", "AgentB", "AgentC"], result.Data!.Select(c => c.AgentName));
    }

    [Fact]
    public async Task WithNames_TreatsNamesCaseSensitively()
    {
        SetupCounts(new() { ["Agent"] = new(2, 0) });

        var result = await _service.GetActivationCountsByAgentAsync(TenantId, ["Agent", "agent"]);

        Assert.Collection(result.Data!,
            c => AssertCount(c, "Agent", 2, 0),
            c => AssertCount(c, "agent", 0, 0));
    }

    [Theory]
    [MemberData(nameof(NoNameInputs))]
    public async Task WithoutNames_ReturnsEveryAgentWithActivationsSortedByName(string[]? agentNames)
    {
        SetupCounts(new()
        {
            ["zeta"] = new(1, 0),
            ["Beta"] = new(4, 2),
            ["alpha"] = new(2, 2),
        });

        var result = await _service.GetActivationCountsByAgentAsync(TenantId, agentNames);

        Assert.True(result.IsSuccess);
        Assert.Empty(_queriedNames!);
        // Ordinal sort: upper-case before lower-case.
        Assert.Collection(result.Data!,
            c => AssertCount(c, "Beta", 4, 2),
            c => AssertCount(c, "alpha", 2, 2),
            c => AssertCount(c, "zeta", 1, 0));
    }

    public static TheoryData<string[]?> NoNameInputs() => new()
    {
        null,
        Array.Empty<string>(),
        new[] { "", " ", ",", " , " },
    };

    [Fact]
    public async Task WithoutNames_AndNoActivations_ReturnsEmptyList()
    {
        var result = await _service.GetActivationCountsByAgentAsync(TenantId, null);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task AtNameLimit_Succeeds()
    {
        var names = Enumerable.Range(0, ActivationService.MaxAgentNamesPerCountRequest).Select(i => $"agent-{i}");

        var result = await _service.GetActivationCountsByAgentAsync(TenantId, names);

        Assert.True(result.IsSuccess);
        Assert.Equal(ActivationService.MaxAgentNamesPerCountRequest, result.Data!.Count);
    }

    [Fact]
    public async Task OverNameLimit_ReturnsBadRequest_WithoutQuerying()
    {
        var names = Enumerable.Range(0, ActivationService.MaxAgentNamesPerCountRequest + 1).Select(i => $"agent-{i}");

        var result = await _service.GetActivationCountsByAgentAsync(TenantId, names);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        _repository.Verify(
            r => r.CountByAgentNamesAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>?>()),
            Times.Never);
    }

    [Fact]
    public async Task DuplicatesDoNotCountTowardsNameLimit()
    {
        var names = Enumerable.Repeat("AgentA", ActivationService.MaxAgentNamesPerCountRequest + 50);

        var result = await _service.GetActivationCountsByAgentAsync(TenantId, names);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Data!);
    }

    [Fact]
    public async Task WhenRepositoryThrows_ReturnsInternalServerError()
    {
        _repository
            .Setup(r => r.CountByAgentNamesAsync(TenantId, It.IsAny<IReadOnlyCollection<string>?>()))
            .ThrowsAsync(new InvalidOperationException("mongo down"));

        var result = await _service.GetActivationCountsByAgentAsync(TenantId, ["AgentA"]);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.InternalServerError, result.StatusCode);
        Assert.DoesNotContain("mongo down", result.ErrorMessage);
    }

    private static void AssertCount(AgentInstanceCount count, string agentName, long total, long active)
    {
        Assert.Equal(agentName, count.AgentName);
        Assert.Equal(total, count.InstanceCount);
        Assert.Equal(active, count.ActiveInstanceCount);
    }
}
