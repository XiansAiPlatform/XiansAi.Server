using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Features.UserApi.Services;
using Features.WebApi.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Data.Models.HealthChecks;
using Shared.Models;
using Shared.Models.Schedule;
using Shared.Repositories;
using Shared.Utils;
using Shared.Utils.Services;

namespace Shared.Services.HealthChecks;

/// <summary>
/// Runs one type of health check, acting as the system user for the check's tenant.
/// Problems are returned as Fail or Error results. If an executor throws, the runner records Error.
/// </summary>
public interface IHealthCheckExecutor
{
    HealthCheckType Type { get; }

    Task<HealthCheckExecutionResult> ExecuteAsync(AgentHealthCheck check, CancellationToken cancellationToken);
}

/// <summary>
/// Sends a chat message to the agent and checks its reply.
/// </summary>
public class ChatHealthCheckExecutor : IHealthCheckExecutor
{
    /// <summary>
    /// The participant used for all health-check chats, so they stay apart from real users' chats.
    /// </summary>
    public const string ParticipantId = "xians-health-check";

    private readonly IActivationValidationService _activationValidationService;
    private readonly IMessageService _messageService;
    private readonly IPendingRequestService _pendingRequestService;
    private readonly ILoggerFactory _loggerFactory;

    public ChatHealthCheckExecutor(
        IActivationValidationService activationValidationService,
        IMessageService messageService,
        IPendingRequestService pendingRequestService,
        ILoggerFactory loggerFactory)
    {
        _activationValidationService = activationValidationService;
        _messageService = messageService;
        _pendingRequestService = pendingRequestService;
        _loggerFactory = loggerFactory;
    }

    public HealthCheckType Type => HealthCheckType.Chat;

    public async Task<HealthCheckExecutionResult> ExecuteAsync(AgentHealthCheck check, CancellationToken cancellationToken)
    {
        var config = check.Chat ?? throw new InvalidOperationException("Chat health check has no chat configuration");

        var resolved = await _activationValidationService.ResolveConversationalWorkflowAsync(
            check.TenantId, check.AgentName, config.WorkflowType);
        if (!resolved.IsSuccess)
        {
            return HealthCheckExecutionResult.Error($"Could not resolve conversational workflow: {resolved.ErrorMessage}");
        }
        var workflowType = resolved.Data!;

        var validation = await _activationValidationService.ValidateActivationAsync(
            check.TenantId, check.AgentName, check.ActivationName, workflowType);
        if (!validation.IsSuccess)
        {
            return HealthCheckExecutionResult.Error($"Activation is not routable: {validation.ErrorMessage}");
        }

        var workflowId = WorkflowIdentifier.BuildWorkflowId(check.TenantId, check.AgentName, workflowType, check.ActivationName);
        var request = MessageRequestProcessor.CreateRequest(
            MessageType.Chat,
            workflowId,
            ParticipantId,
            text: config.Message,
            requestId: MessageRequestProcessor.GenerateRequestId(workflowId, ParticipantId),
            origin: "health-check");

        var syncHandler = new SyncMessageHandler(_messageService, _pendingRequestService, _loggerFactory.CreateLogger<SyncMessageHandler>());
        var result = await syncHandler.ProcessSyncMessageAsync(request, MessageType.Chat, check.TimeoutSeconds, cancellationToken);

        if (result is IResult httpResult)
        {
            return HealthCheckExecutionResult.Error($"No response from agent: {HealthCheckResultDescriber.Describe(httpResult)}");
        }

        var text = result.GetType().GetProperty("Text")?.GetValue(result) as string;
        var (allPassed, assertionResults) = HealthCheckAssertionEvaluator.Evaluate(text, config.Assertions);

        return allPassed
            ? HealthCheckExecutionResult.Pass("All assertions passed", text, assertionResults)
            : HealthCheckExecutionResult.Fail(HealthCheckAssertionEvaluator.DescribeFailures(assertionResults), text, assertionResults);
    }
}

/// <summary>
/// Calls one of the agent's webhooks (an update method or the <c>OnWebhook</c> handler)
/// and checks the status code, the response and how long it took.
/// </summary>
public class WebhookHealthCheckExecutor : IHealthCheckExecutor
{
    private readonly IWebhookReceiverService _webhookReceiverService;
    private readonly IActivationValidationService _activationValidationService;
    private readonly IMessageService _messageService;
    private readonly IPendingRequestService _pendingRequestService;
    private readonly ILoggerFactory _loggerFactory;

