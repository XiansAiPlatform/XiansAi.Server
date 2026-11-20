using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
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
            "XiansAi.Server.Shared.Services.Privacy.Resources.PrivacyExportReadme.txt")
            ?? throw new InvalidOperationException("The privacy export README is missing.");
        using var reader = new StreamReader(template);
        var text = await reader.ReadToEndAsync(cancellationToken);
        text = Regex.Replace(text, @"\{\{([A-Za-z]+)\}\}", match => match.Groups[1].Value switch
        {
            "Controller" => disclosure.Controller,
            "Contact" => disclosure.Contact,
            "Purposes" => disclosure.Purposes,
            "DataCategories" => disclosure.DataCategories,
            "Recipients" => disclosure.Recipients,
            "Retention" => disclosure.Retention,
            "DataSources" => disclosure.DataSources,
            "AutomatedDecisions" => disclosure.AutomatedDecisions,
            "InternationalTransfers" => disclosure.InternationalTransfers,
            "ComplaintAuthority" => disclosure.ComplaintAuthority,
            _ => throw new InvalidOperationException($"Unknown privacy export placeholder: {match.Value}")
        });
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
