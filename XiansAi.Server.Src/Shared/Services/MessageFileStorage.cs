using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;
using MongoDB.Driver.GridFS;
using Shared.Data;
using Shared.Utils;

namespace Shared.Services;

/// <summary>
/// A lightweight reference to a stored file, safe to persist in a message and
/// forward in a Temporal signal (contains no file bytes).
/// Element/property names are pinned to camelCase so the stored BSON, the API/SSE
/// JSON, and the Temporal signal payload all use the same field names.
/// </summary>
public class StoredFileRef
{
    [BsonElement("fileId")]
    [JsonPropertyName("fileId")]
    public required string FileId { get; set; }

    [BsonElement("fileName")]
    [JsonPropertyName("fileName")]
    public required string FileName { get; set; }

    [BsonElement("contentType")]
    [JsonPropertyName("contentType")]
    public required string ContentType { get; set; }

    [BsonElement("fileSize")]
    [JsonPropertyName("fileSize")]
    public long FileSize { get; set; }
}

/// <summary>
/// The bytes and metadata for a downloaded file.
/// </summary>
public class DownloadedFile
{
    public required Stream Stream { get; set; }
    public required string FileName { get; set; }
    public required string ContentType { get; set; }
}

/// <summary>
/// Stores and retrieves message file attachments in MongoDB GridFS.
/// Keeps large binary payloads out of the Temporal signal (which caps a single
/// payload blob at ~2MB) and out of the message document.
/// </summary>
public interface IMessageFileStorage
{
    /// <summary>Uploads a file to GridFS and returns a reference (no bytes).</summary>
    Task<StoredFileRef> UploadAsync(
        string tenantId,
        string participantId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a stored file for download. Returns null if the file does not exist
    /// or does not belong to the given tenant.
    /// </summary>
    Task<DownloadedFile?> OpenDownloadAsync(
        string tenantId,
        string fileId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a stored file into its authoritative reference. Returns null if the file does not
    /// exist, belongs to another tenant, or is not owned by the given participant. Use this before
    /// attaching an agent-supplied fileId to a message, so one participant's file cannot be
    /// delivered to another.
    /// </summary>
    Task<StoredFileRef?> ResolveReferenceAsync(
        string tenantId,
        string participantId,
        string fileId,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes stored files by id. Missing files and other tenants' files are ignored.</summary>
    Task DeleteAsync(
        string tenantId,
        IEnumerable<string> fileIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one batch of files whose <c>metadata.expires_at</c> is at or before <paramref name="asOfUtc"/>.
    /// Both the file document and its chunks are removed (a native GridFS TTL index cannot do this, as it
    /// would orphan the chunk documents). Returns the number of files deleted in this batch.
    /// </summary>
    Task<int> DeleteExpiredAsync(
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken = default);
}

public class MessageFileStorage : IMessageFileStorage
{
    private const string BucketName = "message_files";
    private const string DefaultContentType = "application/octet-stream";

    /// <summary>
    /// How long a stored file is retained before the background sweeper removes it. Mirrors the
    /// 180-day TTL on the conversation_message documents that reference these files, so a file and
    /// the message pointing at it expire together.
    /// </summary>
    private static readonly TimeSpan FileRetention = TimeSpan.FromDays(180);

    private readonly ILogger<MessageFileStorage> _logger;
    private readonly IGridFSBucket _bucket;

    public MessageFileStorage(IMongoDbClientService mongoDbClientService, ILogger<MessageFileStorage> logger)
    {
        _logger = logger;
        var database = mongoDbClientService.GetDatabase();
        _bucket = new GridFSBucket(database, new GridFSBucketOptions { BucketName = BucketName });
    }

    public async Task<StoredFileRef> UploadAsync(
        string tenantId,
        string participantId,
        string fileName,
        string contentType,
        byte[] content,
        CancellationToken cancellationToken = default)
    {
        var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "uploaded-file" : fileName;
        var safeContentType = string.IsNullOrWhiteSpace(contentType) ? DefaultContentType : contentType;

        var metadata = new BsonDocument
        {
            { "tenant_id", tenantId },
            { "participant_id", participantId ?? string.Empty },
            { "content_type", safeContentType },
            { "file_name", safeFileName },
            // Drives the background cleanup sweep (see DeleteExpiredAsync). Stored as BSON UTC datetime.
            { "expires_at", DateTime.UtcNow.Add(FileRetention) },
        };

        var uploadOptions = new GridFSUploadOptions { Metadata = metadata };

        var fileId = await _bucket.UploadFromBytesAsync(safeFileName, content, uploadOptions, cancellationToken);

        _logger.LogInformation(
            "Stored file {FileId} ({FileSize} bytes) for tenant {TenantId}",
            fileId, content.Length, LogSanitizer.Sanitize(tenantId));

        return new StoredFileRef
        {
            FileId = fileId.ToString(),
            FileName = safeFileName,
            ContentType = safeContentType,
            FileSize = content.Length,
        };
    }

    public async Task<DownloadedFile?> OpenDownloadAsync(
        string tenantId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = await FindFileForTenantAsync(tenantId, fileId, cancellationToken);
        if (fileInfo == null)
        {
            return null;
        }

        var stream = await _bucket.OpenDownloadStreamAsync(fileInfo.Id, cancellationToken: cancellationToken);

        return new DownloadedFile
        {
            Stream = stream,
            FileName = GetFileName(fileInfo),
            ContentType = GetContentType(fileInfo),
        };
    }

    public async Task<StoredFileRef?> ResolveReferenceAsync(
        string tenantId,
        string participantId,
        string fileId,
        CancellationToken cancellationToken = default)
    {
        var fileInfo = await FindFileForTenantAsync(tenantId, fileId, cancellationToken);
        if (fileInfo == null)
        {
            return null;
        }

        // Participant ownership: a file may only be attached to a message for the participant it
        // was stored against. Files stored without an owner are never attachable.
        var storedParticipantId = fileInfo.Metadata?.GetValue("participant_id", BsonNull.Value)?.AsString;
        if (string.IsNullOrEmpty(storedParticipantId) ||
            !string.Equals(storedParticipantId, participantId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Rejected file reference {FileId} for tenant {TenantId}: file is not owned by the target participant",
                LogSanitizer.Sanitize(fileId), LogSanitizer.Sanitize(tenantId));
            return null;
        }

        return new StoredFileRef
        {
            FileId = fileId,
            FileName = GetFileName(fileInfo),
            ContentType = GetContentType(fileInfo),
            FileSize = fileInfo.Length,
        };
    }

    /// <summary>
    /// Loads the GridFS file document, enforcing tenant isolation. Returns null when the id is
    /// malformed, the file is missing, or it belongs to another tenant.
    /// </summary>
    private async Task<GridFSFileInfo?> FindFileForTenantAsync(
        string tenantId,
        string fileId,
        CancellationToken cancellationToken)
    {
        if (!ObjectId.TryParse(fileId, out var objectId))
        {
            return null;
        }

        var filter = Builders<GridFSFileInfo>.Filter.Eq(f => f.Id, objectId);
        using var cursor = await _bucket.FindAsync(filter, cancellationToken: cancellationToken);
        var fileInfo = await cursor.FirstOrDefaultAsync(cancellationToken);

        if (fileInfo == null)
        {
            return null;
        }

        // Tenant isolation: the stored metadata tenant must match the caller's tenant.
        var storedTenantId = fileInfo.Metadata?.GetValue("tenant_id", BsonNull.Value)?.AsString;
        if (!string.Equals(storedTenantId, tenantId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Rejected cross-tenant file access: file {FileId} belongs to a different tenant than {TenantId}",
                LogSanitizer.Sanitize(fileId), LogSanitizer.Sanitize(tenantId));
            return null;
        }

        return fileInfo;
    }

    private static string GetFileName(GridFSFileInfo fileInfo) =>
        fileInfo.Metadata?.GetValue("file_name", fileInfo.Filename)?.AsString ?? fileInfo.Filename;

    private static string GetContentType(GridFSFileInfo fileInfo) =>
        fileInfo.Metadata?.GetValue("content_type", DefaultContentType)?.AsString ?? DefaultContentType;

    public async Task DeleteAsync(
        string tenantId,
        IEnumerable<string> fileIds,
        CancellationToken cancellationToken = default)
    {
        foreach (var fileId in fileIds)
        {
            if (!ObjectId.TryParse(fileId, out var objectId))
            {
                continue;
            }

            try
            {
                // Verify tenant ownership before deleting.
                var filter = Builders<GridFSFileInfo>.Filter.Eq(f => f.Id, objectId);
                using var cursor = await _bucket.FindAsync(filter, cancellationToken: cancellationToken);
                var fileInfo = await cursor.FirstOrDefaultAsync(cancellationToken);
                if (fileInfo == null)
                {
                    continue;
                }

                var storedTenantId = fileInfo.Metadata?.GetValue("tenant_id", BsonNull.Value)?.AsString;
                if (!string.Equals(storedTenantId, tenantId, StringComparison.Ordinal))
                {
                    continue;
                }

                await _bucket.DeleteAsync(objectId, cancellationToken);
            }
            catch (GridFSFileNotFoundException)
            {
                // Already gone; ignore.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete stored file {FileId} for tenant {TenantId}", fileId, tenantId);
            }
        }
    }

    public async Task<int> DeleteExpiredAsync(
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken = default)
    {
        // Only files uploaded after expires_at was introduced carry the field; documents missing it
        // are not matched by this comparison and are simply left untouched.
        var filter = Builders<GridFSFileInfo>.Filter.Lte("metadata.expires_at", asOfUtc);
        var options = new GridFSFindOptions
        {
            Limit = limit,
            Sort = Builders<GridFSFileInfo>.Sort.Ascending("metadata.expires_at"),
        };

        using var cursor = await _bucket.FindAsync(filter, options, cancellationToken);
        var expired = await cursor.ToListAsync(cancellationToken);

        var deleted = 0;
        foreach (var fileInfo in expired)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                // DeleteAsync removes both the file document and its chunks.
                await _bucket.DeleteAsync(fileInfo.Id, cancellationToken);
                deleted++;
            }
            catch (GridFSFileNotFoundException)
            {
                // Already gone (e.g. deleted alongside its message); ignore.
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete expired file {FileId}", fileInfo.Id);
            }
        }

        return deleted;
    }
}
