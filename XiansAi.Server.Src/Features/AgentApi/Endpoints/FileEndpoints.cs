using Microsoft.AspNetCore.Mvc;
using Features.AdminApi.Endpoints;
using Features.AgentApi.Auth;
using Features.Shared.Configuration;
using Shared.Auth;
using Shared.Services;
using Shared.Utils;

namespace Features.AgentApi.Endpoints;

public class AgentUploadFilesRequest
{
    public string? ParticipantId { get; set; }
    public required List<FileAttachment> Files { get; set; }
}

// Non-static class for logger type parameter
public class FileEndpointLogger {}

/// <summary>
/// Provides extension methods for registering message file upload/download endpoints for agents.
/// Agents receive file references (not bytes) in messages and download the content on demand.
/// Agents also upload bytes here before posting an outbound File message with references only.
/// </summary>
public static class FileEndpoints
{
    private const int MaxFiles = 5;
    private const long MaxFileSizeBytes = 10L * 1024 * 1024;
    private const long MaxTotalSizeBytes = 20L * 1024 * 1024;

    private static ILogger<FileEndpointLogger> _logger = null!;

    public static void MapFileEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<FileEndpointLogger>();

        var fileGroup = app.MapGroup("/api/agent/files")
            .WithTags("AgentAPI - Files")
            .RequiresCertificate()
            .WithAgentUserApiRateLimit();

        fileGroup.MapPost("", async (
            [FromBody] AgentUploadFilesRequest request,
            [FromServices] IMessageFileStorage fileStorage,
            [FromServices] ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Results.Unauthorized();
            }

            // Every stored file must have an owner: outbound messages may only reference files
            // belonging to the participant they are sent to.
            if (string.IsNullOrWhiteSpace(request.ParticipantId))
            {
                return Results.BadRequest("participantId is required");
            }

            var fileValidationError = ValidateFiles(request.Files);
            if (fileValidationError != null)
            {
                return Results.BadRequest(fileValidationError);
            }

            var participantId = request.ParticipantId.ToLowerInvariant();

            try
            {
                var stored = await StoreFilesAsReferencesAsync(
                    fileStorage, tenantId, participantId, request.Files, cancellationToken);
                return Results.Ok(stored);
            }
            catch (FormatException)
            {
                return Results.BadRequest("Each file must include valid base64 content");
            }
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .ProducesProblem(StatusCodes.Status401Unauthorized)
        .WithSummary("Upload message files")
        .WithDescription("Uploads one or more files to GridFS and returns fileId references. Does not create a conversation message.");

        fileGroup.MapGet("/{fileId}", async (
            string fileId,
            [FromServices] IMessageFileStorage fileStorage,
            [FromServices] ITenantContext tenantContext,
            CancellationToken cancellationToken) =>
        {
            var tenantId = tenantContext.TenantId;
            if (string.IsNullOrWhiteSpace(tenantId))
            {
                return Results.Unauthorized();
            }

            _logger.LogInformation("Agent downloading file {FileId}", LogSanitizer.Sanitize(fileId));

            var download = await fileStorage.OpenDownloadAsync(tenantId, fileId, cancellationToken);
            if (download == null)
            {
                return Results.NotFound();
            }
            return Results.File(download.Stream, download.ContentType, download.FileName);
        })
        .Produces(StatusCodes.Status200OK)
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithSummary("Download message file")
        .WithDescription("Downloads a stored message file attachment by its id (tenant-scoped).");
    }

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
    /// Validates the strongly-typed files list for agent upload.
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
}
