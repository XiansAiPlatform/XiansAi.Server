using Shared.Data.Models;

namespace Shared.Services.Privacy;

/// <summary>
/// One place that holds personal data, such as a group of MongoDB collections. Every registered source is asked by
/// <see cref="IPersonalDataDiscovery"/>, so adding a source means registering a new implementation and nothing else.
/// </summary>
public interface IPersonalDataSource
{
    /// <summary>Stable name of the data area, such as <c>conversations</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Counts the person's records within the scope without changing anything. Returns one row for every collection
    /// the source covers, including collections with no records, so discovery lists every place it checked.
    /// </summary>
    Task<IReadOnlyList<PersonalDataCount>> CountAsync(PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken);
}

/// <param name="Collection">The data store, such as a MongoDB collection name.</param>
/// <param name="Records">The person's records in it within the request's scope.</param>
public sealed record PersonalDataCount(string Collection, long Records);
