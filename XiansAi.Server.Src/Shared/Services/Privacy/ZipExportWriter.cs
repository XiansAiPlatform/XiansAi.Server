using System.IO.Compression;
using System.Text.Json;

namespace Shared.Services.Privacy;

/// <summary>Streams contributor entries into an open ZIP archive.</summary>
public sealed class ZipExportWriter(ZipArchive archive) : IExportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly HashSet<string> paths = new(StringComparer.Ordinal);

    public async Task WriteJsonAsync(string path, object data, CancellationToken cancellationToken)
    {
        var entry = CreateEntry(path);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await JsonSerializer.SerializeAsync(stream, data, JsonOptions, cancellationToken);
    }

    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        var entry = CreateEntry(path);
        await using var stream = await entry.OpenAsync(cancellationToken);
        await content.CopyToAsync(stream, cancellationToken);
    }

    private ZipArchiveEntry CreateEntry(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\') ||
            path.Split('/').Any(part => part is "" or "." or ".." ||
                part.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_' and not '.')))
        {
            throw new ArgumentException("Invalid export path.", nameof(path));
        }

        if (!paths.Add(path))
        {
            throw new ArgumentException("Duplicate export path.", nameof(path));
        }

        return archive.CreateEntry(path, CompressionLevel.Fastest);
    }
}
