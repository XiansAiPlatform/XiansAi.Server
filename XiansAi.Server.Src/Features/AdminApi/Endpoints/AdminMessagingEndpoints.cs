using Microsoft.AspNetCore.Mvc;
using Features.UserApi.Services;
using Features.WebApi.Utils;
using Shared.Auth;
using Shared.Services;
using Shared.Utils.Services;
using Shared.Repositories;
using Shared.Utils;
using System.Text.Json;
using System.Text.Json.Serialization;
using Features.AdminApi.Auth;

namespace Features.AdminApi.Endpoints;

/// <summary>
/// Logger class for AdminApi messaging endpoints.
/// </summary>
public class AdminMessagingEndpointsLogger { }

/// <summary>
/// Request model for sending messages to an agent activation.
/// </summary>
public class AdminSendMessageRequest
{
    public required string AgentName { get; set; }
    public required string ActivationName { get; set; }
    /// <summary>Optional. When omitted, the agent's unique built-in conversational workflow is used.</summary>
    public string? WorkflowType { get; set; }
    public required string ParticipantId { get; set; }
    public required string Text { get; set; }
    public object? Data { get; set; }
    public string? Topic { get; set; }
    public string? RequestId { get; set; }
    public string? Hint { get; set; }
    public string? Authorization { get; set; }
    public string? Origin { get; set; }
    
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageType? Type { get; set; }
}

/// <summary>
/// A single file attachment for a File-type message.
/// </summary>
public class FileAttachment
{
    /// <summary>Base64-encoded file content (no data: prefix).</summary>
    public required string Content { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
    public long? FileSize { get; set; }
}

/// <summary>
/// Request model for sending a File-type message (one or more files, with optional chat text)
/// to an agent activation. Strongly typed to give API consumers a self-documenting schema.
/// </summary>
public class AdminSendFileMessageRequest
{
    public required string AgentName { get; set; }
    public required string ActivationName { get; set; }
    /// <summary>Optional. When omitted, the agent's unique built-in conversational workflow is used.</summary>
    public string? WorkflowType { get; set; }
    public required string ParticipantId { get; set; }
    /// <summary>Optional chat text to accompany the files.</summary>
    public string? Text { get; set; }
    public required List<FileAttachment> Files { get; set; }
    public string? Topic { get; set; }
    public string? RequestId { get; set; }
    public string? Hint { get; set; }
    public string? Authorization { get; set; }
    public string? Origin { get; set; }
}

/// <summary>
/// AdminApi endpoints for messaging and communication.
/// These are administrative operations for managing real-time messaging via Server-Sent Events.
/// All endpoints are under /api/v{version}/admin/ prefix (versioned).
/// </summary>
public static class AdminMessagingEndpoints
{
    private const int MaxFiles = 5;
    private const long MaxFileSizeBytes = 10L * 1024 * 1024;
    private const long MaxTotalSizeBytes = 20L * 1024 * 1024;

    /// <summary>
    /// Estimates the decoded byte length of a base64 string without allocating a buffer.
    /// </summary>
    private static long EstimateBase64Bytes(string base64)
    {
        if (string.IsNullOrEmpty(base64)) return 0;
        var length = base64.Length;
        var padding = base64.EndsWith("==") ? 2 : base64.EndsWith("=") ? 1 : 0;
        return (length * 3L / 4L) - padding;
    }

    /// <summary>
    /// Validates the file payload for File-type messages. The expected shape is
    /// { "files": [{ "content": base64, "fileName": string, "contentType": string }] }.
    /// Returns an error message, or null when valid.
    /// </summary>
    private static string? ValidateFilePayload(object? data)
    {
        if (data is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return "data.files is required for file uploads";
        }

        if (!element.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            return "data.files must be a non-empty array";
        }

        var fileCount = filesElement.GetArrayLength();
        if (fileCount == 0)
        {
            return "data.files must be a non-empty array";
        }
        if (fileCount > MaxFiles)
        {
            return $"A maximum of {MaxFiles} files can be sent per message";
        }

        long totalBytes = 0;
        foreach (var file in filesElement.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object)
            {
                return "Each file must be an object with content and fileName";
            }

            if (!file.TryGetProperty("content", out var contentElement)
                || contentElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(contentElement.GetString()))
            {
                return "Each file must include base64 content";
            }

            if (!file.TryGetProperty("fileName", out var fileNameElement)
                || fileNameElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(fileNameElement.GetString()))
            {
                return "Each file must include a fileName";
            }

            var bytes = EstimateBase64Bytes(contentElement.GetString()!);
            if (bytes > MaxFileSizeBytes)
            {
                return $"File \"{fileNameElement.GetString()}\" exceeds the 10MB per-file limit";
            }
            totalBytes += bytes;
        }

