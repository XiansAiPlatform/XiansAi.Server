using System.Text.RegularExpressions;

namespace Shared.Utils;

/// <summary>
/// Utility class for validating inputs to prevent NoSQL injection attacks
/// </summary>
public static class InputValidationUtils
{
    // Validation patterns
    private static readonly Regex ValidObjectIdPattern = new Regex(@"^[0-9a-fA-F]{24}$", RegexOptions.Compiled);
    private static readonly Regex ValidTenantIdPattern = new Regex(@"^[a-zA-Z0-9._@-]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex ValidAgentPattern = new Regex(@"^[a-zA-Z0-9 ._@-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidWorkflowTypePattern = new Regex(@"^[a-zA-Z0-9 _\-\.]{1,100}$", RegexOptions.Compiled);

    private static readonly Regex ValidParticipantIdPattern = new Regex(@"^[a-zA-Z0-9 .@_-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidWorkflowIdPattern = new Regex(@"^[a-zA-Z0-9_\-\.]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidCreatedByPattern = new Regex(@"^[a-zA-Z0-9_\-\s\.@|]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidEventTypePattern = new Regex(@"^[a-zA-Z0-9_\-\s\.]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex ValidActivityIdPattern = new Regex(@"^[a-zA-Z0-9_\-\.]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidActivityNamePattern = new Regex(@"^[a-zA-Z0-9_\-\s]{1,200}$", RegexOptions.Compiled);
    private static readonly Regex ValidTaskQueuePattern = new Regex(@"^[a-zA-Z0-9_\-\.]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidSearchTermPattern = new Regex(@"^[a-zA-Z0-9 _\-\.]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidTenantNamePattern = new Regex(@"^[a-zA-Z0-9 _\-\.@]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidDomainPattern = new Regex(@"^(?:[a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,61}[a-zA-Z0-9])?\.)+[a-zA-Z]{2,}$", RegexOptions.Compiled);
    private static readonly Regex ValidThemePattern = new Regex(@"^[a-zA-Z0-9 _\-.]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex ValidTimezonePattern = new Regex(@"^[a-zA-Z0-9/_+\-]{1,50}$", RegexOptions.Compiled);
    private static readonly Regex ValidTenantSearchTermPattern = new Regex(@"^[a-zA-Z0-9 _\-\.@]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidUserIdPattern = new Regex(@"^[a-zA-Z0-9|@._:-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidRevocationReasonPattern = new Regex(@"^[a-zA-Z0-9 _\-\.@!?]{1,500}$", RegexOptions.Compiled);
    /// <summary>
    /// Validates if the input is a valid ObjectId format
    /// </summary>
    /// <param name="id">The ObjectId string to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateObjectId(string id, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (!ValidObjectIdPattern.IsMatch(id))
        {
            throw new ArgumentException($"{parameterName} must be a valid 24-character hexadecimal ObjectId", parameterName);
        }
    }

    /// <summary>
    /// Validates and casts string to Integer to prevent NoSQL injection
    /// </summary>
    /// <param name="value">The string value to validate and cast</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <returns>Casted integer value</returns>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static int ValidateAndCastToInt(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (!int.TryParse(value, out int result))
        {
            throw new ArgumentException($"{parameterName} must be a valid integer", parameterName);
        }

        return result;
    }

    /// <summary>
    /// Validates and casts string to Guid to prevent NoSQL injection
    /// </summary>
    /// <param name="value">The string value to validate and cast</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <returns>Casted Guid value</returns>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static Guid ValidateAndCastToGuid(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (!Guid.TryParse(value, out Guid result))
        {
            throw new ArgumentException($"{parameterName} must be a valid GUID", parameterName);
        }

        return result;
    }

    /// <summary>
    /// Validates if the input string matches the specified pattern and length
    /// </summary>
    /// <param name="value">The string value to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <param name="pattern">Regex pattern for validation</param>
    /// <param name="maxLength">Maximum allowed length</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateString(string value, string parameterName, Regex pattern, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (value.Length > maxLength)
        {
            throw new ArgumentException($"{parameterName} cannot exceed {maxLength} characters", parameterName);
        }

        if (!pattern.IsMatch(value))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates tenant ID
    /// </summary>
    public static void ValidateTenantId(string tenantId, string parameterName = "tenantId")
    {
        ValidateString(tenantId, parameterName, ValidTenantIdPattern, 50);
    }

    /// <summary>
    /// Validates agent name
    /// </summary>
    public static void ValidateAgentName(string agentName, string parameterName = "agentName")
    {
        ValidateString(agentName, parameterName, ValidAgentPattern, 100);
    }

    /// <summary>
    /// Validates workflow type
    /// </summary>
    public static void ValidateWorkflowType(string workflowType, string parameterName = "workflowType")
    {
        ValidateString(workflowType, parameterName, ValidWorkflowTypePattern, 100);
    }

    /// <summary>
    /// Validates workflow ID
    /// </summary>
    public static void ValidateWorkflowId(string workflowId, string parameterName = "workflowId")
    {
        ValidateString(workflowId, parameterName, ValidWorkflowIdPattern, 100);
    }

    /// <summary>
    /// Validates participant ID
    /// </summary>
    public static void ValidateParticipantId(string participantId, string parameterName = "participantId")
    {
        ValidateString(participantId, parameterName, ValidParticipantIdPattern, 100);
    }

    /// <summary>
    /// Validates created by field
    /// </summary>
    public static void ValidateCreatedBy(string createdBy, string parameterName = "createdBy")
    {
        if (string.IsNullOrWhiteSpace(createdBy))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (createdBy.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }

        // Allow alphanumeric characters, hyphens, underscores, spaces, and common punctuation
        if (!ValidCreatedByPattern.IsMatch(createdBy))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, underscores, spaces, dots, and @ symbols are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates a FlowDefinition object for completeness and correctness
    /// </summary>
    /// <param name="definition">The FlowDefinition to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateFlowDefinition(Shared.Data.Models.FlowDefinition? definition)
    {
        if (definition == null)
        {
            throw new ArgumentException("Flow definition cannot be null", nameof(definition));
        }

        // Validate required string properties
        if (string.IsNullOrWhiteSpace(definition.WorkflowType))
        {
            throw new ArgumentException("WorkflowType is required", nameof(definition.WorkflowType));
        }

        if (string.IsNullOrWhiteSpace(definition.Agent))
        {
            throw new ArgumentException("Agent is required", nameof(definition.Agent));
        }

        if (string.IsNullOrWhiteSpace(definition.Hash))
        {
            throw new ArgumentException("Hash is required", nameof(definition.Hash));
        }

        if (string.IsNullOrWhiteSpace(definition.CreatedBy))
        {
            throw new ArgumentException("CreatedBy is required", nameof(definition.CreatedBy));
        }

        // Validate collections
        if (definition.ActivityDefinitions == null || definition.ActivityDefinitions.Count == 0)
        {
            throw new ArgumentException("At least one ActivityDefinition is required", nameof(definition.ActivityDefinitions));
        }

        if (definition.ParameterDefinitions == null)
        {
            definition.ParameterDefinitions = new List<Shared.Data.Models.ParameterDefinition>();
        }

        // Validate each activity definition
        foreach (var activity in definition.ActivityDefinitions)
        {
            ValidateActivityDefinition(activity);
        }

        // Validate each parameter definition
        foreach (var parameter in definition.ParameterDefinitions)
        {
            ValidateParameterDefinition(parameter);
        }
    }

    /// <summary>
    /// Validates an ActivityDefinition object
    /// </summary>
    /// <param name="activity">The ActivityDefinition to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateActivityDefinition(Shared.Data.Models.ActivityDefinition? activity)
    {
        if (activity == null)
        {
            throw new ArgumentException("ActivityDefinition cannot be null");
        }

        if (string.IsNullOrWhiteSpace(activity.ActivityName))
        {
            throw new ArgumentException("ActivityName is required", nameof(activity.ActivityName));
        }

        // Validate knowledge IDs
        if (activity.KnowledgeIds == null || activity.KnowledgeIds.Count == 0)
        {
            throw new ArgumentException("At least one KnowledgeId is required", nameof(activity.KnowledgeIds));
        }

        // Validate each knowledge ID
        for (int i = 0; i < activity.KnowledgeIds.Count; i++)
        {
            var knowledgeId = activity.KnowledgeIds[i];
            if (string.IsNullOrWhiteSpace(knowledgeId))
            {

                throw new ArgumentException($"KnowledgeId at index {i} cannot be null or empty");
            }
            ValidateObjectId(knowledgeId, $"KnowledgeId at index {i}");
        }

        // Validate parameter definitions
        if (activity.ParameterDefinitions == null)
        {
            activity.ParameterDefinitions = new List<Shared.Data.Models.ParameterDefinition>();
        }

        foreach (var parameter in activity.ParameterDefinitions)
        {
            ValidateParameterDefinition(parameter);
        }
    }

    /// <summary>
    /// Validates a ParameterDefinition object
    /// </summary>
    /// <param name="parameter">The ParameterDefinition to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateParameterDefinition(Shared.Data.Models.ParameterDefinition? parameter)
    {
        if (parameter == null)
        {
            throw new ArgumentException("ParameterDefinition cannot be null");
        }

        if (string.IsNullOrWhiteSpace(parameter.Name))
        {
            throw new ArgumentException("Parameter name is required", nameof(parameter.Name));
        }

        if (string.IsNullOrWhiteSpace(parameter.Type))
        {
            throw new ArgumentException("Parameter type is required", nameof(parameter.Type));
        }
    }

    /// <summary>
    /// Validates certificate thumbprint format
    /// </summary>
    /// <param name="thumbprint">The thumbprint to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateCertificateThumbprint(string thumbprint, string parameterName = "thumbprint")
    {
        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        // Certificate thumbprints are typically 40-character SHA1 hashes
        if (thumbprint.Length != 40)
        {
            throw new ArgumentException($"{parameterName} must be exactly 40 characters", parameterName);
        }

        // Validate hex format
        if (!thumbprint.All(c => char.IsLetterOrDigit(c) && c <= 'F'))
        {
            throw new ArgumentException($"{parameterName} must contain only hexadecimal characters", parameterName);
        }
    }

    /// <summary>
    /// Validates a Certificate object
    /// </summary>
    /// <param name="certificate">The Certificate to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateCertificate(Features.AgentApi.Models.Certificate? certificate)
    {
        if (certificate == null)
        {
            throw new ArgumentException("Certificate cannot be null");
        }

        if (string.IsNullOrWhiteSpace(certificate.Thumbprint))
        {
            throw new ArgumentException("Thumbprint is required", nameof(certificate.Thumbprint));
        }

        if (string.IsNullOrWhiteSpace(certificate.SubjectName))
        {
            throw new ArgumentException("SubjectName is required", nameof(certificate.SubjectName));
        }

        if (string.IsNullOrWhiteSpace(certificate.TenantId))
        {
            throw new ArgumentException("TenantId is required", nameof(certificate.TenantId));
        }

        if (string.IsNullOrWhiteSpace(certificate.IssuedTo))
        {
            throw new ArgumentException("IssuedTo is required", nameof(certificate.IssuedTo));
        }

        // Validate thumbprint format
        ValidateCertificateThumbprint(certificate.Thumbprint, nameof(certificate.Thumbprint));

        // Validate dates
        if (certificate.ExpiresAt <= certificate.IssuedAt)
        {
            throw new ArgumentException("ExpiresAt must be after IssuedAt");
        }

        if (certificate.ExpiresAt <= DateTime.UtcNow)
        {
            throw new ArgumentException("Certificate has already expired");
        }
    }

    /// <summary>
    /// Validates webhook URL format
    /// </summary>
    /// <param name="url">The URL to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateWebhookUrl(string url, string parameterName = "callbackUrl")
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"{parameterName} must be a valid absolute URL", parameterName);
        }

        if (uri.Scheme != "http" && uri.Scheme != "https")
        {
            throw new ArgumentException($"{parameterName} must use HTTP or HTTPS protocol", parameterName);
        }

        if (url.Length > 500)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 500 characters", parameterName);
        }
    }

    /// <summary>
    /// Validates webhook event type
    /// </summary>
    /// <param name="eventType">The event type to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateWebhookEventType(string eventType, string parameterName = "eventType")
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (eventType.Length > 50)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 50 characters", parameterName);
        }

        // Validate event type format (alphanumeric, hyphens, underscores)
        if (!ValidEventTypePattern.IsMatch(eventType))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates webhook secret
    /// </summary>
    /// <param name="secret">The secret to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateWebhookSecret(string secret, string parameterName = "secret")
    {
        if (string.IsNullOrWhiteSpace(secret))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (secret.Length < 16)
        {
            throw new ArgumentException($"{parameterName} must be at least 16 characters long", parameterName);
        }

        if (secret.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }
    }

    /// <summary>
    /// Validates a Webhook object
    /// </summary>
    /// <param name="webhook">The Webhook to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateWebhook(XiansAi.Server.Shared.Data.Models.Webhook? webhook)
    {
        if (webhook == null)
        {
            throw new ArgumentException("Webhook cannot be null");
        }

        if (string.IsNullOrWhiteSpace(webhook.TenantId))
        {
            throw new ArgumentException("TenantId is required", nameof(webhook.TenantId));
        }

        if (string.IsNullOrWhiteSpace(webhook.WorkflowId))
        {
            throw new ArgumentException("WorkflowId is required", nameof(webhook.WorkflowId));
        }

        if (string.IsNullOrWhiteSpace(webhook.CallbackUrl))
        {
            throw new ArgumentException("CallbackUrl is required", nameof(webhook.CallbackUrl));
        }

        if (string.IsNullOrWhiteSpace(webhook.EventType))
        {
            throw new ArgumentException("EventType is required", nameof(webhook.EventType));
        }

        if (string.IsNullOrWhiteSpace(webhook.Secret))
        {
            throw new ArgumentException("Secret is required", nameof(webhook.Secret));
        }

        // Validate individual fields
        ValidateTenantId(webhook.TenantId, nameof(webhook.TenantId));
        ValidateWorkflowId(webhook.WorkflowId, nameof(webhook.WorkflowId));
        ValidateWebhookUrl(webhook.CallbackUrl, nameof(webhook.CallbackUrl));
        ValidateWebhookEventType(webhook.EventType, nameof(webhook.EventType));
        ValidateWebhookSecret(webhook.Secret, nameof(webhook.Secret));

        // Validate CreatedBy if provided
        if (!string.IsNullOrWhiteSpace(webhook.CreatedBy))
        {
            ValidateString(webhook.CreatedBy, nameof(webhook.CreatedBy), ValidAgentPattern, 100);
        }
    }

    /// <summary>
    /// Validates activity ID format
    /// </summary>
    /// <param name="activityId">The activity ID to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateActivityId(string activityId, string parameterName = "activityId")
    {
        if (string.IsNullOrWhiteSpace(activityId))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (activityId.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }

        // Validate activity ID format (alphanumeric, hyphens, underscores)
        if (!ValidActivityIdPattern.IsMatch(activityId))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates activity name
    /// </summary>
    /// <param name="activityName">The activity name to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateActivityName(string activityName, string parameterName = "activityName")
    {
        if (string.IsNullOrWhiteSpace(activityName))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (activityName.Length > 200)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 200 characters", parameterName);
        }

        // Validate activity name format (alphanumeric, hyphens, underscores, spaces)
        if (!ValidActivityNamePattern.IsMatch(activityName))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, underscores, and spaces are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates task queue name
    /// </summary>
    /// <param name="taskQueue">The task queue to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateTaskQueue(string taskQueue, string parameterName = "taskQueue")
    {
        if (string.IsNullOrWhiteSpace(taskQueue))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (taskQueue.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }

        // Validate task queue format (alphanumeric, hyphens, underscores, colons)
        if (!ValidTaskQueuePattern.IsMatch(taskQueue))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, underscores, and colons are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates search term
    /// </summary>
    /// <param name="searchTerm">The search term to validate</param>
    /// <param name="parameterName">Name of the parameter for error messages</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateSearchTerm(string searchTerm, string parameterName = "searchTerm")
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (searchTerm.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }

        // Validate search term format (alphanumeric, hyphens, underscores, spaces)
        if (!ValidSearchTermPattern.IsMatch(searchTerm))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, underscores, and spaces are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates an Activity object
    /// </summary>
    /// <param name="activity">The Activity to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateActivity(Features.WebApi.Models.Activity? activity)
    {
        if (activity == null)
        {
            throw new ArgumentException("Activity cannot be null");
        }

        // Validate required string properties
        if (!string.IsNullOrWhiteSpace(activity.ActivityId))
        {
            ValidateActivityId(activity.ActivityId, nameof(activity.ActivityId));
        }

        if (!string.IsNullOrWhiteSpace(activity.ActivityName))
        {
            ValidateActivityName(activity.ActivityName, nameof(activity.ActivityName));
        }

        if (!string.IsNullOrWhiteSpace(activity.WorkflowId))
        {
            ValidateWorkflowId(activity.WorkflowId, nameof(activity.WorkflowId));
        }

        if (!string.IsNullOrWhiteSpace(activity.WorkflowType))
        {
            ValidateWorkflowType(activity.WorkflowType, nameof(activity.WorkflowType));
        }

        if (!string.IsNullOrWhiteSpace(activity.TaskQueue))
        {
            ValidateTaskQueue(activity.TaskQueue, nameof(activity.TaskQueue));
        }

        // Validate dates
        if (activity.StartedTime.HasValue && activity.EndedTime.HasValue)
        {
            if (activity.EndedTime.Value <= activity.StartedTime.Value)
            {
                throw new ArgumentException("EndedTime must be after StartedTime");
            }
        }

        // Validate collections
        if (activity.AgentToolNames != null)
        {
            foreach (var toolName in activity.AgentToolNames)
            {
                if (!string.IsNullOrWhiteSpace(toolName))
                {
                    ValidateString(toolName, "AgentToolName", ValidAgentPattern, 100);
                }
            }
        }

        if (activity.InstructionIds != null)
        {
            foreach (var instructionId in activity.InstructionIds)
            {
                if (!string.IsNullOrWhiteSpace(instructionId))
                {
                    ValidateObjectId(instructionId, "InstructionId");
                }
            }
        }
    }

    /// <summary>
    /// Validates workflow run ID
    /// </summary>
    public static void ValidateWorkflowRunId(string workflowRunId, string parameterName = "workflowRunId")
    {
        ValidateString(workflowRunId, parameterName, ValidWorkflowIdPattern, 100);
    }

    /// <summary>
    /// Validates log message
    /// </summary>
    public static void ValidateLogMessage(string message, string parameterName = "message")
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (message.Length > 10000) // 10KB limit for log messages
        {
            throw new ArgumentException($"{parameterName} cannot exceed 10000 characters", parameterName);
        }
    }

    /// <summary>
    /// Validates log exception
    /// </summary>
    public static void ValidateLogException(string? exception, string parameterName = "exception")
    {
        if (!string.IsNullOrWhiteSpace(exception) && exception.Length > 50000) // 50KB limit for exceptions
        {
            throw new ArgumentException($"{parameterName} cannot exceed 50000 characters", parameterName);
        }
    }


    /// <summary>
    /// Validates pagination parameters
    /// </summary>
    public static void ValidatePagination(int skip, int limit, string skipParameterName = "skip", string limitParameterName = "limit")
    {
        if (skip < 0)
        {
            throw new ArgumentException($"{skipParameterName} cannot be negative", skipParameterName);
        }

        if (limit <= 0)
        {
            throw new ArgumentException($"{limitParameterName} must be greater than 0", limitParameterName);
        }

        if (limit > 1000) // Reasonable limit to prevent abuse
        {
            throw new ArgumentException($"{limitParameterName} cannot exceed 1000", limitParameterName);
        }
    }

    /// <summary>
    /// Validates a Log object for completeness and correctness
    /// </summary>
    /// <param name="log">The Log to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateLog(Features.WebApi.Models.Log? log)
    {
        if (log == null)
        {
            throw new ArgumentException("Log cannot be null", nameof(log));
        }

        // Validate required string properties
        if (string.IsNullOrWhiteSpace(log.Id))
        {
            throw new ArgumentException("Id is required", nameof(log.Id));
        }

        if (string.IsNullOrWhiteSpace(log.TenantId))
        {
            throw new ArgumentException("TenantId is required", nameof(log.TenantId));
        }

        if (string.IsNullOrWhiteSpace(log.Message))
        {
            throw new ArgumentException("Message is required", nameof(log.Message));
        }

        if (string.IsNullOrWhiteSpace(log.WorkflowId))
        {
            throw new ArgumentException("WorkflowId is required", nameof(log.WorkflowId));
        }

        if (string.IsNullOrWhiteSpace(log.WorkflowRunId))
        {
            throw new ArgumentException("WorkflowRunId is required", nameof(log.WorkflowRunId));
        }

        if (string.IsNullOrWhiteSpace(log.WorkflowType))
        {
            throw new ArgumentException("WorkflowType is required", nameof(log.WorkflowType));
        }

        if (string.IsNullOrWhiteSpace(log.Agent))
        {
            throw new ArgumentException("Agent is required", nameof(log.Agent));
        }

        // Validate dates
        if (log.CreatedAt <= DateTime.MinValue)
        {
            throw new ArgumentException("CreatedAt must be a valid date", nameof(log.CreatedAt));
        }

        // Validate individual fields
        ValidateObjectId(log.Id, nameof(log.Id));
        ValidateTenantId(log.TenantId, nameof(log.TenantId));
        ValidateLogMessage(log.Message, nameof(log.Message));
        ValidateWorkflowId(log.WorkflowId, nameof(log.WorkflowId));
        ValidateWorkflowRunId(log.WorkflowRunId, nameof(log.WorkflowRunId));
        ValidateWorkflowType(log.WorkflowType, nameof(log.WorkflowType));
        ValidateAgentName(log.Agent, nameof(log.Agent));

        // Validate optional fields if present
        if (!string.IsNullOrWhiteSpace(log.ParticipantId))
        {
            ValidateParticipantId(log.ParticipantId, nameof(log.ParticipantId));
        }

        if (!string.IsNullOrWhiteSpace(log.Exception))
        {
            ValidateLogException(log.Exception, nameof(log.Exception));
        }
    }

    /// <summary>
    /// Validates tenant name
    /// </summary>
    public static void ValidateTenantName(string name, string parameterName = "name")
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (name.Length > 200)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 200 characters", parameterName);
        }

        // Validate tenant name format (alphanumeric, hyphens, underscores, spaces)
        if (!ValidTenantNamePattern.IsMatch(name))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, underscores, and spaces are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates domain name
    /// </summary>
    public static void ValidateDomain(string domain, string parameterName = "domain")
    {
        if (string.IsNullOrWhiteSpace(domain))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (domain.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }

        // Validate domain format (alphanumeric, hyphens, dots)
        if (!ValidDomainPattern.IsMatch(domain))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, and dots are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates description
    /// </summary>
    public static void ValidateDescription(string? description, string parameterName = "description")
    {
        if (!string.IsNullOrWhiteSpace(description) && description.Length > 1000)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 1000 characters", parameterName);
        }
    }

    /// <summary>
    /// Validates theme
    /// </summary>
    public static void ValidateTheme(string? theme, string parameterName = "theme")
    {
        if (!string.IsNullOrWhiteSpace(theme) && theme.Length > 50)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 50 characters", parameterName);
        }

        if (!string.IsNullOrWhiteSpace(theme) && !ValidThemePattern.IsMatch(theme))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, and underscores are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates timezone
    /// </summary>
    public static void ValidateTimezone(string? timezone, string parameterName = "timezone")
    {
        if (!string.IsNullOrWhiteSpace(timezone) && timezone.Length > 50)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 50 characters", parameterName);
        }

        if (!string.IsNullOrWhiteSpace(timezone) && !ValidTimezonePattern.IsMatch(timezone))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, underscores, slashes, and plus signs are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates search term
    /// </summary>
    public static void ValidateTenantSearchTerm(string searchTerm, string parameterName = "searchTerm")
    {
        if (string.IsNullOrWhiteSpace(searchTerm))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (searchTerm.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }

        // Validate search term format (alphanumeric, hyphens, underscores, spaces, dots)
        if (!ValidTenantSearchTermPattern.IsMatch(searchTerm))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, hyphens, underscores, spaces, and dots are allowed.", parameterName);
        }
    }

    /// <summary>
    /// Validates a Tenant object for completeness and correctness
    /// </summary>
    /// <param name="tenant">The Tenant to validate</param>
    /// <exception cref="ArgumentException">Thrown when validation fails</exception>
    public static void ValidateTenant(Features.WebApi.Models.Tenant? tenant)
    {
        if (tenant == null)
        {
            throw new ArgumentException("Tenant cannot be null", nameof(tenant));
        }

        // Validate required string properties
        if (string.IsNullOrWhiteSpace(tenant.Id))
        {
            throw new ArgumentException("Id is required", nameof(tenant.Id));
        }

        if (string.IsNullOrWhiteSpace(tenant.TenantId))
        {
            throw new ArgumentException("TenantId is required", nameof(tenant.TenantId));
        }

        if (string.IsNullOrWhiteSpace(tenant.Name))
        {
            throw new ArgumentException("Name is required", nameof(tenant.Name));
        }

        if (string.IsNullOrWhiteSpace(tenant.Domain))
        {
            throw new ArgumentException("Domain is required", nameof(tenant.Domain));
        }

        if (string.IsNullOrWhiteSpace(tenant.CreatedBy))
        {
            throw new ArgumentException("CreatedBy is required", nameof(tenant.CreatedBy));
        }

        // Validate dates
        if (tenant.CreatedAt <= DateTime.MinValue)
        {
            throw new ArgumentException("CreatedAt must be a valid date", nameof(tenant.CreatedAt));
        }

        // Validate individual fields
        ValidateObjectId(tenant.Id, nameof(tenant.Id));
        ValidateTenantId(tenant.TenantId, nameof(tenant.TenantId));
        ValidateTenantName(tenant.Name, nameof(tenant.Name));
        ValidateDomain(tenant.Domain, nameof(tenant.Domain));
        ValidateDescription(tenant.Description, nameof(tenant.Description));
        ValidateTheme(tenant.Theme, nameof(tenant.Theme));
        ValidateTimezone(tenant.Timezone, nameof(tenant.Timezone));

        // Validate CreatedBy
        ValidateString(tenant.CreatedBy, nameof(tenant.CreatedBy), ValidAgentPattern, 100);
    }

    /// <summary>
    /// Validates user ID
    /// </summary>
    public static void ValidateUserId(string userId, string parameterName = "userId")
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (userId.Length > 100)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 100 characters", parameterName);
        }

