using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Shared.Data.Models;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.Services;
using Xunit;

namespace Tests.UnitTests.Shared.Services;

public class ActivationValidationServiceTests
{
    private const string TenantId = "acme";
    private const string AgentName = "My Agent";
    private const string FlowName = "Supervisor Workflow";
    private const string FullWorkflowType = $"{AgentName}:{FlowName}";
    private const string ActivationName = "prod";

    private readonly Mock<IActivationRepository> _activationRepository = new();
    private readonly Mock<IFlowDefinitionRepository> _flowDefinitionRepository = new();
    private readonly PassthroughAsyncResultCache _cache = new();
    private readonly ActivationValidationService _service;

    public ActivationValidationServiceTests()
    {
        _service = new ActivationValidationService(
            _activationRepository.Object,
            _flowDefinitionRepository.Object,
            _cache,
            NullLogger<ActivationValidationService>.Instance,
            new ConfigurationBuilder().Build());
    }

    [Fact]
    public async Task ValidateWorkflowTargetAsync_RejectsUnknownAgent_WithNoFlowDefinitions()
    {
        _flowDefinitionRepository
            .Setup(r => r.GetByNameAsync(AgentName, TenantId))
            .ReturnsAsync(new List<FlowDefinition>());

        var result = await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}",
            FullWorkflowType);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Contains(AgentName, result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateWorkflowTargetAsync_RejectsUnregisteredWorkflowType()
    {
        SeedFlowDefinitions($"{AgentName}:Other Flow");

        var result = await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}",
            FullWorkflowType);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Contains(FullWorkflowType, result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateWorkflowTargetAsync_SucceedsWithoutPostfix_WhenWorkflowTypeIsRegistered()
    {
        SeedFlowDefinitions(FullWorkflowType);

        var result = await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}",
            FullWorkflowType);

        Assert.True(result.IsSuccess);
        _activationRepository.Verify(
            r => r.GetByNameAndAgentAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidateActivationAsync_AcceptsShortWorkflowTypeForm()
    {
        SeedFlowDefinitions(FullWorkflowType);
        _activationRepository
            .Setup(r => r.GetByNameAndAgentAsync(TenantId, AgentName, ActivationName))
            .ReturnsAsync(new AgentActivation
            {
                Id = "activation-1",
                Name = ActivationName,
                AgentName = AgentName,
                CreatedBy = "tester",
                TenantId = TenantId,
                Active = true
            });

        // AdminApi passes the short flow name (without Agent: prefix).
        var result = await _service.ValidateActivationAsync(
            TenantId, AgentName, ActivationName, FlowName);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ValidateWorkflowTargetAsync_RejectsUnknownActivationName()
    {
        SeedFlowDefinitions(FullWorkflowType);
        _activationRepository
            .Setup(r => r.GetByNameAndAgentAsync(TenantId, AgentName, ActivationName))
            .ReturnsAsync((AgentActivation?)null);

        var result = await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}:{ActivationName}",
            FullWorkflowType);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.NotFound, result.StatusCode);
        Assert.Contains(ActivationName, result.ErrorMessage);
    }

    [Fact]
    public async Task ValidateWorkflowTargetAsync_RejectsDeactivatedActivation()
    {
        SeedFlowDefinitions(FullWorkflowType);
        _activationRepository
            .Setup(r => r.GetByNameAndAgentAsync(TenantId, AgentName, ActivationName))
            .ReturnsAsync(new AgentActivation
            {
                Id = "activation-1",
                Name = ActivationName,
                AgentName = AgentName,
                CreatedBy = "tester",
                TenantId = TenantId,
                Active = false
            });

        var result = await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}:{ActivationName}",
            FullWorkflowType);

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.Conflict, result.StatusCode);
        Assert.Contains("deactivated", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ValidateWorkflowTargetAsync_SucceedsWithActiveActivation()
    {
        SeedFlowDefinitions(FullWorkflowType);
        _activationRepository
            .Setup(r => r.GetByNameAndAgentAsync(TenantId, AgentName, ActivationName))
            .ReturnsAsync(new AgentActivation
            {
                Id = "activation-1",
                Name = ActivationName,
                AgentName = AgentName,
                CreatedBy = "tester",
                TenantId = TenantId,
                Active = true
            });

        var result = await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}:{ActivationName}",
            FullWorkflowType);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task ResolveConversationalWorkflowAsync_UsesRequestedBuiltInWorkflow()
    {
        SeedFlowDefinitions(
            (FullWorkflowType, true),
            ($"{AgentName}:Invoice Workflow", false));

        var result = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, "Invoice Workflow");

        Assert.False(result.IsSuccess);
        Assert.Equal(StatusCode.BadRequest, result.StatusCode);
        Assert.Contains("conversational capability", result.ErrorMessage);

        var builtInResult = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, FlowName);

        Assert.True(builtInResult.IsSuccess);
        Assert.Equal(FlowName, builtInResult.Data);
    }

    [Fact]
    public async Task ResolveConversationalWorkflowAsync_TreatsSupervisorNameAsBuiltInForBackwardCompatibility()
    {
        SeedFlowDefinitions(($"{AgentName}:Supervisor Workflow", false));

        var specified = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, "Supervisor Workflow");
        var omitted = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, workflowType: null);

        Assert.True(specified.IsSuccess);
        Assert.Equal("Supervisor Workflow", specified.Data);
        Assert.True(omitted.IsSuccess);
        Assert.Equal("Supervisor Workflow", omitted.Data);
    }

    [Fact]
    public async Task ResolveConversationalWorkflowAsync_WhenOmitted_PrefersExplicitBuiltInOverSupervisor()
    {
        SeedFlowDefinitions(
            ($"{AgentName}:Supervisor Workflow", false),
            ($"{AgentName}:Conversation Workflow 1", true));

        var result = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, workflowType: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Conversation Workflow 1", result.Data);
    }

    [Fact]
    public async Task ResolveConversationalWorkflowAsync_WhenOmitted_UsesUniqueBuiltInWorkflow()
    {
        SeedFlowDefinitions(
            ($"{AgentName}:Conversation Workflow 1", true),
            ($"{AgentName}:Invoice Workflow", false));

        var result = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, workflowType: null);

        Assert.True(result.IsSuccess);
        Assert.Equal("Conversation Workflow 1", result.Data);
    }

    [Fact]
    public async Task ResolveConversationalWorkflowAsync_WhenOmitted_RequiresUniqueBuiltInWorkflow()
    {
        SeedFlowDefinitions(
            ($"{AgentName}:Conversation Workflow 1", true),
            ($"{AgentName}:Conversation Workflow 2", true));

        var result = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, workflowType: null);

        Assert.False(result.IsSuccess);
        Assert.Contains("multiple built-in conversational workflows", result.ErrorMessage);
        Assert.Contains("Conversation Workflow 1", result.ErrorMessage);
        Assert.Contains("Conversation Workflow 2", result.ErrorMessage);
    }

    [Fact]
    public async Task ResolveConversationalWorkflowAsync_WhenOmitted_FailsWithoutBuiltInWorkflow()
    {
        SeedFlowDefinitions(($"{AgentName}:Invoice Workflow", false));

        var result = await _service.ResolveConversationalWorkflowAsync(TenantId, AgentName, workflowType: null);

        Assert.False(result.IsSuccess);
        Assert.Contains("no built-in workflow", result.ErrorMessage);
    }

    [Fact]
    public async Task InvalidateAgentWorkflowTypesCache_ClearsCachedList()
    {
        SeedFlowDefinitions(FullWorkflowType);

        // Prime the cache
        await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}",
            FullWorkflowType);

        _service.InvalidateAgentWorkflowTypesCache(TenantId, AgentName);

        // After invalidation, the next call should re-query the repository.
        _flowDefinitionRepository.Invocations.Clear();
        SeedFlowDefinitions(FullWorkflowType);

        var result = await _service.ValidateWorkflowTargetAsync(
            TenantId,
            $"{TenantId}:{FullWorkflowType}",
            FullWorkflowType);

        Assert.True(result.IsSuccess);
        _flowDefinitionRepository.Verify(r => r.GetByNameAsync(AgentName, TenantId), Times.Once);
    }

    private void SeedFlowDefinitions(params string[] workflowTypes)
    {
        SeedFlowDefinitions(workflowTypes.Select(wt => (wt, false)).ToArray());
    }

    private void SeedFlowDefinitions(params (string WorkflowType, bool IsBuiltIn)[] workflowTypes)
    {
        var definitions = workflowTypes.Select(wt => new FlowDefinition
        {
            Id = Guid.NewGuid().ToString("N"),
            Agent = AgentName,
            WorkflowType = wt.WorkflowType,
            Hash = Guid.NewGuid().ToString("N"),
            CreatedBy = "tester",
            Tenant = TenantId,
            IsBuiltIn = wt.IsBuiltIn,
            ActivityDefinitions = new List<ActivityDefinition>(),
            ParameterDefinitions = new List<ParameterDefinition>()
        }).ToList();

        _flowDefinitionRepository
            .Setup(r => r.GetByNameAsync(AgentName, TenantId))
            .ReturnsAsync(definitions);
    }

    /// <summary>
    /// Minimal IAsyncResultCache that always invokes the factory (no real caching),
    /// except it still honors Remove so Invalidate* tests can clear primed entries when
    /// combined with a real MemoryCache. For these unit tests we use a dictionary so
    /// InvalidateAgentWorkflowTypesCache actually drops a primed entry.
    /// </summary>
    private sealed class PassthroughAsyncResultCache : IAsyncResultCache
    {
        private readonly Dictionary<string, object> _store = new(StringComparer.Ordinal);

        public Task<T> GetOrAddAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan absoluteExpiration,
            long size = 1,
            CancellationToken cancellationToken = default) where T : class
        {
            if (_store.TryGetValue(key, out var cached) && cached is T typed)
            {
                return Task.FromResult(typed);
            }

            return AddAsync(key, factory, cancellationToken);
        }

        private async Task<T> AddAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            CancellationToken cancellationToken) where T : class
        {
            var value = await factory(cancellationToken);
            _store[key] = value;
            return value;
        }

        public void Remove(string key) => _store.Remove(key);
    }
}