        if (totalBytes > MaxTotalSizeBytes)
        {
            return "Combined attachments exceed the 20MB per-message limit";
        }

        return null;
    }

    /// <summary>
    /// Validates the strongly-typed files list for the specialized file-message endpoint.
    /// Returns an error message, or null when valid.
    /// </summary>
    private static string? ValidateFiles(List<FileAttachment>? files)
    {
        if (files == null || files.Count == 0)
        {
            return "files must be a non-empty array";
        }
        if (files.Count > MaxFiles)
        {
            return $"A maximum of {MaxFiles} files can be sent per message";
        }

        long totalBytes = 0;
        foreach (var file in files)
        {
            if (string.IsNullOrEmpty(file.Content))
            {
                return "Each file must include base64 content";
            }
            if (string.IsNullOrEmpty(file.FileName))
            {
                return "Each file must include a fileName";
            }

            var bytes = EstimateBase64Bytes(file.Content);
            if (bytes > MaxFileSizeBytes)
            {
                return $"File \"{file.FileName}\" exceeds the 10MB per-file limit";
            }
            totalBytes += bytes;
        }

        if (totalBytes > MaxTotalSizeBytes)
        {
            return "Combined attachments exceed the 20MB per-message limit";
        }

        return null;
    }

    /// <summary>
    /// Validates an outbound File message: references only (fileId + metadata, no inline content).
    /// Returns an error message, or null when valid.
    /// </summary>
    private static string? ValidateOutboundFileData(object? data)
    {
        if (data is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return "data.files is required for file messages";
        }

        if (!element.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            return "data.files must be a non-empty array";
        }

        var fileCount = filesElement.GetArrayLength();
        if (fileCount == 0)
        {
            return "data.files must be a non-empty array";
        }
        if (fileCount > MaxFiles)
        {
            return $"A maximum of {MaxFiles} files can be sent per message";
        }

        foreach (var file in filesElement.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.Object)
            {
                return "Each file must be an object with fileId and fileName";
            }

            if (file.TryGetProperty("content", out var contentElement)
                && contentElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrEmpty(contentElement.GetString()))
            {
                return "File messages must carry fileId references only; do not include content";
            }

            if (!file.TryGetProperty("fileId", out var fileIdElement)
                || fileIdElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(fileIdElement.GetString()))
            {
                return "Each file must include a fileId";
            }

            if (!file.TryGetProperty("fileName", out var fileNameElement)
                || fileNameElement.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(fileNameElement.GetString()))
            {
                return "Each file must include a fileName";
            }
        }

