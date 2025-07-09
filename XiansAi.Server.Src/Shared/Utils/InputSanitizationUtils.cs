using System.Text.RegularExpressions;

namespace Shared.Utils;

/// <summary>
/// Utility class for sanitizing inputs to prevent NoSQL injection attacks
/// </summary>
public static class InputSanitizationUtils
{
    // Sanitization patterns
    private static readonly Regex WhitespacePattern = new Regex(@"\s+", RegexOptions.Compiled);
    private static readonly Regex InvalidCharsPattern = new Regex(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled);

    /// <summary>
    /// Sanitizes ObjectId string by trimming whitespace
    /// </summary>
    /// <param name="id">The ObjectId string to sanitize</param>
    /// <returns>Sanitized ObjectId string</returns>
    public static string SanitizeObjectId(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return string.Empty;
        }

        // Trim whitespace
        return id.Trim();
    }

    /// <summary>
    /// Sanitizes string by trimming whitespace and normalizing
    /// </summary>
    /// <param name="value">The string value to sanitize</param>
    /// <returns>Sanitized string value</returns>
    public static string SanitizeString(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = value.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        return sanitized;
    }

    /// <summary>
    /// Sanitizes string by removing invalid characters and normalizing
    /// </summary>
    /// <param name="value">The string value to sanitize</param>
    /// <param name="maxLength">Maximum length to truncate to</param>
    /// <returns>Sanitized string value</returns>
    public static string SanitizeStringStrict(string value, int maxLength = 100)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = value.Trim();

        // Remove invalid characters (keep only alphanumeric, hyphens, underscores)
        sanitized = InvalidCharsPattern.Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > maxLength)
        {
            sanitized = sanitized.Substring(0, maxLength);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes tenant ID (allows spaces and special characters)
    /// </summary>
    public static string SanitizeTenantId(string tenantId)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = tenantId.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove only truly dangerous characters (control characters, null bytes, etc.)
        sanitized = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes agent name (allows spaces and special characters)
    /// </summary>
    public static string SanitizeAgent(string agent)
    {
        if (string.IsNullOrWhiteSpace(agent))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = agent.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove only truly dangerous characters (control characters, null bytes, etc.)
        sanitized = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes workflow type (allows spaces and special characters)
    /// </summary>
    public static string SanitizeWorkflowType(string workflowType)
    {
        if (string.IsNullOrWhiteSpace(workflowType))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = workflowType.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove only truly dangerous characters (control characters, null bytes, etc.)
        sanitized = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes workflow ID (allows spaces and special characters)
    /// </summary>
    public static string SanitizeWorkflowId(string workflowId)
    {
        if (string.IsNullOrWhiteSpace(workflowId))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = workflowId.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove only truly dangerous characters (control characters, null bytes, etc.)
        sanitized = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes participant ID (allows spaces and special characters)
    /// </summary>
    public static string SanitizeParticipantId(string participantId)
    {
        if (string.IsNullOrWhiteSpace(participantId))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = participantId.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove only truly dangerous characters (control characters, null bytes, etc.)
        sanitized = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes message ID
    /// </summary>
    public static string SanitizeMessageId(string messageId)
    {
        return SanitizeObjectId(messageId);
    }

    /// <summary>
    /// Sanitizes thread ID
    /// </summary>
    public static string SanitizeThreadId(string threadId)
    {
        return SanitizeObjectId(threadId);
    }

    /// <summary>
    /// Sanitizes created by field (allows spaces, dots, @, and |)
    /// </summary>
    public static string SanitizeCreatedBy(string createdBy)
    {
        if (string.IsNullOrWhiteSpace(createdBy))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = createdBy.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove only truly dangerous characters (control characters, null bytes, etc.)
        sanitized = new Regex(@"[\x00-\x1F\x7F]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes message text
    /// </summary>
    public static string? SanitizeMessageText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // For message text, we use regular sanitization (not strict) to preserve formatting
        var sanitized = SanitizeString(text);
        
        // Truncate to max length (10,000 characters)
        if (sanitized.Length > 10000)
        {
            sanitized = sanitized.Substring(0, 10000);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a ConversationMessage object
    /// </summary>
    /// <param name="message">The ConversationMessage to sanitize</param>
    /// <returns>Sanitized ConversationMessage</returns>
    public static Shared.Repositories.ConversationMessage? SanitizeConversationMessage(Shared.Repositories.ConversationMessage? message)
    {
        if (message == null)
        {
            return null;
        }

        // Sanitize string properties
        message.ThreadId = SanitizeThreadId(message.ThreadId);
        message.WorkflowId = SanitizeWorkflowId(message.WorkflowId);
        message.WorkflowType = SanitizeWorkflowType(message.WorkflowType);
        message.Text = SanitizeMessageText(message.Text);

        return message;
    }

    /// <summary>
    /// Sanitizes a FlowDefinition object
    /// </summary>
    /// <param name="definition">The FlowDefinition to sanitize</param>
    /// <returns>Sanitized FlowDefinition</returns>
    public static Shared.Data.Models.FlowDefinition? SanitizeFlowDefinition(Shared.Data.Models.FlowDefinition? definition)
    {
        if (definition == null)
        {
            return null;
        }

        // Sanitize string properties
        definition.WorkflowType = SanitizeWorkflowType(definition.WorkflowType);
        definition.Agent = SanitizeAgent(definition.Agent);
        definition.CreatedBy = SanitizeString(definition.CreatedBy ?? string.Empty);
        definition.Source = SanitizeString(definition.Source ?? string.Empty);
        definition.Markdown = SanitizeString(definition.Markdown ?? string.Empty);

        // Sanitize activity definitions
        if (definition.ActivityDefinitions != null)
        {
            foreach (var activity in definition.ActivityDefinitions)
            {
                SanitizeActivityDefinition(activity);
            }
        }

        // Sanitize parameter definitions
        if (definition.ParameterDefinitions != null)
        {
            foreach (var parameter in definition.ParameterDefinitions)
            {
                SanitizeParameterDefinition(parameter);
            }
        }

        return definition;
    }

    /// <summary>
    /// Sanitizes an ActivityDefinition object
    /// </summary>
    /// <param name="activity">The ActivityDefinition to sanitize</param>
    /// <returns>Sanitized ActivityDefinition</returns>
    public static Shared.Data.Models.ActivityDefinition? SanitizeActivityDefinition(Shared.Data.Models.ActivityDefinition? activity)
    {
        if (activity == null)
        {
            return null;
        }

        // Sanitize activity name
        activity.ActivityName = SanitizeStringStrict(activity.ActivityName, 100);

        // Sanitize knowledge IDs
        if (activity.KnowledgeIds != null)
        {
            for (int i = 0; i < activity.KnowledgeIds.Count; i++)
            {
                activity.KnowledgeIds[i] = SanitizeObjectId(activity.KnowledgeIds[i]);
            }
        }

        // Sanitize agent tool names
        if (activity.AgentToolNames != null)
        {
            for (int i = 0; i < activity.AgentToolNames.Count; i++)
            {
                activity.AgentToolNames[i] = SanitizeStringStrict(activity.AgentToolNames[i], 100);
            }
        }

        // Sanitize parameter definitions
        if (activity.ParameterDefinitions != null)
        {
            foreach (var parameter in activity.ParameterDefinitions)
            {
                SanitizeParameterDefinition(parameter);
            }
        }

        return activity;
    }

    /// <summary>
    /// Sanitizes a ParameterDefinition object
    /// </summary>
    /// <param name="parameter">The ParameterDefinition to sanitize</param>
    /// <returns>Sanitized ParameterDefinition</returns>
    public static Shared.Data.Models.ParameterDefinition? SanitizeParameterDefinition(Shared.Data.Models.ParameterDefinition? parameter)
    {
        if (parameter == null)
        {
            return null;
        }

        // Sanitize parameter name and type
        parameter.Name = SanitizeStringStrict(parameter.Name, 50);
        parameter.Type = SanitizeStringStrict(parameter.Type, 20);

        return parameter;
    }

    /// <summary>
    /// Sanitizes certificate thumbprint by trimming and converting to uppercase
    /// </summary>
    /// <param name="thumbprint">The thumbprint to sanitize</param>
    /// <returns>Sanitized thumbprint</returns>
    public static string SanitizeCertificateThumbprint(string thumbprint)
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            return string.Empty;
        }

        // Trim whitespace and convert to uppercase
        return thumbprint.Trim().ToUpperInvariant();
    }

    /// <summary>
    /// Sanitizes a Certificate object
    /// </summary>
    /// <param name="certificate">The Certificate to sanitize</param>
    /// <returns>Sanitized Certificate</returns>
    public static Features.AgentApi.Models.Certificate? SanitizeCertificate(Features.AgentApi.Models.Certificate? certificate)
    {
        if (certificate == null)
        {
            return null;
        }

        // Sanitize string properties
        certificate.Thumbprint = SanitizeCertificateThumbprint(certificate.Thumbprint);
        certificate.SubjectName = SanitizeStringStrict(certificate.SubjectName, 200);
        certificate.TenantId = SanitizeTenantId(certificate.TenantId);
        certificate.IssuedTo = SanitizeStringStrict(certificate.IssuedTo, 100);
        certificate.RevocationReason = SanitizeString(certificate.RevocationReason ?? string.Empty);

        return certificate;
    }

    /// <summary>
    /// Sanitizes webhook URL by trimming and normalizing
    /// </summary>
    /// <param name="url">The URL to sanitize</param>
    /// <returns>Sanitized URL</returns>
    public static string SanitizeWebhookUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = url.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        return sanitized;
    }

    /// <summary>
    /// Sanitizes webhook event type
    /// </summary>
    /// <param name="eventType">The event type to sanitize</param>
    /// <returns>Sanitized event type</returns>
    public static string SanitizeWebhookEventType(string eventType)
    {
        return SanitizeStringStrict(eventType, 50);
    }

    /// <summary>
    /// Sanitizes webhook secret
    /// </summary>
    /// <param name="secret">The secret to sanitize</param>
    /// <returns>Sanitized secret</returns>
    public static string SanitizeWebhookSecret(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            return string.Empty;
        }

        // Trim whitespace
        return secret.Trim();
    }

    /// <summary>
    /// Sanitizes a Webhook object
    /// </summary>
    /// <param name="webhook">The Webhook to sanitize</param>
    /// <returns>Sanitized Webhook</returns>
    public static XiansAi.Server.Shared.Data.Models.Webhook? SanitizeWebhook(XiansAi.Server.Shared.Data.Models.Webhook? webhook)
    {
        if (webhook == null)
        {
            return null;
        }

        // Sanitize string properties
        webhook.TenantId = SanitizeTenantId(webhook.TenantId);
        webhook.WorkflowId = SanitizeWorkflowId(webhook.WorkflowId);
        webhook.CallbackUrl = SanitizeWebhookUrl(webhook.CallbackUrl);
        webhook.EventType = SanitizeWebhookEventType(webhook.EventType);
        webhook.Secret = SanitizeWebhookSecret(webhook.Secret);
        webhook.CreatedBy = SanitizeString(webhook.CreatedBy ?? string.Empty);

        return webhook;
    }

    /// <summary>
    /// Sanitizes activity ID
    /// </summary>
    /// <param name="activityId">The activity ID to sanitize</param>
    /// <returns>Sanitized activity ID</returns>
    public static string SanitizeActivityId(string activityId)
    {
        return SanitizeStringStrict(activityId, 100);
    }

    /// <summary>
    /// Sanitizes activity name
    /// </summary>
    /// <param name="activityName">The activity name to sanitize</param>
    /// <returns>Sanitized activity name</returns>
    public static string SanitizeActivityName(string activityName)
    {
        if (string.IsNullOrWhiteSpace(activityName))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = activityName.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove invalid characters (keep alphanumeric, hyphens, underscores, spaces)
        sanitized = new Regex(@"[^a-zA-Z0-9_\-\s]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 200)
        {
            sanitized = sanitized.Substring(0, 200);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes task queue name
    /// </summary>
    /// <param name="taskQueue">The task queue to sanitize</param>
    /// <returns>Sanitized task queue</returns>
    public static string SanitizeTaskQueue(string taskQueue)
    {
        return SanitizeStringStrict(taskQueue, 100);
    }

    /// <summary>
    /// Sanitizes search term
    /// </summary>
    /// <param name="searchTerm">The search term to sanitize</param>
    /// <returns>Sanitized search term</returns>
    public static string SanitizeSearchTerm(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = searchTerm.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove invalid characters (keep alphanumeric, hyphens, underscores, spaces)
        sanitized = new Regex(@"[^a-zA-Z0-9_\-\s]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes an Activity object
    /// </summary>
    /// <param name="activity">The Activity to sanitize</param>
    /// <returns>Sanitized Activity</returns>
    public static Features.WebApi.Models.Activity? SanitizeActivity(Features.WebApi.Models.Activity? activity)
    {
        if (activity == null)
        {
            return null;
        }

        // Sanitize string properties
        activity.ActivityId = SanitizeActivityId(activity.ActivityId ?? string.Empty);
        activity.ActivityName = SanitizeActivityName(activity.ActivityName ?? string.Empty);
        activity.WorkflowId = SanitizeWorkflowId(activity.WorkflowId ?? string.Empty);
        activity.WorkflowType = SanitizeWorkflowType(activity.WorkflowType ?? string.Empty);
        activity.TaskQueue = SanitizeTaskQueue(activity.TaskQueue ?? string.Empty);

        // Sanitize collections
        if (activity.AgentToolNames != null)
        {
            for (int i = 0; i < activity.AgentToolNames.Count; i++)
            {
                activity.AgentToolNames[i] = SanitizeStringStrict(activity.AgentToolNames[i], 100);
            }
        }

        if (activity.InstructionIds != null)
        {
            for (int i = 0; i < activity.InstructionIds.Count; i++)
            {
                activity.InstructionIds[i] = SanitizeObjectId(activity.InstructionIds[i]);
            }
        }

        return activity;
    }

    /// <summary>
    /// Sanitizes workflow run ID
    /// </summary>
    /// <param name="workflowRunId">The workflow run ID to sanitize</param>
    /// <returns>Sanitized workflow run ID</returns>
    public static string SanitizeWorkflowRunId(string workflowRunId)
    {
        return SanitizeStringStrict(workflowRunId, 100);
    }

    /// <summary>
    /// Sanitizes log message
    /// </summary>
    /// <param name="message">The log message to sanitize</param>
    /// <returns>Sanitized log message</returns>
    public static string SanitizeLogMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = message.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Truncate to max length (10KB)
        if (sanitized.Length > 10000)
        {
            sanitized = sanitized.Substring(0, 10000);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes log exception
    /// </summary>
    /// <param name="exception">The log exception to sanitize</param>
    /// <returns>Sanitized log exception</returns>
    public static string? SanitizeLogException(string? exception)
    {
        if (string.IsNullOrWhiteSpace(exception))
        {
            return null;
        }

        // Trim whitespace
        var sanitized = exception.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Truncate to max length (50KB)
        if (sanitized.Length > 50000)
        {
            sanitized = sanitized.Substring(0, 50000);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a Log object
    /// </summary>
    /// <param name="log">The Log to sanitize</param>
    /// <returns>Sanitized Log</returns>
    public static Features.WebApi.Models.Log? SanitizeLog(Features.WebApi.Models.Log? log)
    {
        if (log == null)
        {
            return null;
        }

        // Sanitize string properties
        log.Id = SanitizeObjectId(log.Id);
        log.TenantId = SanitizeTenantId(log.TenantId);
        log.Message = SanitizeLogMessage(log.Message);
        log.WorkflowId = SanitizeWorkflowId(log.WorkflowId);
        log.WorkflowRunId = SanitizeWorkflowRunId(log.WorkflowRunId);
        log.WorkflowType = SanitizeWorkflowType(log.WorkflowType);
        log.Agent = SanitizeAgent(log.Agent);

        // Sanitize optional string properties
        if (!string.IsNullOrWhiteSpace(log.ParticipantId))
        {
            log.ParticipantId = SanitizeParticipantId(log.ParticipantId);
        }

        if (!string.IsNullOrWhiteSpace(log.Exception))
        {
            log.Exception = SanitizeLogException(log.Exception);
        }

        // Sanitize properties dictionary if present
        if (log.Properties != null)
        {
            var sanitizedProperties = new Dictionary<string, object>();
            foreach (var kvp in log.Properties)
            {
                var sanitizedKey = SanitizeStringStrict(kvp.Key, 100);
                if (!string.IsNullOrWhiteSpace(sanitizedKey))
                {
                    // For string values, sanitize them
                    if (kvp.Value is string stringValue)
                    {
                        sanitizedProperties[sanitizedKey] = SanitizeString(stringValue);
                    }
                    else
                    {
                        sanitizedProperties[sanitizedKey] = kvp.Value;
                    }
                }
            }
            log.Properties = sanitizedProperties;
        }

        return log;
    }

    /// <summary>
    /// Sanitizes tenant name
    /// </summary>
    /// <param name="name">The tenant name to sanitize</param>
    /// <returns>Sanitized tenant name</returns>
    public static string SanitizeTenantName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = name.Trim();

        sanitized = WhitespacePattern.Replace(sanitized, " ");
      sanitized = new Regex(@"[^a-zA-Z0-9_\-\s@.]", RegexOptions.Compiled).Replace(sanitized, "");
        // Truncate to max length
        if (sanitized.Length > 200)
        {
            sanitized = sanitized.Substring(0, 200);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes domain name
    /// </summary>
    /// <param name="domain">The domain to sanitize</param>
    /// <returns>Sanitized domain</returns>
    public static string SanitizeDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = domain.Trim().ToLowerInvariant();

        // Remove invalid characters (keep alphanumeric, hyphens, dots)
        sanitized = new Regex(@"[^a-zA-Z0-9\-\.]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes description
    /// </summary>
    /// <param name="description">The description to sanitize</param>
    /// <returns>Sanitized description</returns>
    public static string? SanitizeDescription(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return null;
        }

        // Trim whitespace
        var sanitized = description.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Truncate to max length
        if (sanitized.Length > 1000)
        {
            sanitized = sanitized.Substring(0, 1000);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes theme
    /// </summary>
    /// <param name="theme">The theme to sanitize</param>
    /// <returns>Sanitized theme</returns>
    public static string? SanitizeTheme(string? theme)
    {
        if (string.IsNullOrWhiteSpace(theme))
        {
            return null;
        }

        // Trim whitespace
        var sanitized = theme.Trim();

        // Remove invalid characters (keep alphanumeric, hyphens, underscores)
        sanitized = new Regex(@"[^a-zA-Z0-9_-]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes timezone
    /// </summary>
    /// <param name="timezone">The timezone to sanitize</param>
    /// <returns>Sanitized timezone</returns>
    public static string? SanitizeTimezone(string? timezone)
    {
        if (string.IsNullOrWhiteSpace(timezone))
        {
            return null;
        }

        // Trim whitespace
        var sanitized = timezone.Trim();

        // Remove invalid characters (keep alphanumeric, hyphens, underscores, slashes, plus signs)
        sanitized = new Regex(@"[^a-zA-Z0-9_/+-]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 50)
        {
            sanitized = sanitized.Substring(0, 50);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes tenant search term
    /// </summary>
    /// <param name="searchTerm">The search term to sanitize</param>
    /// <returns>Sanitized search term</returns>
    public static string SanitizeTenantSearchTerm(string searchTerm)
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = searchTerm.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove invalid characters (keep alphanumeric, hyphens, underscores, spaces, dots)
        sanitized = new Regex(@"[^a-zA-Z0-9_\-\s\.]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 100)
        {
            sanitized = sanitized.Substring(0, 100);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes a Tenant object
    /// </summary>
    /// <param name="tenant">The Tenant to sanitize</param>
    /// <returns>Sanitized Tenant</returns>
    public static Features.WebApi.Models.Tenant? SanitizeTenant(Features.WebApi.Models.Tenant? tenant)
    {
        if (tenant == null)
        {
            return null;
        }

        // Sanitize string properties
        tenant.Id = SanitizeObjectId(tenant.Id);
        tenant.TenantId = SanitizeTenantId(tenant.TenantId);
        tenant.Name = SanitizeTenantName(tenant.Name);
        tenant.Domain = SanitizeDomain(tenant.Domain);
        tenant.Description = SanitizeDescription(tenant.Description);
        tenant.Theme = SanitizeTheme(tenant.Theme);
        tenant.Timezone = SanitizeTimezone(tenant.Timezone);
        tenant.CreatedBy = SanitizeString(tenant.CreatedBy ?? string.Empty);

        return tenant;
    }

    /// <summary>
    /// Sanitizes user ID
    /// </summary>
    public static string SanitizeUserId(string userId)
    {
        return SanitizeStringStrict(userId, 100);
    }

    /// <summary>
    /// Sanitizes revocation reason
    /// </summary>
    /// <param name="reason">The revocation reason to sanitize</param>
    /// <returns>Sanitized revocation reason</returns>
    public static string SanitizeRevocationReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return string.Empty;
        }

        // Trim whitespace
        var sanitized = reason.Trim();

        // Normalize multiple whitespace to single space
        sanitized = WhitespacePattern.Replace(sanitized, " ");

        // Remove invalid characters (keep alphanumeric, spaces, hyphens, underscores, dots, @, !, ?)
        sanitized = new Regex(@"[^a-zA-Z0-9_\-\s\.@!?]", RegexOptions.Compiled).Replace(sanitized, "");

        // Truncate to max length
        if (sanitized.Length > 500)
        {
            sanitized = sanitized.Substring(0, 500);
        }

        return sanitized;
    }

    /// <summary>
    /// Sanitizes user roles array
    /// </summary>
    public static string[]? SanitizeUserRoles(string[]? userRoles)
    {
        if (userRoles == null || userRoles.Length == 0)
        {
            return null;
        }

        return userRoles.Select(role => SanitizeStringStrict(role, 50))
                      .Where(role => !string.IsNullOrWhiteSpace(role))
                      .ToArray();
    }

    /// <summary>
    /// Sanitizes an Agent object
    /// </summary>
    public static Shared.Data.Models.Agent? SanitizeAgent(Shared.Data.Models.Agent? agent)
    {
        if (agent == null)
        {
            return null;
        }

        agent.Name = SanitizeAgent(agent.Name);
        agent.Tenant = SanitizeTenantId(agent.Tenant);
        agent.CreatedBy = SanitizeCreatedBy(agent.CreatedBy);

        return agent;
    }
} 