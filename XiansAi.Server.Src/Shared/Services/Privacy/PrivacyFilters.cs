using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data.Models;

namespace Shared.Services.Privacy;

/// <summary>
/// Filter building blocks for finding a person's records. Every method returns null instead of a filter that would
/// match everything, such as one built from an empty tenant or identifier list. An empty filter matches every
/// document, so a count would report other people's records and a later erase would delete them. Callers skip the
/// query when they get null.
/// </summary>
internal static class PrivacyFilters
{
    private static FilterDefinitionBuilder<BsonDocument> Filter => Builders<BsonDocument>.Filter;

    /// <summary>
    /// Limits a query to the request's tenants. Requests covering all tenants also reach documents with no tenant,
    /// such as secrets that are not tied to a tenant.
    /// </summary>
    public static FilterDefinition<BsonDocument>? InScope(string tenantField, PrivacySubject subject, PrivacyScope scope)
    {
        if (subject.TenantIds.Count == 0)
        {
            return null;
        }

        var inTenants = Filter.In(tenantField, subject.TenantIds);
        return scope.AllTenants ? inTenants | Filter.Eq(tenantField, BsonNull.Value) : inTenants;
    }

    /// <summary>Matches documents whose <paramref name="field"/> equals any of <paramref name="values"/>.</summary>
    public static FilterDefinition<BsonDocument>? AnyOf(string field, IEnumerable<string> values)
    {
        var distinct = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return distinct.Count == 0 ? null : Filter.In(field, distinct);
    }

    /// <summary>All of the filters. Null when any is null, because a missing part would widen the match.</summary>
    public static FilterDefinition<BsonDocument>? All(params FilterDefinition<BsonDocument>?[] filters) =>
        filters.Any(filter => filter == null) ? null : Filter.And(filters!);

    /// <summary>Any of the filters that exist. Null when none exists.</summary>
    public static FilterDefinition<BsonDocument>? Either(params FilterDefinition<BsonDocument>?[] filters)
    {
        var present = filters.OfType<FilterDefinition<BsonDocument>>().ToList();
        return present.Count switch
        {
            0 => null,
            1 => present[0],
            _ => Filter.Or(present)
        };
    }

    /// <summary>
    /// Values that identify the person in fields such as <c>participant_id</c>, <c>created_by</c> and
    /// <c>user_id</c>: the participant IDs (lowercase), the user IDs and their provider subjects as stored. Fields
    /// written before identifiers were lowercased can hold either form.
    /// </summary>
    public static IReadOnlyList<string> PersonIds(PrivacySubject subject)
    {
        var ids = new HashSet<string>(subject.ParticipantIds, StringComparer.Ordinal);
        foreach (var userId in subject.UserIds)
        {
            ids.Add(userId);
            var separator = userId.IndexOf('|');
            if (separator > 0 && separator < userId.Length - 1)
            {
                ids.Add(userId[(separator + 1)..]);
            }
        }

        ids.RemoveWhere(string.IsNullOrWhiteSpace);
        return ids.Order(StringComparer.Ordinal).ToList();
    }
}
