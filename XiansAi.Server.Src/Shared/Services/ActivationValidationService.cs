using Shared.Repositories;
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
    /// <param name="workflowType">Optional. When provided, validates that the agent has a flow definition for this workflow type (e.g. "Supervisor Workflow", "Integrator Workflow").</param>
    /// <returns>Success if activation exists and is active; NotFound if not found; Conflict if deactivated; BadRequest if workflow type not registered for agent.</returns>
    Task<ServiceResult> ValidateActivationAsync(string tenantId, string agentName, string activationName, string? workflowType = null);

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
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(2);
    // Flow definitions are rarely changed at runtime — cache for 5 minutes to reduce DB load
    // on every inbound message and adminapi /send, /listen, /history, /topics call.
    private static readonly TimeSpan WorkflowTypeCacheDuration = TimeSpan.FromMinutes(5);

    private readonly IActivationRepository _activationRepository;
    private readonly IFlowDefinitionRepository _flowDefinitionRepository;
    private readonly IAsyncResultCache _cache;
    private readonly ILogger<ActivationValidationService> _logger;

    public ActivationValidationService(
        IActivationRepository activationRepository,
        IFlowDefinitionRepository flowDefinitionRepository,
        IAsyncResultCache cache,
        ILogger<ActivationValidationService> logger)
    {
        _activationRepository = activationRepository ?? throw new ArgumentNullException(nameof(activationRepository));
        _flowDefinitionRepository = flowDefinitionRepository ?? throw new ArgumentNullException(nameof(flowDefinitionRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            CacheDuration,
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
        _cache.Remove(BuildCacheKey(tenantId, agentName, activationName));
    }

    public void InvalidateAgentWorkflowTypesCache(string tenantId, string agentName)
    {
        _cache.Remove(BuildAgentWorkflowTypesCacheKey(tenantId, agentName));
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
            var fullWorkflowType = NormalizeFullWorkflowType(agentName, workflowType);
            var cacheKey = BuildAgentWorkflowTypesCacheKey(tenantId, agentName);
            var registeredTypes = await _cache.GetOrAddAsync(
                cacheKey,
                _ => LoadRegisteredWorkflowTypesAsync(tenantId, agentName),
                WorkflowTypeCacheDuration,
                size: 1);

            if (registeredTypes.Count == 0)
            {
                _logger.LogWarning(
                    "No workflow definitions found for agent '{AgentName}' in tenant {TenantId}",
                    LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(tenantId));
                return ServiceResult.Failure(
                    $"No agent process registered for agent '{agentName}'. Unable to use this agent for this purpose.",
                    StatusCode.BadRequest);
            }

            var hasWorkflow = registeredTypes.Any(t =>
                string.Equals(t, fullWorkflowType, StringComparison.Ordinal));
            if (!hasWorkflow)
            {
                var displayTypes = registeredTypes
                    .Select(t => t.Contains(':') ? t.Split(':').LastOrDefault()?.Trim() : t)
                    .Where(t => !string.IsNullOrEmpty(t))
                    .Distinct()
                    .OrderBy(t => t)
                    .ToList();
                var registeredList = displayTypes.Count > 0 ? string.Join(", ", displayTypes) : "none";
                _logger.LogWarning(
                    "Workflow type '{WorkflowType}' is not registered for agent '{AgentName}'. Registered types: {Registered}",
                    LogSanitizer.Sanitize(fullWorkflowType), LogSanitizer.Sanitize(agentName), LogSanitizer.Sanitize(registeredList));
                return ServiceResult.Failure(
                    $"Workflow type '{fullWorkflowType}' is not registered for agent '{agentName}'. Registered workflow types: {registeredList}.",
                    StatusCode.BadRequest);
            }

            return ServiceResult.Success();
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

    private async Task<List<string>> LoadRegisteredWorkflowTypesAsync(string tenantId, string agentName)
    {
        var flowDefinitions = await _flowDefinitionRepository.GetByNameAsync(agentName, tenantId);
        if (flowDefinitions == null || flowDefinitions.Count == 0)
        {
            return new List<string>();
        }

        return flowDefinitions
            .Select(fd => fd.WorkflowType)
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

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
