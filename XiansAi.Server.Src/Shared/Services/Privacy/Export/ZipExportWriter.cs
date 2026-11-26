using System.IO.Compression;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Bson.IO;

namespace Shared.Services.Privacy.Export;

/// <summary>
/// Streams export entries into a ZIP archive opened in Create mode.
/// </summary>
/// <remarks>
/// MongoDB documents are written as relaxed extended JSON; other objects use System.Text.Json. Paths must follow
/// <see cref="ExportEntryPath"/>, and duplicate paths are rejected. The caller owns the archive.
/// </remarks>
public sealed class ZipExportWriter : IExportWriter
{
    private const int CopyBufferSize = 81920;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly JsonWriterSettings BsonJsonSettings = new()
    {
        OutputMode = JsonOutputMode.RelaxedExtendedJson,
        Indent = true
    };

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ZipArchive _archive;
    private readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

    public ZipExportWriter(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);
        if (archive.Mode != ZipArchiveMode.Create)
        {
            throw new ArgumentException("The archive must be opened in Create mode.", nameof(archive));
        }

        _archive = archive;
    }

    /// <summary>Entries written so far.</summary>
    public int EntryCount => _paths.Count;

    /// <exception cref="ArgumentException">The path is invalid or already written.</exception>
    public async Task WriteJsonAsync(string path, object data, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(data);
        await using var entry = await OpenEntryAsync(path, CompressionLevel.Optimal, cancellationToken);

        switch (data)
        {
            case BsonValue bson:
                await WriteBsonAsync(entry, bson, cancellationToken);
                break;
            case IEnumerable<BsonDocument> documents:
                await WriteBsonArrayAsync(entry, documents, cancellationToken);
                break;
            default:
                await JsonSerializer.SerializeAsync(entry, data, data.GetType(), JsonOptions, cancellationToken);
                break;
        }
    }

    /// <exception cref="ArgumentException">The path is invalid or already written.</exception>
    public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        await using var entry = await OpenEntryAsync(path, CompressionLevel.Fastest, cancellationToken);
        await content.CopyToAsync(entry, CopyBufferSize, cancellationToken);
    }

    private async Task<Stream> OpenEntryAsync(string path, CompressionLevel compression, CancellationToken cancellationToken)
    {
        ExportEntryPath.Validate(path);
        if (!_paths.Add(path))
        {
            throw new ArgumentException("Invalid export entry path: the path was already written.", nameof(path));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var entry = _archive.CreateEntry(path, compression);
        return await entry.OpenAsync(cancellationToken);
    }

    private static async Task WriteBsonAsync(Stream entry, BsonValue value, CancellationToken cancellationToken)
    {
        await using var text = new StreamWriter(entry, Utf8NoBom, CopyBufferSize, leaveOpen: true);
        await text.WriteAsync(value.ToJson(BsonJsonSettings).AsMemory(), cancellationToken);
        await text.FlushAsync(cancellationToken);
    }

    // One document at a time, so a long list is never built into one string.
    private static async Task WriteBsonArrayAsync(
        Stream entry, IEnumerable<BsonDocument> documents, CancellationToken cancellationToken)
    {
        await using var text = new StreamWriter(entry, Utf8NoBom, CopyBufferSize, leaveOpen: true);
        await text.WriteAsync("[".AsMemory(), cancellationToken);

        var first = true;
        foreach (var document in documents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await text.WriteAsync((first ? "\n" : ",\n").AsMemory(), cancellationToken);
            await text.WriteAsync(document.ToJson(BsonJsonSettings).AsMemory(), cancellationToken);
            first = false;
        }

        await text.WriteAsync((first ? "]" : "\n]").AsMemory(), cancellationToken);
        await text.FlushAsync(cancellationToken);
    }
}
