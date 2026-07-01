using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// Persistence for the webhook outbox (collection <c>webhook_deliveries</c>). Provides an atomic
/// claim so that, across multiple server instances, exactly one instance delivers each row.
/// </summary>
public interface IWebhookDeliveryRepository
{
    /// <summary>Inserts one or more pending delivery rows.</summary>
    Task InsertManyAsync(IReadOnlyList<WebhookDelivery> deliveries);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> deliveries that are due for
    /// (re)attempt and not currently leased, transitioning them to <see cref="WebhookDeliveryStatus.Delivering"/>.
    /// Also reclaims rows whose previous lease has expired (e.g. a crashed instance).
    /// </summary>
    Task<List<WebhookDelivery>> ClaimDueBatchAsync(string claimedBy, DateTime now, TimeSpan lease, int batchSize);

    /// <summary>Marks a delivery as successfully delivered.</summary>
    Task MarkDeliveredAsync(string id, DateTime deliveredAt);

    /// <summary>Schedules a delivery for another attempt after <paramref name="nextAttemptAt"/>.</summary>
    Task MarkRetryAsync(string id, int attemptCount, DateTime nextAttemptAt, string? error);

    /// <summary>Marks a delivery as permanently failed.</summary>
    Task MarkFailedAsync(string id, int attemptCount, string? error);
}

public class WebhookDeliveryRepository : IWebhookDeliveryRepository
{
    private const string CollectionName = "webhook_deliveries";

    private readonly IMongoCollection<WebhookDelivery> _collection;
    private readonly ILogger<WebhookDeliveryRepository> _logger;

    public WebhookDeliveryRepository(IDatabaseService databaseService, ILogger<WebhookDeliveryRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<WebhookDelivery>(CollectionName);
    }

    public async Task InsertManyAsync(IReadOnlyList<WebhookDelivery> deliveries)
    {
        if (deliveries == null || deliveries.Count == 0)
        {
            return;
        }

        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _collection.InsertManyAsync(deliveries);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "InsertWebhookDeliveries");
    }

    public async Task<List<WebhookDelivery>> ClaimDueBatchAsync(string claimedBy, DateTime now, TimeSpan lease, int batchSize)
    {
        var claimed = new List<WebhookDelivery>();

        // A row is claimable when it is due (next_attempt_at <= now) and not actively leased.
        // Both Pending rows and stale Delivering rows (expired lease) are eligible, which lets a
        // surviving instance recover deliveries abandoned by a crashed one.
        var filter = Builders<WebhookDelivery>.Filter.And(
            Builders<WebhookDelivery>.Filter.In(d => d.Status,
                new[] { WebhookDeliveryStatus.Pending, WebhookDeliveryStatus.Delivering }),
            Builders<WebhookDelivery>.Filter.Lte(d => d.NextAttemptAt, now),
            Builders<WebhookDelivery>.Filter.Or(
                Builders<WebhookDelivery>.Filter.Eq(d => d.LeaseExpiresAt, (DateTime?)null),
                Builders<WebhookDelivery>.Filter.Lte(d => d.LeaseExpiresAt, now)));

        var update = Builders<WebhookDelivery>.Update
            .Set(d => d.Status, WebhookDeliveryStatus.Delivering)
            .Set(d => d.ClaimedBy, claimedBy)
            .Set(d => d.LeaseExpiresAt, now.Add(lease));

        var options = new FindOneAndUpdateOptions<WebhookDelivery>
        {
            ReturnDocument = ReturnDocument.After,
            Sort = Builders<WebhookDelivery>.Sort.Ascending(d => d.NextAttemptAt)
        };

        for (var i = 0; i < batchSize; i++)
        {
            var doc = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _collection.FindOneAndUpdateAsync(filter, update, options),
                _logger, maxRetries: 3, baseDelayMs: 100, operationName: "ClaimWebhookDelivery");

            if (doc == null)
            {
                break;
            }

            claimed.Add(doc);
        }

        return claimed;
    }

    public async Task MarkDeliveredAsync(string id, DateTime deliveredAt)
    {
        var update = Builders<WebhookDelivery>.Update
            .Set(d => d.Status, WebhookDeliveryStatus.Delivered)
            .Set(d => d.DeliveredAt, deliveredAt)
            .Set(d => d.LeaseExpiresAt, (DateTime?)null)
            .Set(d => d.LastError, (string?)null);

        await UpdateByIdAsync(id, update, "MarkWebhookDeliveryDelivered");
    }

    public async Task MarkRetryAsync(string id, int attemptCount, DateTime nextAttemptAt, string? error)
    {
        var update = Builders<WebhookDelivery>.Update
            .Set(d => d.Status, WebhookDeliveryStatus.Pending)
            .Set(d => d.AttemptCount, attemptCount)
            .Set(d => d.NextAttemptAt, nextAttemptAt)
            .Set(d => d.LeaseExpiresAt, (DateTime?)null)
            .Set(d => d.LastError, Truncate(error));

        await UpdateByIdAsync(id, update, "MarkWebhookDeliveryRetry");
    }

    public async Task MarkFailedAsync(string id, int attemptCount, string? error)
    {
        var update = Builders<WebhookDelivery>.Update
            .Set(d => d.Status, WebhookDeliveryStatus.Failed)
            .Set(d => d.AttemptCount, attemptCount)
            .Set(d => d.LeaseExpiresAt, (DateTime?)null)
            .Set(d => d.LastError, Truncate(error));

        await UpdateByIdAsync(id, update, "MarkWebhookDeliveryFailed");
    }

    private async Task UpdateByIdAsync(string id, UpdateDefinition<WebhookDelivery> update, string operationName)
    {
        var filter = Builders<WebhookDelivery>.Filter.Eq(d => d.Id, id);
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _collection.UpdateOneAsync(filter, update);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: operationName);
    }

    private static string? Truncate(string? value, int maxLength = 2000)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
        {
            return value;
        }

        return value.Substring(0, maxLength);
    }
}