        // Validate user ID format (alphanumeric, hyphens, underscores)
        if (!ValidUserIdPattern.IsMatch(userId))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters.", parameterName);
        }
    }

    /// <summary>
    /// Validates revocation reason
    /// </summary>
    public static void ValidateRevocationReason(string reason, string parameterName = "reason")
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new ArgumentException($"{parameterName} cannot be null or empty", parameterName);
        }

        if (reason.Length > 500)
        {
            throw new ArgumentException($"{parameterName} cannot exceed 500 characters", parameterName);
        }

        // Validate revocation reason format (alphanumeric, spaces, hyphens, underscores, dots, @, !, ?)
        if (!ValidRevocationReasonPattern.IsMatch(reason))
        {
            throw new ArgumentException($"{parameterName} contains invalid characters. Only alphanumeric characters, spaces, hyphens, underscores, dots, @, !, and ? are allowed.", parameterName);
        }
    }


    /// <summary>
    /// Validates date range
    /// </summary>
    public static void ValidateDateRange(DateTime? startTime, DateTime? endTime, string startTimeParameterName = "startTime", string endTimeParameterName = "endTime")
    {
        if (startTime.HasValue && endTime.HasValue && startTime.Value > endTime.Value)
        {
            throw new ArgumentException($"{startTimeParameterName} ({startTime.Value}) cannot be after {endTimeParameterName} ({endTime.Value})");
        }
    }

    /// <summary>
    /// Validates an Agent object
    /// </summary>
    public static void ValidateAgent(Shared.Data.Models.Agent? agent)
    {
        if (agent == null)
        {
            throw new ArgumentException("Agent cannot be null", nameof(agent));
        }

        ValidateAgentName(agent.Name, nameof(agent.Name));
        ValidateTenantId(agent.Tenant, nameof(agent.Tenant));
        ValidateCreatedBy(agent.CreatedBy, nameof(agent.CreatedBy));
    }
}