using Features.WebApi.Services;
using Shared.Auditing;
using Shared.Configuration;
using Shared.Data.Models;
using Shared.Data.Models.HealthChecks;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services.HealthChecks;

public interface IAgentHealthCheckService
{
    Task<ServiceResult<List<AgentHealthCheck>>> ListAsync(string tenantId, string activationId);
    Task<ServiceResult<AgentHealthCheck>> GetAsync(string tenantId, string activationId, string checkId);
    Task<ServiceResult<AgentHealthCheck>> CreateAsync(string tenantId, string activationId, HealthCheckRequest request, string userId);
    Task<ServiceResult<AgentHealthCheck>> UpdateAsync(string tenantId, string activationId, string checkId, HealthCheckRequest request, string userId);
    Task<ServiceResult<bool>> DeleteAsync(string tenantId, string activationId, string checkId);
    Task<ServiceResult<HealthCheckRunPage>> GetRunsAsync(string tenantId, string activationId, string checkId,
        DateTime? from, DateTime? to, HealthCheckStatus? status, int page, int pageSize);
    Task<ServiceResult<ActivationHealthSummary>> GetSummaryAsync(string tenantId, string activationId);
    Task<ServiceResult<List<AgentHealthCheck>>> ListByTenantAsync(string tenantId, IReadOnlyCollection<HealthCheckStatus>? statuses, string? agentName);

    /// <summary>The agent's workflows and webhooks, for the webhook eval form.</summary>
    Task<ServiceResult<List<WebhookTargetWorkflow>>> GetWebhookTargetsAsync(string tenantId, string activationId);
}

/// <summary>
/// Called by <see cref="ActivationService"/> when an activation changes.
/// Errors are only logged; the reconciler fixes them later.
/// </summary>
public interface IHealthCheckActivationHooks
{
    /// <summary>Pauses or resumes the activation's checks when it is deactivated or activated.</summary>
    Task OnActivationStateChangedAsync(AgentActivation activation);

    /// <summary>Deletes the activation's checks, their runs and their schedules.</summary>
    Task OnActivationDeletedAsync(AgentActivation activation);
}

public class AgentHealthCheckService : IAgentHealthCheckService, IHealthCheckActivationHooks
{
    private readonly IAgentHealthCheckRepository _repository;
    private readonly IActivationRepository _activationRepository;
    private readonly IHealthCheckScheduler _scheduler;
    private readonly IActivationValidationService _activationValidationService;
    private readonly IScheduleService _scheduleService;
    private readonly IWebhookTargetDiscovery _webhookTargetDiscovery;
    private readonly IContactRepository _contactRepository;
    private readonly IWebhookEventPublisher _webhookEventPublisher;
    private readonly IAuditLogService _auditLogService;
    private readonly HealthChecksOptions _options;
    private readonly ILogger<AgentHealthCheckService> _logger;

    public AgentHealthCheckService(
        IAgentHealthCheckRepository repository,
        IActivationRepository activationRepository,
        IHealthCheckScheduler scheduler,
        IActivationValidationService activationValidationService,
        IScheduleService scheduleService,
        IWebhookTargetDiscovery webhookTargetDiscovery,
        IContactRepository contactRepository,
        IWebhookEventPublisher webhookEventPublisher,
        IAuditLogService auditLogService,
        HealthChecksOptions options,
        ILogger<AgentHealthCheckService> logger)
    {
        _webhookTargetDiscovery = webhookTargetDiscovery;
        _contactRepository = contactRepository;
        _repository = repository;
        _activationRepository = activationRepository;
        _scheduler = scheduler;
        _activationValidationService = activationValidationService;
        _scheduleService = scheduleService;
        _webhookEventPublisher = webhookEventPublisher;
        _auditLogService = auditLogService;
        _options = options;
        _logger = logger;
    }

    public async Task<ServiceResult<List<AgentHealthCheck>>> ListAsync(string tenantId, string activationId)
    {
        var activation = await GetActivationAsync(tenantId, activationId);
        if (activation == null)
            return ServiceResult<List<AgentHealthCheck>>.NotFound("Activation not found in the specified tenant");

        return ServiceResult<List<AgentHealthCheck>>.Success(await _repository.ListByActivationAsync(tenantId, activationId));
    }

    public async Task<ServiceResult<AgentHealthCheck>> GetAsync(string tenantId, string activationId, string checkId)
    {
        var check = await GetCheckAsync(tenantId, activationId, checkId);
        return check == null
            ? ServiceResult<AgentHealthCheck>.NotFound("Health check not found")
            : ServiceResult<AgentHealthCheck>.Success(check);
    }