        return null;
    }

    /// <summary>
    /// Uploads the strongly-typed files to GridFS and returns lightweight references
    /// (fileId + metadata, no bytes) suitable for persisting and signalling.
    /// </summary>
    private static async Task<object> StoreFilesAsReferencesAsync(
        IMessageFileStorage fileStorage,
        string tenantId,
        string participantId,
        IEnumerable<FileAttachment> files,
        CancellationToken cancellationToken)
    {
        var refs = new List<StoredFileRef>();
        foreach (var file in files)
        {
            var bytes = Convert.FromBase64String(file.Content);
            var stored = await fileStorage.UploadAsync(
                tenantId, participantId, file.FileName, file.ContentType, bytes, cancellationToken);
            refs.Add(stored);
        }
        return new { files = refs };
    }

    /// <summary>
    /// Parses the generic /send File payload (untyped JsonElement) into typed file attachments.
    /// Assumes the payload already passed <see cref="ValidateFilePayload"/>.
    /// </summary>
    private static List<FileAttachment> ExtractFileAttachments(object? data)
    {
        var result = new List<FileAttachment>();
        if (data is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return result;
        }
        if (!element.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array)
        {
            return result;
        }
        foreach (var file in filesElement.EnumerateArray())
        {
            var content = file.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() : null;
            var fileName = file.TryGetProperty("fileName", out var fn) && fn.ValueKind == JsonValueKind.String ? fn.GetString() : null;
            var contentType = file.TryGetProperty("contentType", out var ct) && ct.ValueKind == JsonValueKind.String ? ct.GetString() : null;
            long? fileSize = file.TryGetProperty("fileSize", out var fs) && fs.ValueKind == JsonValueKind.Number ? fs.GetInt64() : null;
            if (string.IsNullOrEmpty(content)) continue;
            result.Add(new FileAttachment
            {
                Content = content,
                FileName = fileName ?? "uploaded-file",
                ContentType = string.IsNullOrEmpty(contentType) ? "application/octet-stream" : contentType,
                FileSize = fileSize,
            });
        }
        return result;
    }

    private static void SetAuthorizationFromHeader(ChatOrDataRequest request, HttpContext context)
    {
        if (request.Authorization == null)
        {
            var authHeader = context.Request.Headers["Authorization"].ToString();
            var (success, token) = AuthorizationHeaderHelper.ExtractBearerToken(authHeader);
            
            if (success && token != null)
            {
                request.Authorization = token;
            }
        }
    }

    /// <summary>
    /// Maps all AdminApi messaging endpoints.
    /// </summary>
    public static void MapAdminMessagingEndpoints(this RouteGroupBuilder adminApiGroup)
    {
        var adminMessagingGroup = adminApiGroup.MapGroup("/tenants/{tenantId}/messaging")
            .WithTags("AdminAPI - Messaging")
            .RequireAuthorization("AdminEndpointAuthPolicy")
            .AddEndpointFilter<TenantRouteScopeFilter>();

        // Server-Sent Events endpoint for real-time message streaming
        adminMessagingGroup.MapGet("/listen", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageEventPublisher messageEventPublisher,
            [FromServices] IConversationRepository conversationRepository,
            [FromServices] ITenantContext tenantContext,
            [FromServices] ILoggerFactory loggerFactory,
            HttpContext context,
            CancellationToken cancellationToken,
            [FromQuery] string? workflowType = null,
            [FromQuery] int heartbeatSeconds = 60) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            var resolved = await ResolveConversationalActivationAsync(
                activationValidationService, tenantId, agentName, activationName, workflowType);
            if (!resolved.IsSuccess)
            {
                return resolved.ToHttpResult();
            }
            var effectiveWorkflowType = resolved.Data!;
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();

            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            // Create or get the thread
            var thread = new ConversationThread
            {
                TenantId = tenantId,
                WorkflowId = workflowId,
                Agent = agentName,
                ParticipantId = participantId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = tenantContext.LoggedInUser,
                Status = ConversationThreadStatus.Active
            };

            var threadId = await conversationRepository.CreateOrGetThreadIdAsync(thread);

            // Create logger from factory
            var logger = loggerFactory.CreateLogger<AdminMessagingEndpointsLogger>();

            // Use the SSE stream handler to manage the entire connection lifecycle
            var streamHandler = new WebApiSSEStreamHandler(
                messageEventPublisher, 
                logger, 
                context, 
                threadId, 
                tenantContext, 
                cancellationToken,
                TimeSpan.FromSeconds(Math.Max(1, Math.Min(heartbeatSeconds, 300)))); // Between 1-300 seconds

            return await streamHandler.HandleStreamAsync();
        })
        .WithName("ListenToAgentActivationMessagesForAdminApi")
        ;

        // Unified send message endpoint
        adminMessagingGroup.MapPost("/send", async (
            string tenantId,
            [FromBody] AdminSendMessageRequest request,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromServices] IMessageFileStorage fileStorage,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveConversationalActivationAsync(
                activationValidationService, tenantId, request.AgentName, request.ActivationName, request.WorkflowType);
            if (!resolved.IsSuccess)
            {
                return resolved.ToHttpResult();
            }
            var effectiveWorkflowType = resolved.Data!;
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, request.AgentName, effectiveWorkflowType, request.ActivationName);
            
            // Default to Chat if type not specified
            var messageType = request.Type ?? MessageType.Chat;
            
            // Normalize participantId to lowercase (typically an email)
            var normalizedParticipantId = request.ParticipantId.ToLowerInvariant();

            // For File messages, validate the base64 payload then offload bytes to GridFS,
            // keeping only references in the message data / signal (Temporal payloads are ~2MB capped).
            var messageData = request.Data;
            if (messageType == MessageType.File)
            {
                var fileValidationError = ValidateFilePayload(request.Data);
                if (fileValidationError != null)
                {
                    return Results.BadRequest(fileValidationError);
                }
                var attachments = ExtractFileAttachments(request.Data);
                messageData = await StoreFilesAsReferencesAsync(
                    fileStorage, tenantId, normalizedParticipantId, attachments, cancellationToken);
            }
            
            // Map to ChatOrDataRequest
            var chatOrDataRequest = new ChatOrDataRequest
            {
                WorkflowId = workflowId,
                ParticipantId = normalizedParticipantId,
                Text = request.Text,
                Data = messageData,
                Scope = request.Topic,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Hint = request.Hint,
                Authorization = request.Authorization,
                Origin = request.Origin,
                Type = messageType
            };
            
            // Use authorization from header if not in body
            SetAuthorizationFromHeader(chatOrDataRequest, context);
            
            var result = await messageService.ProcessIncomingMessage(chatOrDataRequest, messageType);
            return result.ToHttpResult();
        })
        .WithName("SendMessageForAdminApi")
        ;

        // Specialized file-message endpoint (typed JSON). Convenience wrapper over /send for
        // File-type messages: strongly-typed files[] schema for API consumers, same pipeline.
        adminMessagingGroup.MapPost("/send/file", async (
            string tenantId,
            [FromBody] AdminSendFileMessageRequest request,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromServices] IMessageFileStorage fileStorage,
            HttpContext context,
            CancellationToken cancellationToken) =>
        {
            // Validate the file payload (count and size limits)
            var fileValidationError = ValidateFiles(request.Files);
            if (fileValidationError != null)
            {
                return Results.BadRequest(fileValidationError);
            }

            var resolved = await ResolveConversationalActivationAsync(
                activationValidationService, tenantId, request.AgentName, request.ActivationName, request.WorkflowType);
            if (!resolved.IsSuccess)
            {
                return resolved.ToHttpResult();
            }
            var effectiveWorkflowType = resolved.Data!;
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, request.AgentName, effectiveWorkflowType, request.ActivationName);

            // Normalize participantId to lowercase (typically an email)
            var normalizedParticipantId = request.ParticipantId.ToLowerInvariant();

            // Offload file bytes to GridFS; persist and signal only references.
            var messageData = await StoreFilesAsReferencesAsync(
                fileStorage, tenantId, normalizedParticipantId, request.Files, cancellationToken);

            // Map to ChatOrDataRequest using the canonical data.files[] reference shape
            var chatOrDataRequest = new ChatOrDataRequest
            {
                WorkflowId = workflowId,
                ParticipantId = normalizedParticipantId,
                Text = request.Text ?? string.Empty,
                Data = messageData,
                Scope = request.Topic,
                RequestId = request.RequestId ?? Guid.NewGuid().ToString(),
                Hint = request.Hint,
                Authorization = request.Authorization,
                Origin = request.Origin,
                Type = MessageType.File
            };

            // Use authorization from header if not in body
            SetAuthorizationFromHeader(chatOrDataRequest, context);

            var result = await messageService.ProcessIncomingMessage(chatOrDataRequest, MessageType.File);
            return result.ToHttpResult();
        })
        .WithName("SendFileMessageForAdminApi")
        ;

        // Download a stored message file attachment by its GridFS id (tenant-scoped).
        adminMessagingGroup.MapGet("/files/{fileId}", async (
            string tenantId,
            string fileId,
            [FromServices] IMessageFileStorage fileStorage,
            CancellationToken cancellationToken) =>
        {
            var download = await fileStorage.OpenDownloadAsync(tenantId, fileId, cancellationToken);
            if (download == null)
            {
                return Results.NotFound();
            }
            return Results.File(download.Stream, download.ContentType, download.FileName);
        })
        .WithName("DownloadMessageFileForAdminApi")
        ;

        // Get topics (distinct scopes) for a specific agent activation and participant
        adminMessagingGroup.MapGet("/topics", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromQuery] string? workflowType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            var resolved = await ResolveConversationalActivationAsync(
                activationValidationService, tenantId, agentName, activationName, workflowType);
            if (!resolved.IsSuccess)
            {
                return resolved.ToHttpResult();
            }
            var effectiveWorkflowType = resolved.Data!;
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            var result = await messageService.GetTopicsByWorkflowAndParticipantAsync(workflowId, participantId, page, pageSize);
            return result.ToHttpResult();
        })
        .WithName("GetTopicsForAdminApi")
        ;

        // Get message history for a specific agent activation and participant
        adminMessagingGroup.MapGet("/history", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromQuery] string? workflowType = null,
            [FromQuery] string? topic = null,
            [FromQuery] string sortOrder = "desc",
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] bool chatOnly = false) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            var resolved = await ResolveConversationalActivationAsync(
                activationValidationService, tenantId, agentName, activationName, workflowType);
            if (!resolved.IsSuccess)
            {
                return resolved.ToHttpResult();
            }
            var effectiveWorkflowType = resolved.Data!;
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();

            // Validate sort order
            var normalizedSortOrder = sortOrder.ToLowerInvariant();
            if (normalizedSortOrder != "asc" && normalizedSortOrder != "desc")
            {
                return Results.BadRequest("sortOrder must be either 'asc' or 'desc'");
            }
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            // When topic is not provided (null) or empty, get messages with null scope (no topic)
            // When topic has a specific value, get messages with that specific scope
            var result = await messageService.GetThreadHistoryAsync(workflowId, participantId, page, pageSize, topic, chatOnly, normalizedSortOrder);
            return result.ToHttpResult();
        })
        .WithName("GetHistoryForAdminApi")
        ;

        // Delete messages by topic for a specific agent activation and participant
        adminMessagingGroup.MapDelete("/messages", async (
            string tenantId,
            [FromQuery] string agentName,
            [FromQuery] string activationName,
            [FromQuery] string participantId,
            [FromServices] IActivationValidationService activationValidationService,
            [FromServices] IMessageService messageService,
            [FromQuery] string? workflowType = null,
            [FromQuery] string? topic = null) =>
        {
            // Validate required parameters
            if (string.IsNullOrWhiteSpace(agentName))
            {
                return Results.BadRequest("agentName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(activationName))
            {
                return Results.BadRequest("activationName query parameter is required");
            }

            if (string.IsNullOrWhiteSpace(participantId))
            {
                return Results.BadRequest("participantId query parameter is required");
            }

            var resolved = await ResolveConversationalActivationAsync(
                activationValidationService, tenantId, agentName, activationName, workflowType);
            if (!resolved.IsSuccess)
            {
                return resolved.ToHttpResult();
            }
            var effectiveWorkflowType = resolved.Data!;
            
            // Normalize participantId to lowercase (typically an email)
            participantId = participantId.ToLowerInvariant();
            
            // Normalize topic: empty string should be treated as null
            var normalizedTopic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
            var workflowId = WorkflowIdentifier.BuildWorkflowId(tenantId, agentName, effectiveWorkflowType, activationName);

            // Delete messages with the specified topic/scope
            // When topic is null or empty, delete messages with null scope (no topic)
            // When topic has a value, delete messages with that specific scope
            var result = await messageService.DeleteMessagesByTopicAsync(workflowId, participantId, normalizedTopic);
            return result.ToHttpResult();
        })
        .WithName("DeleteMessagesByTopicForAdminApi")
        ;
    }

    /// <summary>
    /// Resolves the conversational flow from <c>IsBuiltIn</c> and confirms the activation is usable.
    /// </summary>
    private static async Task<ServiceResult<string>> ResolveConversationalActivationAsync(
        IActivationValidationService activationValidationService,
        string tenantId,
        string agentName,
        string activationName,
        string? workflowType)
    {
        var resolved = await activationValidationService.ResolveConversationalWorkflowAsync(
            tenantId, agentName, workflowType);
        if (!resolved.IsSuccess)
            return resolved;

        var validationResult = await activationValidationService.ValidateActivationAsync(
            tenantId, agentName, activationName, resolved.Data);
        if (!validationResult.IsSuccess)
        {
            return ServiceResult<string>.Failure(
                validationResult.ErrorMessage ?? "Activation validation failed",
                validationResult.StatusCode);
        }

        return ServiceResult<string>.Success(resolved.Data!);
    }
}

