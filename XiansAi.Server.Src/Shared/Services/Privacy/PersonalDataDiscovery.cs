using Shared.Data.Models;

namespace Shared.Services.Privacy;

/// <summary>Lists every data source that holds a person's data, with record counts, within a request's scope.</summary>
public interface IPersonalDataDiscovery
{
    Task<IReadOnlyList<DiscoveredPersonalData>> DiscoverAsync(
        PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken);
}

/// <param name="Source">The <see cref="IPersonalDataSource.Name"/> that covers the collection.</param>
/// <param name="Collection">The data store, such as a MongoDB collection name.</param>
/// <param name="Records">The person's records in it within the request's scope.</param>
public sealed record DiscoveredPersonalData(string Source, string Collection, long Records);

/// <summary>
/// Asks every registered <see cref="IPersonalDataSource"/> for its counts, in registration order. The registered
/// sources are the registry, so adding a source means registering it and nothing here changes.
/// </summary>
public sealed class PersonalDataDiscovery : IPersonalDataDiscovery
{
    private readonly IReadOnlyList<IPersonalDataSource> _sources;

    public PersonalDataDiscovery(IEnumerable<IPersonalDataSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources.ToList();
    }

    public async Task<IReadOnlyList<DiscoveredPersonalData>> DiscoverAsync(
        PrivacySubject subject, PrivacyScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(scope);

        var results = new List<DiscoveredPersonalData>();
        foreach (var source in _sources)
        {
            var counts = await source.CountAsync(subject, scope, cancellationToken);
            results.AddRange(counts.Select(count => new DiscoveredPersonalData(source.Name, count.Collection, count.Records)));
        }

        return results;
    }
}