    public async Task<ServiceResult<AgentHealthCheck>> CreateAsync(
        string tenantId, string activationId, HealthCheckRequest request, string userId)
    {
        var activation = await GetActivationAsync(tenantId, activationId);
        if (activation == null)
            return ServiceResult<AgentHealthCheck>.NotFound("Activation not found in the specified tenant");

        var (definition, error) = HealthCheckValidator.Validate(request, _options);
        if (definition == null)
            return ServiceResult<AgentHealthCheck>.BadRequest(error!);

        if (await _repository.CountByActivationAsync(tenantId, activationId) >= _options.MaxChecksPerActivation)
            return ServiceResult<AgentHealthCheck>.BadRequest(
                $"An activation can have at most {_options.MaxChecksPerActivation} health checks");

        var targetError = await ValidateTargetAsync(definition, activation);
        if (targetError != null)
            return ServiceResult<AgentHealthCheck>.BadRequest(targetError);

        var check = new AgentHealthCheck
        {
            TenantId = tenantId,
            ActivationId = activation.Id,
            AgentName = activation.AgentName,
            ActivationName = activation.Name,
            Name = definition.Name,
            CreatedBy = userId,
            CreatedAt = DateTime.UtcNow,
            ScheduleState = HealthCheckScheduleState.Pending
        };
        ApplyDefinition(check, definition);

        if (!await _repository.InsertAsync(check))
            return ServiceResult<AgentHealthCheck>.Conflict($"A health check named '{definition.Name}' already exists on this activation");

        check.ScheduleState = await SyncScheduleAsync(check, activation.IsActive);

        Emit(DomainEventTypes.HealthCheckCreated, check,
            $"Health check '{check.Name}' ({check.Type}) was attached to activation '{activation.Name}' of agent '{activation.AgentName}'.");

        return ServiceResult<AgentHealthCheck>.Success(check, StatusCode.Created);
    }

    public async Task<ServiceResult<AgentHealthCheck>> UpdateAsync(
        string tenantId, string activationId, string checkId, HealthCheckRequest request, string userId)
    {
        var activation = await GetActivationAsync(tenantId, activationId);
        if (activation == null)
            return ServiceResult<AgentHealthCheck>.NotFound("Activation not found in the specified tenant");

        var check = await GetCheckAsync(tenantId, activationId, checkId);
        if (check == null)
            return ServiceResult<AgentHealthCheck>.NotFound("Health check not found");

        var (definition, error) = HealthCheckValidator.Validate(request, _options, check.Type);
        if (definition == null)
            return ServiceResult<AgentHealthCheck>.BadRequest(error!);

        var targetError = await ValidateTargetAsync(definition, activation);
        if (targetError != null)
            return ServiceResult<AgentHealthCheck>.BadRequest(targetError);

        check.Name = definition.Name;
        ApplyDefinition(check, definition);
        check.UpdatedBy = userId;
        check.UpdatedAt = DateTime.UtcNow;
        check.ScheduleState = HealthCheckScheduleState.Pending;

        var replaced = await _repository.ReplaceAsync(check);
        if (replaced == null)
            return ServiceResult<AgentHealthCheck>.NotFound("Health check not found");
        if (replaced == false)
            return ServiceResult<AgentHealthCheck>.Conflict($"A health check named '{definition.Name}' already exists on this activation");

        check.ScheduleState = await SyncScheduleAsync(check, activation.IsActive);

        Emit(DomainEventTypes.HealthCheckUpdated, check,
            $"Health check '{check.Name}' on activation '{activation.Name}' was updated (enabled: {check.Enabled}).");

        return ServiceResult<AgentHealthCheck>.Success(check);
    }

    public async Task<ServiceResult<bool>> DeleteAsync(string tenantId, string activationId, string checkId)
    {
        var check = await GetCheckAsync(tenantId, activationId, checkId);
        if (check == null)
            return ServiceResult<bool>.NotFound("Health check not found");

        await DeleteCheckAsync(check);

        Emit(DomainEventTypes.HealthCheckDeleted, check,
            $"Health check '{check.Name}' on activation '{check.ActivationName}' was deleted with its run history.");

        return ServiceResult<bool>.Success(true);
    }

