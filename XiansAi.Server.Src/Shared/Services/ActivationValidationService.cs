using Shared.Repositories;
using Shared.Providers;
using Shared.Utils.Services;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// Validates activation state for webhook and message routing.
/// Uses a shared generic cache for consistent performance across webhooks and Admin API.
/// </summary>
public interface IActivationValidationService
{
    /// <summary>
    /// Validates that the specified activation exists and is active.
    /// Use when routing webhooks, messages, or API requests to a specific activation instance.
    /// Results are cached to reduce database load on repeated calls.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="agentName">The agent name.</param>
    /// <param name="activationName">The activation name (workflow ID postfix).</param>
    /// <param name="workflowType">Optional. When provided, validates that the agent has a flow definition for this workflow type.</param>
    /// <returns>Success if activation exists and is active; NotFound if not found; Conflict if deactivated; BadRequest if workflow type not registered for agent.</returns>
    Task<ServiceResult> ValidateActivationAsync(string tenantId, string agentName, string activationName, string? workflowType = null);

    /// <summary>
    /// Resolves the flow name to use for conversation routing.
    /// Conversational capability comes from <c>IsBuiltIn</c> on the flow definition.
    /// The well-known name "Supervisor Workflow" is also treated as conversational for backward compatibility.
    /// When <paramref name="workflowType"/> is provided, that definition must be built-in (or Supervisor Workflow).
    /// When omitted, the agent's unique built-in workflow is used, falling back to Supervisor Workflow.
    /// </summary>
    /// <returns>The short flow name (without the agent prefix) on success.</returns>
    Task<ServiceResult<string>> ResolveConversationalWorkflowAsync(string tenantId, string agentName, string? workflowType);

    /// <summary>
    /// Validates that a message target (workflow id + type) is routable:
    /// always checks that the agent has a registered flow definition for the workflow type;
    /// when the workflow id has an activation postfix, also checks that the activation exists and is active.
    /// Results are cached to avoid a database round-trip on every inbound message.
    /// </summary>
    /// <param name="tenantId">The tenant ID.</param>
    /// <param name="workflowId">Fully qualified workflow id (<c>tenant:Agent:Flow[:Postfix]</c>).</param>
    /// <param name="workflowType">Workflow type in either <c>Agent:Flow</c> or <c>Flow</c> form.</param>
    Task<ServiceResult> ValidateWorkflowTargetAsync(string tenantId, string workflowId, string workflowType);

    /// <summary>
    /// Invalidates the cached validation result for an activation.
    /// Call when an activation is deactivated or deleted to ensure subsequent requests fail immediately.
    /// </summary>
    void InvalidateActivationCache(string tenantId, string agentName, string activationName);

    /// <summary>
    /// Invalidates the cached registered workflow-type list for an agent.
    /// Call when flow definitions are created, updated, or deleted for the agent.
    /// </summary>
    void InvalidateAgentWorkflowTypesCache(string tenantId, string agentName);
}

public class ActivationValidationService : IActivationValidationService
{
    private const string CacheKeyPrefix = "activation:validation:";
    // Per-agent list of registered workflow types (invalidatable by agent alone).
    private const string AgentWorkflowTypesCacheKeyPrefix = "activation:agent-workflow-types:";
    private const double DefaultCacheMinutes = 5;
    private const double DefaultWorkflowTypeCacheMinutes = 15;
    // Older agents registered the conversational workflow under this name without isBuiltIn.
    private const string LegacySupervisorWorkflowName = "Supervisor Workflow";

    private readonly IActivationRepository _activationRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IAsyncResultCache _cache;
    private readonly ILogger<ActivationValidationService> _logger;
    private readonly ICacheInvalidationBus _invalidationBus;

    // Activation state and flow definitions change rarely and every mutation path invalidates the
    // relevant key explicitly, so these durations only bound staleness for server instances that
    // did not handle the mutation. Configurable so deployments can trade freshness for latency.
    private readonly TimeSpan _cacheDuration;
    private readonly TimeSpan _workflowTypeCacheDuration;

