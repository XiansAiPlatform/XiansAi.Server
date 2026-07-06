using Microsoft.AspNetCore.Mvc;
using Features.AgentApi.Auth;
using Shared.Auth;
using Shared.Services;
using Shared.Utils;

namespace Features.AgentApi.Endpoints;

// Non-static class for logger type parameter
public class FileEndpointLogger {}

/// <summary>
/// Provides extension methods for registering message file download endpoints for agents.
/// Agents receive file references (not bytes) in messages and download the content on demand.
/// </summary>
public static class FileEndpoints
{
    private static ILogger<FileEndpointLogger> _logger = null!;

    public static void MapFileEndpoints(this WebApplication app, ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<FileEndpointLogger>();

        var fileGroup = app.MapGroup("/api/agent/files")
            .WithTags("AgentAPI - Files")
            .RequiresCertificate();

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
}