    public async Task<ServiceResult<HealthCheckRunPage>> GetRunsAsync(
        string tenantId, string activationId, string checkId,
        DateTime? from, DateTime? to, HealthCheckStatus? status, int page, int pageSize)
    {
        if (page < 1)
            return ServiceResult<HealthCheckRunPage>.BadRequest("page must be 1 or greater");
        if (pageSize < 1 || pageSize > HealthCheckLimits.MaxRunsPageSize)
            return ServiceResult<HealthCheckRunPage>.BadRequest($"pageSize must be between 1 and {HealthCheckLimits.MaxRunsPageSize}");
        if (from.HasValue && to.HasValue && from > to)
            return ServiceResult<HealthCheckRunPage>.BadRequest("from must not be after to");

        var check = await GetCheckAsync(tenantId, activationId, checkId);
        if (check == null)
            return ServiceResult<HealthCheckRunPage>.NotFound("Health check not found");

        return ServiceResult<HealthCheckRunPage>.Success(
            await _repository.ListRunsAsync(tenantId, checkId, from, to, status, page, pageSize));
    }

    public async Task<ServiceResult<ActivationHealthSummary>> GetSummaryAsync(string tenantId, string activationId)
    {
        var activation = await GetActivationAsync(tenantId, activationId);
        if (activation == null)
            return ServiceResult<ActivationHealthSummary>.NotFound("Activation not found in the specified tenant");

        var checks = await _repository.ListByActivationAsync(tenantId, activationId);
        return ServiceResult<ActivationHealthSummary>.Success(BuildSummary(activation, checks));
    }

    public async Task<ServiceResult<List<AgentHealthCheck>>> ListByTenantAsync(
        string tenantId, IReadOnlyCollection<HealthCheckStatus>? statuses, string? agentName)
    {
        return ServiceResult<List<AgentHealthCheck>>.Success(
            await _repository.ListByTenantAsync(tenantId, statuses, agentName));
    }

    public async Task<ServiceResult<List<WebhookTargetWorkflow>>> GetWebhookTargetsAsync(string tenantId, string activationId)
    {
        var activation = await GetActivationAsync(tenantId, activationId);
        if (activation == null)
            return ServiceResult<List<WebhookTargetWorkflow>>.NotFound("Activation not found in the specified tenant");

        return ServiceResult<List<WebhookTargetWorkflow>>.Success(await _webhookTargetDiscovery.DiscoverAsync(activation));
    }

    public async Task OnActivationStateChangedAsync(AgentActivation activation)
    {
        try
        {
            var checks = await _repository.ListByActivationAsync(activation.TenantId, activation.Id);
            foreach (var check in checks)
            {
                await SyncScheduleAsync(check, activation.IsActive);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not update health-check schedules for activation {ActivationId}; the reconciler will retry",
                LogSanitizer.Sanitize(activation.Id));
        }
    }

    public async Task OnActivationDeletedAsync(AgentActivation activation)
    {
        try
        {
            var checks = await _repository.ListByActivationAsync(activation.TenantId, activation.Id);
            foreach (var check in checks)
            {
                await DeleteCheckAsync(check);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not delete health checks for activation {ActivationId}; the reconciler will remove them",
                LogSanitizer.Sanitize(activation.Id));
        }
    }

    /// <summary>
    /// Works out the activation's health from the latest result of each check.
    /// Disabled checks and checks that haven't run yet are ignored.
    /// </summary>
    public static ActivationHealthSummary BuildSummary(AgentActivation activation, IReadOnlyList<AgentHealthCheck> checks)
    {
        var enabled = checks.Where(c => c.Enabled).ToList();
        var passing = enabled.Count(c => c.LastStatus == HealthCheckStatus.Pass);
        var failing = enabled.Count(c => c.LastStatus == HealthCheckStatus.Fail);
        var erroring = enabled.Count(c => c.LastStatus == HealthCheckStatus.Error);
        var reported = passing + failing + erroring;

        var status = reported == 0 ? ActivationHealthStatus.Unknown
            : passing == reported ? ActivationHealthStatus.Healthy
            : passing == 0 ? ActivationHealthStatus.Unhealthy
            : ActivationHealthStatus.Degraded;

        return new ActivationHealthSummary(
            activation.Id,
            activation.AgentName,
            activation.Name,
            activation.IsActive,
            status,
            checks.Count,
            enabled.Count,
            passing,
            failing,
            erroring,
            checks.Max(c => c.LastRunAt),
            checks.Select(c => new HealthCheckStatusSummary(
                c.Id, c.Name, c.Type, c.Enabled, c.LastStatus, c.LastRunAt, c.LastDetail, c.ConsecutiveFailures)).ToList());
    }

