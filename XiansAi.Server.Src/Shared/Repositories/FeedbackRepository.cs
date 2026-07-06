using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// Filter used when querying feedback for admin visualization. All filters are optional except the
/// tenant, which is always required to keep queries tenant-scoped. Date filters apply to
/// <see cref="MessageFeedbackDocument.SubmittedAt"/>.
/// </summary>
public sealed class FeedbackQuery
{
    public required string TenantId { get; init; }
    public int? StarRating { get; init; }
    public string? AgentName { get; init; }
    public DateTime? StartDate { get; init; }
    public DateTime? EndDate { get; init; }
}

/// <summary>Count of feedback entries for a single star rating value.</summary>
public sealed class RatingCountBucket
{
    public int Rating { get; set; }
    public int Count { get; set; }
}

/// <summary>Count of feedback entries for a single reason category.</summary>
public sealed class ReasonCategoryCountBucket
{
    public Shared.Data.Models.FeedbackReasonCategory Category { get; set; }
    public int Count { get; set; }
}

/// <summary>Aggregated feedback statistics for a tenant (optionally filtered).</summary>
public sealed class FeedbackStatsResult
{
    public long Total { get; set; }
    public double AverageRating { get; set; }
    public List<RatingCountBucket> RatingCounts { get; set; } = new();
    public List<ReasonCategoryCountBucket> ReasonCategoryCounts { get; set; } = new();
}

public interface IFeedbackRepository
{
    Task<string> SaveFeedbackAsync(MessageFeedbackDocument feedback);
    Task<MessageFeedbackDocument?> GetFeedbackByMessageIdAsync(string messageId, string tenantId);
    Task<Dictionary<string, MessageFeedbackDocument>> GetFeedbackByMessageIdsAsync(IEnumerable<string> messageIds, string tenantId);

    /// <summary>Gets a single feedback document by its id, scoped to the tenant. Returns null when not found.</summary>
    Task<MessageFeedbackDocument?> GetFeedbackByIdAsync(string id, string tenantId);

    /// <summary>Returns a page of feedback (newest first) matching the query, plus the total match count.</summary>
    Task<(List<MessageFeedbackDocument> Items, long TotalCount)> QueryFeedbackAsync(FeedbackQuery query, int page, int pageSize);

    /// <summary>Returns aggregated statistics (counts per rating and per reason category) for the query.</summary>
    Task<FeedbackStatsResult> GetFeedbackStatsAsync(FeedbackQuery query);
}

public class FeedbackRepository : IFeedbackRepository
{
    private readonly IMongoCollection<MessageFeedbackDocument> _collection;
    private readonly ILogger<FeedbackRepository> _logger;

    public FeedbackRepository(IDatabaseService databaseService, ILogger<FeedbackRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<MessageFeedbackDocument>("message_feedback");
    }

    public async Task<string> SaveFeedbackAsync(MessageFeedbackDocument feedback)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            if (string.IsNullOrEmpty(feedback.Id))
            {
                feedback.Id = ObjectId.GenerateNewId().ToString();
            }