    public WebhookHealthCheckExecutor(
        IWebhookReceiverService webhookReceiverService,
        IActivationValidationService activationValidationService,
        IMessageService messageService,
        IPendingRequestService pendingRequestService,
        ILoggerFactory loggerFactory)
    {
        _webhookReceiverService = webhookReceiverService;
        _activationValidationService = activationValidationService;
        _messageService = messageService;
        _pendingRequestService = pendingRequestService;
        _loggerFactory = loggerFactory;
    }

    public HealthCheckType Type => HealthCheckType.Webhook;

    public async Task<HealthCheckExecutionResult> ExecuteAsync(AgentHealthCheck check, CancellationToken cancellationToken)
    {
        var config = check.Webhook ?? throw new InvalidOperationException("Webhook health check has no webhook configuration");

        var stopwatch = Stopwatch.StartNew();
        var (response, error) = config.Mode == WebhookHealthCheckMode.Builtin
            ? await CallBuiltinAsync(check, config, cancellationToken)
            : await CallUpdateAsync(check, config, cancellationToken);
        stopwatch.Stop();

        if (response == null)
        {
            return HealthCheckExecutionResult.Error(error!);
        }

        var actualStatus = (int)response.StatusCode;
        var (assertionsPassed, assertionResults) = HealthCheckAssertionEvaluator.Evaluate(
            new HealthCheckResponse(response.Content, response.Headers), config.Assertions);

        if (config.ExpectedStatusCode.HasValue && actualStatus != config.ExpectedStatusCode.Value)
        {
            return HealthCheckExecutionResult.Fail(
                $"Expected status code {config.ExpectedStatusCode.Value} but got {actualStatus}",
                response.Content, assertionResults);
        }

        if (!assertionsPassed)
        {
            return HealthCheckExecutionResult.Fail(HealthCheckAssertionEvaluator.DescribeFailures(assertionResults), response.Content, assertionResults);
        }

        if (config.MaxResponseMs.HasValue && stopwatch.ElapsedMilliseconds > config.MaxResponseMs.Value)
        {
            return HealthCheckExecutionResult.Fail(
                $"Responded in {stopwatch.ElapsedMilliseconds} ms, over the {config.MaxResponseMs.Value} ms limit",
                response.Content, assertionResults);
        }

        return HealthCheckExecutionResult.Pass(
            $"Responded with status {actualStatus} in {stopwatch.ElapsedMilliseconds} ms", response.Content, assertionResults);
    }

    private async Task<(WebhookResponse? Response, string? Error)> CallUpdateAsync(
        AgentHealthCheck check, WebhookHealthCheckConfig config, CancellationToken cancellationToken)
    {
        var workflowId = WorkflowIdentifier.BuildWorkflowId(check.TenantId, check.AgentName, config.WorkflowType, check.ActivationName);
        var result = await _webhookReceiverService.ProcessWebhook(
            check.TenantId,
            workflowId,
            config.MethodName,
            new Dictionary<string, string>(config.QueryParams),
            config.Body,
            TimeSpan.FromSeconds(check.TimeoutSeconds),
            cancellationToken);

        return result.IsSuccess && result.Data != null
            ? (result.Data, null)
            : (null, $"Webhook invocation failed ({(int)result.StatusCode}): {result.ErrorMessage ?? "no response"}");
    }

    /// <summary>
    /// Works like <c>POST /api/user/webhooks/builtin</c>: sends the webhook name and body to the agent
    /// and waits for its <c>OnWebhook</c> reply.
    /// </summary>
    private async Task<(WebhookResponse? Response, string? Error)> CallBuiltinAsync(
        AgentHealthCheck check, WebhookHealthCheckConfig config, CancellationToken cancellationToken)
    {
        var validation = await _activationValidationService.ValidateActivationAsync(
            check.TenantId, check.AgentName, check.ActivationName, config.WorkflowType);
        if (!validation.IsSuccess)
        {
            return (null, $"Activation is not routable: {validation.ErrorMessage}");
        }

        var workflowId = WorkflowIdentifier.BuildWorkflowId(check.TenantId, check.AgentName, config.WorkflowType, check.ActivationName);
        var request = new ChatOrDataRequest
        {
            RequestId = MessageRequestProcessor.GenerateRequestId(workflowId, ChatHealthCheckExecutor.ParticipantId),
            ParticipantId = ChatHealthCheckExecutor.ParticipantId,
            WorkflowId = workflowId,
            Text = config.WebhookName,
            Data = config.Body,
            Origin = $"health-check:webhook:{config.WebhookName}"
        };

        var logger = _loggerFactory.CreateLogger<SyncMessageHandler>();
        var result = await new SyncMessageHandler(_messageService, _pendingRequestService, logger)
            .ProcessSyncMessageAsync(request, MessageType.Webhook, check.TimeoutSeconds, cancellationToken);

        if (result is IResult httpResult)
        {
            return (null, $"No response from agent: {HealthCheckResultDescriber.Describe(httpResult)}");
        }

        // Same as the built-in endpoint: any other reply is treated as a 200 JSON response.
        return (WebhookResponseParser.FromSyncResult(result, logger)
                ?? new WebhookResponse(HttpStatusCode.OK, JsonSerializer.Serialize(result)), null);
    }
}