    private static void ApplyDefinition(AgentHealthCheck check, HealthCheckDefinition definition)
    {
        check.Type = definition.Type;
        check.Enabled = definition.Enabled;
        check.IntervalMinutes = definition.IntervalMinutes;
        check.TimeoutSeconds = definition.TimeoutSeconds;
        check.Chat = definition.Chat;
        check.Webhook = definition.Webhook;
        check.Schedule = definition.Schedule;
        check.NotifyContactIds = definition.NotifyContactIds;
    }

    /// <summary>
    /// Checks that need the database, such as whether the workflow, schedule and contacts exist.
    /// This catches typos before the check is saved.
    /// </summary>
    private async Task<string?> ValidateTargetAsync(HealthCheckDefinition definition, AgentActivation activation)
    {
        if (definition.Chat?.WorkflowType != null)
        {
            var resolved = await _activationValidationService.ResolveConversationalWorkflowAsync(
                activation.TenantId, activation.AgentName, definition.Chat.WorkflowType);
            if (!resolved.IsSuccess)
                return $"chat.workflowType is not a conversational workflow of agent '{activation.AgentName}': {resolved.ErrorMessage}";
        }

        if (definition.Schedule != null)
        {
            var schedule = await _scheduleService.GetScheduleByIdAsync(definition.Schedule.ScheduleId);
            if (!schedule.IsSuccess
                || !string.Equals(schedule.Data?.AgentName, activation.AgentName, StringComparison.OrdinalIgnoreCase))
                return $"schedule.scheduleId '{definition.Schedule.ScheduleId}' was not found for agent '{activation.AgentName}'";
        }

        if (definition.NotifyContactIds.Count > 0)
        {
            var tenantContactIds = (await _contactRepository.ListAsync(activation.TenantId)).Select(c => c.Id).ToHashSet();
            var unknown = definition.NotifyContactIds.FirstOrDefault(id => !tenantContactIds.Contains(id));
            if (unknown != null)
                return $"notifyContactIds: contact '{unknown}' was not found in this tenant";
        }

        return null;
    }

    /// <summary>Creates or updates the schedule. If that fails, the state is Pending and the reconciler retries.</summary>
    private async Task<HealthCheckScheduleState> SyncScheduleAsync(AgentHealthCheck check, bool activationActive)
    {
        HealthCheckScheduleState state;
        try
        {
            state = await _scheduler.UpsertAsync(check, activationActive);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not sync Temporal schedule for health check {CheckId}; the reconciler will retry",
                LogSanitizer.Sanitize(check.Id));
            state = HealthCheckScheduleState.Pending;
        }

        await _repository.SetScheduleStateAsync(check.Id, state);
        return state;
    }

    private async Task DeleteCheckAsync(AgentHealthCheck check)
    {
        try
        {
            await _scheduler.DeleteAsync(check.TenantId, check.Id);
        }
        catch (Exception ex)
        {
            // The reconciler removes leftover schedules later, so the check can still be deleted.
            _logger.LogWarning(ex, "Could not delete Temporal schedule for health check {CheckId}; the reconciler will remove it",
                LogSanitizer.Sanitize(check.Id));
        }

        await _repository.DeleteRunsByCheckAsync(check.TenantId, check.Id);
        await _repository.DeleteAsync(check.TenantId, check.Id);
    }

    private async Task<AgentActivation?> GetActivationAsync(string tenantId, string activationId)
    {
        if (!MongoDB.Bson.ObjectId.TryParse(activationId, out _))
            return null;

        var activation = await _activationRepository.GetByIdAsync(activationId);
        return activation?.TenantId == tenantId ? activation : null;
    }

    private async Task<AgentHealthCheck?> GetCheckAsync(string tenantId, string activationId, string checkId)
    {
        var check = await _repository.GetByIdAsync(tenantId, checkId);
        return check?.ActivationId == activationId ? check : null;
    }

    private void Emit(string eventType, AgentHealthCheck check, string description)
    {
        var data = new
        {
            tenantId = check.TenantId,
            checkId = check.Id,
            name = check.Name,
            type = check.Type.ToString().ToLowerInvariant(),
            enabled = check.Enabled,
            intervalMinutes = check.IntervalMinutes,
            activationId = check.ActivationId,
            activationName = check.ActivationName,
            agentName = check.AgentName
        };

        DomainEventEmitter.Emit(_webhookEventPublisher, _auditLogService, eventType, data,
            check.TenantId, check.ActivationName, description);
    }
}