    public ActivationValidationService(
        IActivationRepository activationRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        IAsyncResultCache cache,
        ILogger<ActivationValidationService> logger,
        IConfiguration configuration,
        ICacheInvalidationBus invalidationBus)
    {
        _activationRepository = activationRepository ?? throw new ArgumentNullException(nameof(activationRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _invalidationBus = invalidationBus ?? throw new ArgumentNullException(nameof(invalidationBus));

        if (configuration == null) throw new ArgumentNullException(nameof(configuration));

        _cacheDuration = TimeSpan.FromMinutes(
            configuration.GetValue("Messaging:ActivationCacheMinutes", DefaultCacheMinutes));
        _workflowTypeCacheDuration = TimeSpan.FromMinutes(
            configuration.GetValue("Messaging:WorkflowTypeCacheMinutes", DefaultWorkflowTypeCacheMinutes));
    }

    public async Task<ServiceResult> ValidateActivationAsync(string tenantId, string agentName, string activationName, string? workflowType = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult.Failure("TenantId is required", StatusCode.BadRequest);
        if (string.IsNullOrWhiteSpace(agentName))
            return ServiceResult.Failure("AgentName is required", StatusCode.BadRequest);
        if (string.IsNullOrWhiteSpace(activationName))
            return ServiceResult.Failure("ActivationName is required", StatusCode.BadRequest);

        var cacheKey = BuildCacheKey(tenantId, agentName, activationName);
        var result = await _cache.GetOrAddAsync(
            cacheKey,
            _ => ValidateFromRepositoryAsync(tenantId, agentName, activationName),
            _cacheDuration,
            size: 1);
        if (!result.IsSuccess)
            return result;

        // Optionally validate that the agent has the requested workflow type registered.
        if (!string.IsNullOrWhiteSpace(workflowType))
        {
            var workflowCheck = await ValidateWorkflowTypeRegisteredAsync(tenantId, agentName, workflowType.Trim());
            if (!workflowCheck.IsSuccess)
                return workflowCheck;
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult<string>> ResolveConversationalWorkflowAsync(
        string tenantId, string agentName, string? workflowType)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult<string>.BadRequest("TenantId is required");
        if (string.IsNullOrWhiteSpace(agentName))
            return ServiceResult<string>.BadRequest("AgentName is required");

        try
        {
            var registered = await GetRegisteredWorkflowsAsync(tenantId, agentName);
            if (registered.Count == 0)
            {
                _logger.LogWarning(
                    "No workflow definitions found for agent '{AgentName}' in tenant {TenantId}",
                    LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
                return ServiceResult<string>.BadRequest(
                    $"No agent process registered for agent '{agentName}'. Unable to use this agent for this purpose.");
            }

            if (!string.IsNullOrWhiteSpace(workflowType))
                return ResolveSpecifiedConversationalWorkflow(agentName, workflowType.Trim(), registered);

            return ResolveDefaultConversationalWorkflow(agentName, registered);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error resolving conversational workflow for agent '{AgentName}' in tenant {TenantId}",
                LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
            return ServiceResult<string>.InternalServerError(
                "An error occurred while resolving the conversational workflow");
        }
    }

    public async Task<ServiceResult> ValidateWorkflowTargetAsync(string tenantId, string workflowId, string workflowType)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            return ServiceResult.Failure("TenantId is required", StatusCode.BadRequest);
        if (string.IsNullOrWhiteSpace(workflowId))
            return ServiceResult.Failure("WorkflowId is required", StatusCode.BadRequest);
        if (string.IsNullOrWhiteSpace(workflowType))
            return ServiceResult.Failure("WorkflowType is required", StatusCode.BadRequest);

        string agentName;
        try
        {
            agentName = WorkflowIdentifier.GetAgentName(workflowType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unable to parse agent name from workflow type '{WorkflowType}'", LogSanitizer.Sanitize(workflowType));
            return ServiceResult.Failure(
                $"Invalid workflow type '{workflowType}'",
                StatusCode.BadRequest);
        }

        if (string.IsNullOrWhiteSpace(agentName))
            return ServiceResult.Failure("AgentName could not be determined from WorkflowType", StatusCode.BadRequest);

        var workflowCheck = await ValidateWorkflowTypeRegisteredAsync(tenantId, agentName, workflowType);
        if (!workflowCheck.IsSuccess)
            return workflowCheck;

        var activationName = WorkflowIdentifier.GetIdPostfix(workflowId);
        if (!string.IsNullOrWhiteSpace(activationName))
        {
            return await ValidateActivationAsync(tenantId, agentName, activationName, workflowType: null);
        }

        return ServiceResult.Success();
    }

    public void InvalidateActivationCache(string tenantId, string agentName, string activationName)
    {
        var cacheKey = BuildCacheKey(tenantId, agentName, activationName);
        _cache.Remove(cacheKey);
        _ = _invalidationBus.PublishAsync(new CacheInvalidationEnvelope(
            CacheInvalidationType.Activation,
            UserId: null,
            TenantId: tenantId,
            Keys: [cacheKey],
            DateTimeOffset.UtcNow));
    }

    public void InvalidateAgentWorkflowTypesCache(string tenantId, string agentName)
    {
        var cacheKey = BuildAgentWorkflowTypesCacheKey(tenantId, agentName);
        _cache.Remove(cacheKey);
        _ = _invalidationBus.PublishAsync(new CacheInvalidationEnvelope(
            CacheInvalidationType.AgentWorkflowTypes,
            UserId: null,
            TenantId: tenantId,
            Keys: [cacheKey],
            DateTimeOffset.UtcNow));
    }

    private static string BuildCacheKey(string tenantId, string agentName, string activationName)
        => $"{CacheKeyPrefix}{tenantId}\x01{agentName}\x01{activationName}";

    private static string BuildAgentWorkflowTypesCacheKey(string tenantId, string agentName)
        => $"{AgentWorkflowTypesCacheKeyPrefix}{tenantId}\x01{agentName}";

    /// <summary>
    /// Normalizes a workflow type that may arrive as either <c>Agent:Flow</c> or just <c>Flow</c>
    /// into the full <c>Agent:Flow</c> form used in flow definitions.
    /// </summary>
    private static string NormalizeFullWorkflowType(string agentName, string workflowType)
    {
        var trimmed = workflowType.Trim();
        var agentPrefix = agentName + ":";
        if (trimmed.StartsWith(agentPrefix, StringComparison.Ordinal))
        {
            return trimmed;
        }
        return $"{agentName}:{trimmed}";
    }

    private async Task<ServiceResult> ValidateWorkflowTypeRegisteredAsync(string tenantId, string agentName, string workflowType)
    {
        try
        {
            var registered = await GetRegisteredWorkflowsAsync(tenantId, agentName);
            if (registered.Count == 0)
            {
                _logger.LogWarning(
                    "No workflow definitions found for agent '{AgentName}' in tenant {TenantId}",
                    LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
                return ServiceResult.Failure(
                    $"No agent process registered for agent '{agentName}'. Unable to use this agent for this purpose.",
                    StatusCode.BadRequest);
            }

            var fullWorkflowType = NormalizeFullWorkflowType(agentName, workflowType);
            if (FindRegisteredWorkflow(registered, fullWorkflowType) != null)
                return ServiceResult.Success();

            var registeredList = FormatRegisteredFlowNames(agentName, registered);
            _logger.LogWarning(
                "Workflow type '{WorkflowType}' is not registered for agent '{AgentName}'. Registered types: {Registered}",
                LogSanitizer.Sanitize(fullWorkflowType), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(registeredList));
            return ServiceResult.Failure(
                $"Workflow type '{fullWorkflowType}' is not registered for agent '{agentName}'. Registered workflow types: {registeredList}.",
                StatusCode.BadRequest);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error validating workflow type '{WorkflowType}' for agent '{AgentName}' in tenant {TenantId}",
                LogSanitizer.Sanitize(workflowType), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
            return ServiceResult.InternalServerError(
                "An error occurred while validating the workflow type");
        }
    }

    private async Task<List<RegisteredWorkflow>> GetRegisteredWorkflowsAsync(string tenantId, string agentName)
    {
        var cacheKey = BuildAgentWorkflowTypesCacheKey(tenantId, agentName);
        return await _cache.GetOrAddAsync(
            cacheKey,
            _ => LoadRegisteredWorkflowsAsync(tenantId, agentName),
            _workflowTypeCacheDuration,
            size: 1);
    }

    private async Task<List<RegisteredWorkflow>> LoadRegisteredWorkflowsAsync(string tenantId, string agentName)
    {
        var flowDefinitions = await _flowDefinitionRepository.GetByNameAsync(agentName, tenantId);
        if (flowDefinitions == null || flowDefinitions.Count == 0)
        {
            return new List<RegisteredWorkflow>();
        }

        return flowDefinitions
            .Where(fd => !string.IsNullOrEmpty(fd.WorkflowType))
            .GroupBy(fd => fd.WorkflowType, StringComparer.Ordinal)
            .Select(group => group.First())
            .Select(fd => new RegisteredWorkflow(fd.WorkflowType, fd.IsBuiltIn))
            .ToList();
    }

    private ServiceResult<string> ResolveSpecifiedConversationalWorkflow(
        string agentName, string workflowType, List<RegisteredWorkflow> registered)
    {
        var fullWorkflowType = NormalizeFullWorkflowType(agentName, workflowType);
        var match = FindRegisteredWorkflow(registered, fullWorkflowType);
        if (match == null)
        {
            var registeredList = FormatRegisteredFlowNames(agentName, registered);
            _logger.LogWarning(
                "Workflow type '{WorkflowType}' is not registered for agent '{AgentName}'. Registered types: {Registered}",
                LogSanitizer.Sanitize(fullWorkflowType), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(registeredList));
            return ServiceResult<string>.BadRequest(
                $"Workflow type '{fullWorkflowType}' is not registered for agent '{agentName}'. Registered workflow types: {registeredList}.");
        }

        if (!HasConversationalCapability(agentName, match))
        {
            var flowName = ToFlowName(agentName, match.FullType);
            _logger.LogWarning(
                "Workflow type '{WorkflowType}' for agent '{AgentName}' is not built-in and cannot be used for conversations",
                LogSanitizer.Sanitize(match.FullType), LogSanitizer.Sanitize(agentName));
            return ServiceResult<string>.BadRequest(
                $"Workflow type '{flowName}' does not have conversational capability. Only built-in workflows can be used for conversations.");
        }

        return ServiceResult<string>.Success(ToFlowName(agentName, match.FullType));
    }

    private static ServiceResult<string> ResolveDefaultConversationalWorkflow(
        string agentName, List<RegisteredWorkflow> registered)
    {
        var builtIn = registered.Where(workflow => workflow.IsBuiltIn).ToList();
        if (builtIn.Count == 1)
            return ServiceResult<string>.Success(ToFlowName(agentName, builtIn[0].FullType));

        if (builtIn.Count > 1)
        {
            var names = string.Join(", ", builtIn
                .Select(workflow => ToFlowName(agentName, workflow.FullType))
                .OrderBy(name => name, StringComparer.Ordinal));
            return ServiceResult<string>.BadRequest(
                $"Agent '{agentName}' has multiple built-in conversational workflows: {names}. Specify workflowType.");
        }

        var supervisor = registered.FirstOrDefault(workflow =>
            IsLegacySupervisorWorkflow(agentName, workflow.FullType));
        if (supervisor != null)
            return ServiceResult<string>.Success(LegacySupervisorWorkflowName);

        return ServiceResult<string>.BadRequest(
            $"Agent '{agentName}' has no built-in workflow with conversational capability.");
    }

    private static bool HasConversationalCapability(string agentName, RegisteredWorkflow workflow)
    {
        return workflow.IsBuiltIn || IsLegacySupervisorWorkflow(agentName, workflow.FullType);
    }

    private static bool IsLegacySupervisorWorkflow(string agentName, string fullWorkflowType)
    {
        return string.Equals(
            ToFlowName(agentName, fullWorkflowType),
            LegacySupervisorWorkflowName,
            StringComparison.Ordinal);
    }

    private static RegisteredWorkflow? FindRegisteredWorkflow(
        List<RegisteredWorkflow> registered, string fullWorkflowType)
    {
        return registered.FirstOrDefault(workflow =>
            string.Equals(workflow.FullType, fullWorkflowType, StringComparison.Ordinal));
    }

    private static string FormatRegisteredFlowNames(string agentName, List<RegisteredWorkflow> registered)
    {
        var displayTypes = registered
            .Select(workflow => ToFlowName(agentName, workflow.FullType))
            .Where(name => !string.IsNullOrEmpty(name))
            .Distinct()
            .OrderBy(name => name)
            .ToList();
        return displayTypes.Count > 0 ? string.Join(", ", displayTypes) : "none";
    }

    private static string ToFlowName(string agentName, string fullWorkflowType)
    {
        var agentPrefix = agentName + ":";
        if (fullWorkflowType.StartsWith(agentPrefix, StringComparison.Ordinal))
            return fullWorkflowType[agentPrefix.Length..];
        return fullWorkflowType;
    }

    private sealed record RegisteredWorkflow(string FullType, bool IsBuiltIn);

    private async Task<ServiceResult> ValidateFromRepositoryAsync(string tenantId, string agentName, string activationName)
    {
        try
        {
            var activation = await _activationRepository.GetByNameAndAgentAsync(tenantId, agentName, activationName);
            if (activation == null)
            {
                _logger.LogWarning(
                    "Activation '{ActivationName}' not found for agent '{AgentName}' in tenant {TenantId}",
                    LogSanitizer.Sanitize(activationName), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
                return ServiceResult.Failure(
                    $"Activation '{activationName}' not found for agent '{agentName}'",
                    StatusCode.NotFound);
            }

            if (!activation.IsActive)
            {
                _logger.LogWarning(
                    "Activation '{ActivationName}' for agent '{AgentName}' is deactivated",
                    LogSanitizer.Sanitize(activationName), LogSanitizer.Sanitize(agentName));
                return ServiceResult.Failure(
                    $"Activation '{activationName}' is deactivated",
                    StatusCode.Conflict);
            }

            return ServiceResult.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error validating activation '{ActivationName}' for agent '{AgentName}' in tenant {TenantId}",
                LogSanitizer.Sanitize(activationName), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
            return ServiceResult.InternalServerError(
                "An error occurred while validating the activation");
        }
    }
}
