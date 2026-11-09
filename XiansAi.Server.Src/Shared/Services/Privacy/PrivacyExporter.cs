using System.IO.Compression;
using Shared.Data.Models;

namespace Shared.Services.Privacy;

/// <summary>Streams a privacy export without storing the ZIP on the server.</summary>
public sealed class PrivacyExporter(IEnumerable<IPersonalDataContributor> contributors)
{
    public async Task ExportAsync(
        PrivacySubject subject,
        PrivacyScope scope,
        Stream output,
        CancellationToken cancellationToken)
    {
        using var readme = typeof(PrivacyExporter).Assembly.GetManifestResourceStream(
            "XiansAi.Server.Shared.Services.Privacy.PrivacyExportReadme.txt")
            ?? throw new InvalidOperationException("The privacy export README is missing.");
        var archive = await ZipArchive.CreateAsync(new AsyncZipOutputStream(output), ZipArchiveMode.Create, false, null, cancellationToken);
        var writer = new ZipExportWriter(archive);

        await writer.WriteFileAsync("README.txt", readme, cancellationToken);
        foreach (var contributor in contributors.OrderBy(item => item.Order))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await contributor.ExportAsync(subject, scope, writer, cancellationToken);
        }

        await archive.DisposeAsync();
    }
}