/// <summary>
/// Checks that an agent schedule exists, belongs to the agent, is not paused and, if asked,
/// that its last run did not fail. It never runs the schedule.
/// </summary>
public class ScheduleHealthCheckExecutor : IHealthCheckExecutor
{
    private readonly IScheduleService _scheduleService;

    public ScheduleHealthCheckExecutor(IScheduleService scheduleService)
    {
        _scheduleService = scheduleService;
    }

    public HealthCheckType Type => HealthCheckType.Schedule;

    public async Task<HealthCheckExecutionResult> ExecuteAsync(AgentHealthCheck check, CancellationToken cancellationToken)
    {
        var config = check.Schedule ?? throw new InvalidOperationException("Schedule health check has no schedule configuration");

        var scheduleResult = await _scheduleService.GetScheduleByIdAsync(config.ScheduleId);
        if (scheduleResult.StatusCode == StatusCode.NotFound
            || (scheduleResult.IsSuccess && !BelongsToAgent(scheduleResult.Data, check.AgentName)))
        {
            return HealthCheckExecutionResult.Fail($"Schedule '{config.ScheduleId}' does not exist");
        }

        if (!scheduleResult.IsSuccess || scheduleResult.Data == null)
        {
            return HealthCheckExecutionResult.Error($"Could not read schedule: {scheduleResult.ErrorMessage}");
        }

        var schedule = scheduleResult.Data;
        if (schedule.Status == ScheduleStatus.Suspended)
        {
            return HealthCheckExecutionResult.Fail($"Schedule '{config.ScheduleId}' is paused");
        }

        if (!config.RequireLastRunSuccess)
        {
            return HealthCheckExecutionResult.Pass($"Schedule exists and is enabled (next run {schedule.NextRunTime:u})");
        }

        var history = await _scheduleService.GetScheduleHistoryAsync(config.ScheduleId, 1);
        if (!history.IsSuccess)
        {
            return HealthCheckExecutionResult.Error($"Could not read schedule history: {history.ErrorMessage}");
        }

        var lastRun = history.Data?.FirstOrDefault();
        if (lastRun == null)
        {
            return HealthCheckExecutionResult.Pass("Schedule exists and is enabled; no runs yet");
        }

        return lastRun.Status == ScheduleRunStatus.Failed
            ? HealthCheckExecutionResult.Fail(
                $"Last run at {lastRun.ActualRunTime ?? lastRun.ScheduledTime:u} failed: {lastRun.ErrorMessage ?? "no error detail"}")
            : HealthCheckExecutionResult.Pass(
                $"Schedule exists and is enabled; last run at {lastRun.ActualRunTime ?? lastRun.ScheduledTime:u} was {lastRun.Status}");
    }

    private static bool BelongsToAgent(ScheduleModel? schedule, string agentName)
        => string.Equals(schedule?.AgentName, agentName, StringComparison.OrdinalIgnoreCase);
}

internal static class HealthCheckResultDescriber
{
    private const int MaxLength = 300;

    /// <summary>Turns an error result into one line of text.</summary>
    public static string Describe(IResult result)
    {
        var statusCode = (result as IStatusCodeHttpResult)?.StatusCode;
        var value = (result as IValueHttpResult)?.Value;

        var detail = value switch
        {
            null => null,
            ProblemDetails problem => problem.Detail ?? problem.Title,
            string text => text,
            _ => JsonSerializer.Serialize(value)
        };

        var description = statusCode.HasValue ? $"HTTP {statusCode}" : "error";
        if (!string.IsNullOrWhiteSpace(detail))
        {
            description += $" {detail}";
        }

        return description.Length <= MaxLength ? description : description[..MaxLength];
    }
}