            await _collection.InsertOneAsync(feedback);
            return feedback.Id;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "SaveMessageFeedback");
    }

    public async Task<MessageFeedbackDocument?> GetFeedbackByMessageIdAsync(string messageId, string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<MessageFeedbackDocument>.Filter.And(
                Builders<MessageFeedbackDocument>.Filter.Eq(f => f.MessageId, messageId),
                Builders<MessageFeedbackDocument>.Filter.Eq(f => f.TenantId, tenantId));

            return await _collection.Find(filter).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetFeedbackByMessageId");
    }

    public async Task<Dictionary<string, MessageFeedbackDocument>> GetFeedbackByMessageIdsAsync(
        IEnumerable<string> messageIds,
        string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var idList = messageIds.Where(id => !string.IsNullOrEmpty(id)).Distinct().ToList();
            if (idList.Count == 0)
            {
                return new Dictionary<string, MessageFeedbackDocument>(StringComparer.Ordinal);
            }

            var filter = Builders<MessageFeedbackDocument>.Filter.And(
                Builders<MessageFeedbackDocument>.Filter.In(f => f.MessageId, idList),
                Builders<MessageFeedbackDocument>.Filter.Eq(f => f.TenantId, tenantId));

            var list = await _collection.Find(filter).ToListAsync();
            return list.ToDictionary(f => f.MessageId, f => f, StringComparer.Ordinal);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetFeedbackByMessageIds");
    }

    public async Task<MessageFeedbackDocument?> GetFeedbackByIdAsync(string id, string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<MessageFeedbackDocument>.Filter.And(
                Builders<MessageFeedbackDocument>.Filter.Eq(f => f.Id, id),
                Builders<MessageFeedbackDocument>.Filter.Eq(f => f.TenantId, tenantId));

            return await _collection.Find(filter).FirstOrDefaultAsync();
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetFeedbackById");
    }

    public async Task<(List<MessageFeedbackDocument> Items, long TotalCount)> QueryFeedbackAsync(
        FeedbackQuery query, int page, int pageSize)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = BuildQueryFilter(query);

            var totalCount = await _collection.CountDocumentsAsync(filter);

            var items = await _collection
                .Find(filter)
                .Sort(Builders<MessageFeedbackDocument>.Sort.Descending(f => f.SubmittedAt))
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "QueryFeedback");
    }

    public async Task<FeedbackStatsResult> GetFeedbackStatsAsync(FeedbackQuery query)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = BuildQueryFilter(query);

            // Counts grouped by star rating (1..5).
            var ratingGroups = await _collection
                .Aggregate()
                .Match(filter)
                .Group(f => f.StarRating, g => new RatingCountBucket { Rating = g.Key, Count = g.Count() })
                .ToListAsync();

            // Counts grouped by reason category, ignoring entries without a category (typically 4-5 stars).
            var reasonFilter = Builders<MessageFeedbackDocument>.Filter.And(
                filter,
                Builders<MessageFeedbackDocument>.Filter.Ne(f => f.ReasonCategory, null));

            var reasonGroups = await _collection
                .Aggregate()
                .Match(reasonFilter)
                .Group(f => f.ReasonCategory!.Value,
                    g => new ReasonCategoryCountBucket { Category = g.Key, Count = g.Count() })
                .ToListAsync();

            var total = ratingGroups.Sum(r => (long)r.Count);
            var weightedSum = ratingGroups.Sum(r => (long)r.Rating * r.Count);
            var average = total > 0 ? Math.Round((double)weightedSum / total, 2) : 0d;

            return new FeedbackStatsResult
            {
                Total = total,
                AverageRating = average,
                RatingCounts = ratingGroups,
                ReasonCategoryCounts = reasonGroups
            };
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetFeedbackStats");
    }

    private static FilterDefinition<MessageFeedbackDocument> BuildQueryFilter(FeedbackQuery query)
    {
        var builder = Builders<MessageFeedbackDocument>.Filter;
        var conditions = new List<FilterDefinition<MessageFeedbackDocument>>
        {
            builder.Eq(f => f.TenantId, query.TenantId)
        };

        if (query.StarRating.HasValue)
        {
            conditions.Add(builder.Eq(f => f.StarRating, query.StarRating.Value));
        }

        if (!string.IsNullOrWhiteSpace(query.AgentName))
        {
            conditions.Add(builder.Eq(f => f.AgentName, query.AgentName));
        }

        if (query.StartDate.HasValue)
        {
            conditions.Add(builder.Gte(f => f.SubmittedAt, query.StartDate.Value));
        }

        if (query.EndDate.HasValue)
        {
            conditions.Add(builder.Lte(f => f.SubmittedAt, query.EndDate.Value));
        }

        return builder.And(conditions);
    }
}
