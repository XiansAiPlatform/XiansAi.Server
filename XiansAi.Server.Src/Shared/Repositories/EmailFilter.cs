using System.Linq.Expressions;
using System.Text.RegularExpressions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Shared.Repositories;

/// <summary>
/// Filters for email fields that were stored as entered, so they can differ in case from the
/// lowercase emails the rest of the server works with.
/// </summary>
internal static class EmailFilter
{
    /// <summary>
    /// Matches documents whose <paramref name="field"/> equals any of <paramref name="emails"/>, ignoring
    /// case and surrounding whitespace in the input. Each value is matched exactly (anchored and escaped),
    /// never as a pattern. Null when no usable email is given, so callers can skip the query.
    /// </summary>
    public static FilterDefinition<T>? MatchAnyIgnoringCase<T>(
        Expression<Func<T, object>> field, IEnumerable<string> emails)
    {
        var filters = emails
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Select(email => Builders<T>.Filter.Regex(field, new BsonRegularExpression($"^{Regex.Escape(email)}$", "i")))
            .ToList();

        return filters.Count == 0 ? null : Builders<T>.Filter.Or(filters);
    }
}
