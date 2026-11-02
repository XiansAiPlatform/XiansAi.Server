using Shared.Data.Models;

namespace Shared.Services.Privacy;

/// <summary>
/// Exports and erases one group of a person's data, such as conversations or the account.
/// Implementations are registered as <see cref="IPersonalDataContributor"/> and run in <see cref="Order"/>.
/// </summary>
public interface IPersonalDataContributor
{
    /// <summary>Stable name, stored in each request's results. Never rename a shipped contributor.</summary>
    string Name { get; }

    /// <summary>Lower runs first. The account contributor runs last, so the account exists while the others act.</summary>
    int Order { get; }

    Task ExportAsync(PrivacySubject subject, PrivacyScope scope, IExportWriter writer, CancellationToken cancellationToken);

    /// <summary>
    /// Erases or anonymises the data. Must be idempotent, because a request is retried after a partial failure.
    /// Throws to report failure. Only a <see cref="PrivacyRequestException"/> message is stored on the request.
    /// </summary>
    Task<ContributorErasureResult> EraseAsync(PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken);
}

/// <param name="ItemsAffected">Documents or records deleted, anonymised or revoked.</param>
/// <param name="Notes">Reports for operators. Must not contain the person's identifiers.</param>
public sealed record ContributorErasureResult(long ItemsAffected, IReadOnlyList<string> Notes)
{
    public static ContributorErasureResult Of(long itemsAffected) => new(itemsAffected, []);
}

/// <summary>Receives the entries of an export as they are produced, so nothing is held in memory or stored.</summary>
public interface IExportWriter
{
    /// <summary>Writes <paramref name="data"/> as a JSON entry at <paramref name="path"/> within the export.</summary>
    Task WriteJsonAsync(string path, object data, CancellationToken cancellationToken);

    /// <summary>Copies a binary entry, such as an uploaded file, to <paramref name="path"/> within the export.</summary>
    Task WriteFileAsync(string path, Stream content, CancellationToken cancellationToken);
}
