using System.IO.Compression;
using System.Text;
using Shared.Data.Models;

namespace Shared.Services.Privacy;

/// <summary>Streams a privacy export without storing the ZIP on the server.</summary>
public sealed class PrivacyExporter(IEnumerable<IPersonalDataContributor> contributors)
{
    public async Task ExportAsync(
        PrivacySubject subject,
        PrivacyScope scope,
        Stream output,
        PrivacyExportDisclosure disclosure,
        CancellationToken cancellationToken)
    {
        disclosure.Validate();
        using var template = typeof(PrivacyExporter).Assembly.GetManifestResourceStream(
            "XiansAi.Server.Shared.Services.Privacy.PrivacyExportReadme.txt")
            ?? throw new InvalidOperationException("The privacy export README is missing.");
        using var reader = new StreamReader(template);
        var text = await reader.ReadToEndAsync(cancellationToken);
        text = text.Replace("{{Controller}}", disclosure.Controller)
            .Replace("{{Contact}}", disclosure.Contact)
            .Replace("{{Purposes}}", disclosure.Purposes)
            .Replace("{{DataCategories}}", disclosure.DataCategories)
            .Replace("{{Recipients}}", disclosure.Recipients)
            .Replace("{{Retention}}", disclosure.Retention)
            .Replace("{{DataSources}}", disclosure.DataSources)
            .Replace("{{AutomatedDecisions}}", disclosure.AutomatedDecisions)
            .Replace("{{InternationalTransfers}}", disclosure.InternationalTransfers)
            .Replace("{{ComplaintAuthority}}", disclosure.ComplaintAuthority);
        using var readme = new MemoryStream(Encoding.UTF8.GetBytes(text));
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
