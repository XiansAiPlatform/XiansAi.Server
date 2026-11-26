using System.IO.Compression;
using Shared.Data.Models;

namespace Shared.Services.Privacy.Export;

/// <summary>Streams a person's data as a ZIP without storing it on the server.</summary>
public interface IPrivacyExporter
{
    /// <summary>
    /// Writes each contributor's data into its own folder, in <see cref="IPersonalDataContributor.Order"/>,
    /// then <c>manifest.json</c>.
    /// </summary>
    /// <remarks>
    /// If a contributor fails, the ZIP is left unfinished so it can't be opened. The caller must abort the response.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A contributor name is invalid or used twice.</exception>
    Task ExportAsync(PrivacyExportContext context, Stream output, CancellationToken cancellationToken);
}

/// <param name="RequestId">The privacy request ID, written to the manifest.</param>
/// <param name="Subject">The person, passed to every contributor.</param>
/// <param name="Scope">The request scope, passed to every contributor.</param>
public sealed record PrivacyExportContext(string RequestId, PrivacySubject Subject, PrivacyScope Scope);

/// <summary>Contents of <c>manifest.json</c>.</summary>
public sealed record PrivacyExportManifest(
    int FormatVersion,
    string RequestId,
    DateTimeOffset GeneratedAt,
    PrivacyExportManifestScope Scope,
    IReadOnlyList<PrivacyExportManifestContributor> Contributors,
    int TotalEntries);

public sealed record PrivacyExportManifestScope(bool AllTenants, IReadOnlyList<string> TenantIds, bool IncludeAccount);

/// <param name="Name">Contributor name, also its folder name.</param>
/// <param name="Order">Run order.</param>
/// <param name="Entries">Number of entries it wrote.</param>
public sealed record PrivacyExportManifestContributor(string Name, int Order, int Entries);

public sealed class PrivacyExporter : IPrivacyExporter
{
    public const int FormatVersion = 1;
    public const string ManifestPath = "manifest.json";

    private readonly IReadOnlyList<IPersonalDataContributor> _contributors;
    private readonly TimeProvider _timeProvider;

    public PrivacyExporter(IEnumerable<IPersonalDataContributor> contributors, TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(contributors);
        _contributors = contributors.ToList();
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task ExportAsync(PrivacyExportContext context, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(context.Subject);
        ArgumentNullException.ThrowIfNull(context.Scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.RequestId, nameof(context));
        ArgumentNullException.ThrowIfNull(output);

        // Check names before writing anything.
        var contributors = OrderedContributors();
        var generatedAt = _timeProvider.GetUtcNow();

        var stream = new AsyncOutputStream(output);
        var archive = await ZipArchive.CreateAsync(
            stream, ZipArchiveMode.Create, leaveOpen: true, entryNameEncoding: null, cancellationToken);
        var writer = new ZipExportWriter(archive);

        // If anything below throws, the archive is not disposed, so the partial ZIP can't be opened.
        var results = new List<PrivacyExportManifestContributor>(contributors.Count);
        foreach (var contributor in contributors)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = new ContributorExportWriter(writer, contributor.Name);
            await contributor.ExportAsync(context.Subject, context.Scope, folder, cancellationToken);
            results.Add(new PrivacyExportManifestContributor(contributor.Name, contributor.Order, folder.Entries));
        }

        cancellationToken.ThrowIfCancellationRequested();
        var manifest = new PrivacyExportManifest(
            FormatVersion,
            context.RequestId,
            generatedAt,
            new PrivacyExportManifestScope(
                context.Scope.AllTenants, context.Scope.TenantIds.ToList(), context.Scope.IncludeAccount),
            results,
            results.Sum(result => result.Entries));
        await writer.WriteJsonAsync(ManifestPath, manifest, cancellationToken);

        await archive.DisposeAsync();
        await stream.FlushAsync(cancellationToken);
    }

    private List<IPersonalDataContributor> OrderedContributors()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestPath };
        foreach (var contributor in _contributors)
        {
            if (!ExportEntryPath.IsValidSegment(contributor.Name))
            {
                throw new InvalidOperationException(
                    $"Privacy contributor name '{contributor.Name}' is not a valid export folder name.");
            }

            if (!names.Add(contributor.Name))
            {
                throw new InvalidOperationException(
                    $"Privacy contributor name '{contributor.Name}' is used twice or clashes with a reserved file.");
            }
        }

        return _contributors
            .OrderBy(contributor => contributor.Order)
            .ThenBy(contributor => contributor.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Puts a contributor's entries in its own folder and counts them.</summary>
    private sealed class ContributorExportWriter(ZipExportWriter writer, string folder) : IExportWriter
    {
        public int Entries { get; private set; }

        public async Task WriteJsonAsync(string path, object data, CancellationToken cancellationToken)
        {
            await writer.WriteJsonAsync(FullPath(path), data, cancellationToken);
            Entries++;
        }

        public async Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken)
        {
            await writer.WriteFileAsync(FullPath(path), content, cancellationToken);
            Entries++;
        }

        private string FullPath(string path)
        {
            ExportEntryPath.Validate(path);
            return $"{folder}/{path}";
        }
    }
}
