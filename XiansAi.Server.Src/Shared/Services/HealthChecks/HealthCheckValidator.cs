using System.Text.RegularExpressions;
using Shared.Configuration;
using Shared.Data.Models.HealthChecks;
using Shared.Data.Models.Validation;

namespace Shared.Services.HealthChecks;

/// <summary>The validated, normalized definition fields of a health check.</summary>
public sealed record HealthCheckDefinition(
    string Name,
    HealthCheckType Type,
    bool Enabled,
    int IntervalMinutes,
    int TimeoutSeconds,
    ChatHealthCheckConfig? Chat,
    WebhookHealthCheckConfig? Webhook,
    ScheduleHealthCheckConfig? Schedule);

/// <summary>
/// Synchronous validation of a <see cref="HealthCheckRequest"/>: shape, limits, interval floor (R7),
/// assertion rules (R5). Checks that need I/O (workflow and schedule existence) run in the service.
/// </summary>
public static class HealthCheckValidator
{
    private static readonly Regex NamePattern = new(ValidationHelpers.UnicodeSafeNamePattern, RegexOptions.Compiled);

    public static (HealthCheckDefinition? Definition, string? Error) Validate(
        HealthCheckRequest request, HealthChecksOptions options, HealthCheckType? existingType = null)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > HealthCheckLimits.MaxNameLength)
            return Invalid($"name is required and must be at most {HealthCheckLimits.MaxNameLength} characters");
        if (!NamePattern.IsMatch(name))
            return Invalid("name contains invalid characters");

        if (request.Type == null)
            return Invalid("type is required (chat, webhook or schedule)");
        var type = request.Type.Value;
        if (existingType.HasValue && existingType.Value != type)
            return Invalid("type cannot be changed; delete the check and create a new one");

        var minInterval = options.EffectiveMinIntervalMinutes;
        if (request.IntervalMinutes == null
            || request.IntervalMinutes < minInterval
            || request.IntervalMinutes > HealthChecksOptions.MaxIntervalMinutes)
            return Invalid($"intervalMinutes is required and must be between {minInterval} and {HealthChecksOptions.MaxIntervalMinutes}");

        var payloadCount = (request.Chat != null ? 1 : 0) + (request.Webhook != null ? 1 : 0) + (request.Schedule != null ? 1 : 0);
        if (payloadCount != 1)
            return Invalid("exactly one of chat, webhook or schedule must be provided, matching type");

        var timeout = request.TimeoutSeconds ?? HealthCheckLimits.DefaultTimeoutSeconds;
        if (type != HealthCheckType.Schedule
            && (timeout < HealthCheckLimits.MinTimeoutSeconds || timeout > HealthCheckLimits.MaxTimeoutSeconds))
            return Invalid($"timeoutSeconds must be between {HealthCheckLimits.MinTimeoutSeconds} and {HealthCheckLimits.MaxTimeoutSeconds}");

        string? error = null;
        ChatHealthCheckConfig? chat = null;
        WebhookHealthCheckConfig? webhook = null;
        ScheduleHealthCheckConfig? schedule = null;

        switch (type)
        {
            case HealthCheckType.Chat:
                if (request.Chat == null) return Invalid("chat is required for a chat check");
                (chat, error) = ValidateChat(request.Chat);
                break;
            case HealthCheckType.Webhook:
                if (request.Webhook == null) return Invalid("webhook is required for a webhook check");
                (webhook, error) = ValidateWebhook(request.Webhook);
                break;
            case HealthCheckType.Schedule:
                if (request.Schedule == null) return Invalid("schedule is required for a schedule check");
                (schedule, error) = ValidateSchedule(request.Schedule);
                // The timeout is unused by metadata checks; store the default for a consistent document.
                timeout = HealthCheckLimits.DefaultTimeoutSeconds;
                break;
        }

        if (error != null)
            return Invalid(error);

        return (new HealthCheckDefinition(name, type, request.Enabled, request.IntervalMinutes.Value, timeout, chat, webhook, schedule), null);
    }

    private static (ChatHealthCheckConfig?, string?) ValidateChat(ChatHealthCheckRequest chat)
    {
        if (string.IsNullOrWhiteSpace(chat.Message) || chat.Message.Length > HealthCheckLimits.MaxMessageLength)
            return (null, $"chat.message is required and must be at most {HealthCheckLimits.MaxMessageLength} characters");

        var workflowType = string.IsNullOrWhiteSpace(chat.WorkflowType) ? null : chat.WorkflowType.Trim();
        if (workflowType != null && ValidateFlowName(workflowType, "chat.workflowType") is { } workflowError)
            return (null, workflowError);

        var (assertions, assertionError) = ValidateAssertions(chat.Assertions, "chat.assertions");
        if (assertionError != null)
            return (null, assertionError);

        return (new ChatHealthCheckConfig { WorkflowType = workflowType, Message = chat.Message, Assertions = assertions! }, null);
    }

    private static (WebhookHealthCheckConfig?, string?) ValidateWebhook(WebhookHealthCheckRequest webhook)
    {
        var workflowType = webhook.WorkflowType?.Trim();
        if (string.IsNullOrEmpty(workflowType))
            return (null, "webhook.workflowType is required");
        if (ValidateFlowName(workflowType, "webhook.workflowType") is { } workflowError)
            return (null, workflowError);

        var methodName = webhook.MethodName?.Trim();
        if (string.IsNullOrEmpty(methodName) || methodName.Length > HealthCheckLimits.MaxMethodNameLength)
            return (null, $"webhook.methodName is required and must be at most {HealthCheckLimits.MaxMethodNameLength} characters");

        var queryParams = webhook.QueryParams ?? new Dictionary<string, string>();
        if (queryParams.Count > HealthCheckLimits.MaxQueryParams)
            return (null, $"webhook.queryParams may have at most {HealthCheckLimits.MaxQueryParams} entries");
        foreach (var (key, value) in queryParams)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > HealthCheckLimits.MaxQueryParamKeyLength)
                return (null, $"webhook.queryParams keys must be 1 to {HealthCheckLimits.MaxQueryParamKeyLength} characters");
            if ((value?.Length ?? 0) > HealthCheckLimits.MaxQueryParamValueLength)
                return (null, $"webhook.queryParams values must be at most {HealthCheckLimits.MaxQueryParamValueLength} characters");
            // The inbound endpoint strips credentials from the query before it reaches the workflow;
            // refuse to store them here at all.
            if (key.Equals("apikey", StringComparison.OrdinalIgnoreCase) || key.Equals("access_token", StringComparison.OrdinalIgnoreCase))
                return (null, $"webhook.queryParams must not contain credential parameter '{key}'");
        }

        var body = webhook.Body ?? string.Empty;
        if (body.Length > HealthCheckLimits.MaxBodyLength)
            return (null, $"webhook.body must be at most {HealthCheckLimits.MaxBodyLength} characters");

        if (webhook.ExpectedStatusCode is < 100 or > 599)
            return (null, "webhook.expectedStatusCode must be a valid HTTP status code (100-599)");

        var (assertions, assertionError) = ValidateAssertions(webhook.Assertions, "webhook.assertions");
        if (assertionError != null)
            return (null, assertionError);

        return (new WebhookHealthCheckConfig
        {
            WorkflowType = workflowType,
            MethodName = methodName,
            QueryParams = queryParams.ToDictionary(p => p.Key, p => p.Value ?? string.Empty),
            Body = body,
            ExpectedStatusCode = webhook.ExpectedStatusCode,
            Assertions = assertions!
        }, null);
    }

    private static (ScheduleHealthCheckConfig?, string?) ValidateSchedule(ScheduleHealthCheckRequest schedule)
    {
        var scheduleId = schedule.ScheduleId?.Trim();
        if (string.IsNullOrEmpty(scheduleId) || scheduleId.Length > HealthCheckLimits.MaxScheduleIdLength)
            return (null, $"schedule.scheduleId is required and must be at most {HealthCheckLimits.MaxScheduleIdLength} characters");

        return (new ScheduleHealthCheckConfig { ScheduleId = scheduleId, RequireLastRunSuccess = schedule.RequireLastRunSuccess }, null);
    }

    private static (List<HealthCheckAssertion>?, string?) ValidateAssertions(List<HealthCheckAssertionRequest>? assertions, string field)
    {
        assertions ??= new();
        if (assertions.Count > HealthCheckLimits.MaxAssertions)
            return (null, $"{field} may have at most {HealthCheckLimits.MaxAssertions} entries");

        var result = new List<HealthCheckAssertion>();
        foreach (var assertion in assertions)
        {
            if (assertion?.Op == null)
                return (null, $"{field}[].op is required (contains or notContains)");
            if (string.IsNullOrEmpty(assertion.Value) || assertion.Value.Length > HealthCheckLimits.MaxAssertionValueLength)
                return (null, $"{field}[].value is required and must be at most {HealthCheckLimits.MaxAssertionValueLength} characters");

            result.Add(new HealthCheckAssertion { Op = assertion.Op.Value, Value = assertion.Value });
        }

        return (result, null);
    }

    /// <summary>Workflow types are stored as the flow name alone; the agent prefix comes from the activation.</summary>
    private static string? ValidateFlowName(string workflowType, string field)
    {
        if (workflowType.Length > HealthCheckLimits.MaxWorkflowTypeLength)
            return $"{field} must be at most {HealthCheckLimits.MaxWorkflowTypeLength} characters";
        if (workflowType.Contains(':'))
            return $"{field} must be the flow name without the agent prefix (e.g. 'Router Bot', not 'My Agent:Router Bot')";
        return null;
    }

    private static (HealthCheckDefinition?, string?) Invalid(string error) => (null, error);
}
