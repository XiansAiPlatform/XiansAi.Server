using System.Text.RegularExpressions;
using MongoDB.Bson;
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
    ScheduleHealthCheckConfig? Schedule,
    List<string> NotifyContactIds);

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
                (webhook, error) = ValidateWebhook(request.Webhook, timeout);
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

        var (notifyContactIds, contactsError) = ValidateNotifyContactIds(request.NotifyContactIds);
        if (contactsError != null)
            return Invalid(contactsError);

        return (new HealthCheckDefinition(
            name, type, request.Enabled, request.IntervalMinutes.Value, timeout, chat, webhook, schedule, notifyContactIds!), null);
    }

    /// <summary>
    /// Shape only (ObjectId format, count, duplicates dropped); the service checks the contacts exist in the tenant.
    /// </summary>
    private static (List<string>?, string?) ValidateNotifyContactIds(List<string>? contactIds)
    {
        var ids = (contactIds ?? new())
            .Select(id => id?.Trim() ?? string.Empty)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (ids.Count > HealthCheckLimits.MaxNotifyContacts)
            return (null, $"notifyContactIds can have at most {HealthCheckLimits.MaxNotifyContacts} contacts");
        if (ids.Any(id => !ObjectId.TryParse(id, out _)))
            return (null, "notifyContactIds must contain contact ids");

        return (ids, null);
    }

    private static (ChatHealthCheckConfig?, string?) ValidateChat(ChatHealthCheckRequest chat)
    {
        if (string.IsNullOrWhiteSpace(chat.Message) || chat.Message.Length > HealthCheckLimits.MaxMessageLength)
            return (null, $"chat.message is required and must be at most {HealthCheckLimits.MaxMessageLength} characters");

        var workflowType = string.IsNullOrWhiteSpace(chat.WorkflowType) ? null : chat.WorkflowType.Trim();
        if (workflowType != null && ValidateFlowName(workflowType, "chat.workflowType") is { } workflowError)
            return (null, workflowError);

        var (assertions, assertionError) = ValidateAssertions(chat.Assertions, "chat.assertions", allowStructured: false);
        if (assertionError != null)
            return (null, assertionError);

        return (new ChatHealthCheckConfig { WorkflowType = workflowType, Message = chat.Message, Assertions = assertions! }, null);
    }

    private static (WebhookHealthCheckConfig?, string?) ValidateWebhook(WebhookHealthCheckRequest webhook, int timeoutSeconds)
    {
        var mode = webhook.Mode ?? WebhookHealthCheckMode.Update;

        var workflowType = webhook.WorkflowType?.Trim();
        if (string.IsNullOrEmpty(workflowType))
            return (null, "webhook.workflowType is required");
        if (ValidateFlowName(workflowType, "webhook.workflowType") is { } workflowError)
            return (null, workflowError);

        string methodName = string.Empty;
        string? webhookName = null;
        if (mode == WebhookHealthCheckMode.Update)
        {
            methodName = webhook.MethodName?.Trim() ?? string.Empty;
            if (methodName.Length == 0 || methodName.Length > HealthCheckLimits.MaxMethodNameLength)
                return (null, $"webhook.methodName is required and must be at most {HealthCheckLimits.MaxMethodNameLength} characters");
        }
        else
        {
            webhookName = webhook.WebhookName?.Trim();
            if (string.IsNullOrEmpty(webhookName) || webhookName.Length > HealthCheckLimits.MaxWebhookNameLength)
                return (null, $"webhook.webhookName is required for builtin webhooks and must be at most {HealthCheckLimits.MaxWebhookNameLength} characters");
            // The builtin endpoint passes the body (and webhook name) to OnWebhook, not query parameters.
            if (webhook.QueryParams is { Count: > 0 })
                return (null, "webhook.queryParams are not sent to builtin webhooks; put the data in webhook.body");
        }

        if (webhook.MaxResponseMs is { } maxResponseMs && (maxResponseMs < 1 || maxResponseMs > timeoutSeconds * 1000))
            return (null, $"webhook.maxResponseMs must be between 1 and the timeout ({timeoutSeconds * 1000} ms)");

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

        var (assertions, assertionError) = ValidateAssertions(webhook.Assertions, "webhook.assertions", allowStructured: true);
        if (assertionError != null)
            return (null, assertionError);

        return (new WebhookHealthCheckConfig
        {
            Mode = mode,
            WorkflowType = workflowType,
            MethodName = methodName,
            WebhookName = webhookName,
            QueryParams = queryParams.ToDictionary(p => p.Key, p => p.Value ?? string.Empty),
            Body = body,
            ExpectedStatusCode = webhook.ExpectedStatusCode,
            MaxResponseMs = webhook.MaxResponseMs,
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

    /// <summary>
    /// Text ops (contains / notContains) everywhere; JSON and header ops only where
    /// <paramref name="allowStructured"/> (webhook responses carry JSON and headers, chat replies don't).
    /// </summary>
    private static (List<HealthCheckAssertion>?, string?) ValidateAssertions(
        List<HealthCheckAssertionRequest>? assertions, string field, bool allowStructured)
    {
        assertions ??= new();
        if (assertions.Count > HealthCheckLimits.MaxAssertions)
            return (null, $"{field} may have at most {HealthCheckLimits.MaxAssertions} entries");

        var result = new List<HealthCheckAssertion>();
        foreach (var assertion in assertions)
        {
            if (assertion?.Op == null)
                return (null, allowStructured
                    ? $"{field}[].op is required (contains, notContains, jsonEquals, jsonExists, headerEquals or headerExists)"
                    : $"{field}[].op is required (contains or notContains)");

            var op = assertion.Op.Value;
            var isText = op is HealthCheckAssertionOp.Contains or HealthCheckAssertionOp.NotContains;
            var isJson = op is HealthCheckAssertionOp.JsonEquals or HealthCheckAssertionOp.JsonExists;
            var needsValue = op is HealthCheckAssertionOp.Contains or HealthCheckAssertionOp.NotContains
                or HealthCheckAssertionOp.JsonEquals or HealthCheckAssertionOp.HeaderEquals;

            if (!isText && !allowStructured)
                return (null, $"{field}[].op '{op}' is only supported on webhook checks; chat checks use contains or notContains");

            string? path = null;
            if (!isText)
            {
                path = assertion.Path?.Trim();
                if (string.IsNullOrEmpty(path) || path.Length > HealthCheckLimits.MaxAssertionPathLength)
                    return (null, $"{field}[].path is required for {op} and must be at most {HealthCheckLimits.MaxAssertionPathLength} characters");
                if (isJson && !JsonPath.TryParse(path, out _))
                    return (null, $"{field}[].path '{path}' is not a supported JSON path; use $.field, $.a.b or $.items[0].name");
                if (!isJson && !path.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_'))
                    return (null, $"{field}[].path '{path}' is not a valid header name");
            }

            var value = assertion.Value ?? string.Empty;
            if (needsValue && (value.Length == 0 || value.Length > HealthCheckLimits.MaxAssertionValueLength))
                return (null, $"{field}[].value is required for {op} and must be at most {HealthCheckLimits.MaxAssertionValueLength} characters");

            result.Add(new HealthCheckAssertion { Op = op, Path = path, Value = needsValue ? value : string.Empty });
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
