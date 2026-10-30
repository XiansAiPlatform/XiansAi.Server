using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// Persistence for calls received by builtin webhooks (collection <c>webhook_incomings</c>).
/// Every method is scoped by tenant so one tenant can never read another tenant's rows.
/// </summary>
public interface IWebhookIncomingRepository
{
    /// <summary>Saves one incoming call.</summary>
    Task InsertAsync(WebhookIncoming incoming);

    /// <summary>
    /// Lists a connection's incoming calls, newest first, at most <see cref="WebhookIncomingRepository.MaxPageSize"/>
    /// at a time. Pass <paramref name="after"/> to continue from a previous page.
    /// </summary>
    Task<List<WebhookIncomingSummary>> ListAsync(string tenantId, string integrationId, WebhookIncomingCursor? after, int limit);

    /// <summary>Loads one incoming call, or null when it does not exist for this tenant and connection.</summary>
    Task<WebhookIncoming?> GetAsync(string tenantId, string integrationId, Guid id);

    /// <summary>Lists the incoming calls sent to a workflow, newest first, at most <see cref="WebhookIncomingRepository.MaxPageSize"/>.</summary>
    Task<List<WebhookIncomingSummary>> ListByWorkflowAsync(string tenantId, string workflowId, int limit);

    /// <summary>Keeps the newest <paramref name="keep"/> calls for a connection and deletes the rest.</summary>
    /// <returns>The number of rows deleted.</returns>
    Task<long> TrimAsync(string tenantId, string integrationId, int keep);

    /// <summary>Deletes every incoming call for a connection.</summary>
    /// <returns>The number of rows deleted.</returns>
    Task<long> DeleteByIntegrationAsync(string tenantId, string integrationId);
}

public class WebhookIncomingRepository : IWebhookIncomingRepository
{
    private const string CollectionName = "webhook_incomings";

    /// <summary>Largest page a list call returns, whatever limit the caller asks for.</summary>
    public const int MaxPageSize = 200;

    private static readonly FilterDefinitionBuilder<WebhookIncoming> Filter = Builders<WebhookIncoming>.Filter;

    private static readonly SortDefinition<WebhookIncoming> NewestFirst = Builders<WebhookIncoming>.Sort
        .Descending(i => i.ReceivedAt)
        .Descending(i => i.Id);

    private readonly IMongoCollection<WebhookIncoming> _collection;
    private readonly ILogger<WebhookIncomingRepository> _logger;

    public WebhookIncomingRepository(IDatabaseService databaseService, ILogger<WebhookIncomingRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<WebhookIncoming>(CollectionName);
    }

    public async Task InsertAsync(WebhookIncoming incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);

        try
        {
            await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            {
                await _collection.InsertOneAsync(incoming);
            }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "InsertWebhookIncoming");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // The id is fixed before the first attempt, so a duplicate means an earlier attempt
            // was saved but its acknowledgement was lost.
            _logger.LogDebug("Webhook incoming {Id} was already saved by an earlier attempt", incoming.Id);
        }
    }

    public async Task<List<WebhookIncomingSummary>> ListAsync(string tenantId, string integrationId, WebhookIncomingCursor? after, int limit)
    {
        var filter = ByIntegration(tenantId, integrationId);
        if (after != null)
        {
            filter &= OlderThan(ToUtc(after.ReceivedAt), after.Id, inclusive: false);
        }

        return await ListSummariesAsync(filter, limit, "ListWebhookIncomings");
    }

    public async Task<WebhookIncoming?> GetAsync(string tenantId, string integrationId, Guid id)
    {
        var filter = ByIntegration(tenantId, integrationId) & Filter.Eq(i => i.Id, id);

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            await _collection.Find(filter).FirstOrDefaultAsync(),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetWebhookIncoming");
    }

    public async Task<List<WebhookIncomingSummary>> ListByWorkflowAsync(string tenantId, string workflowId, int limit)
    {
        var filter = Filter.Eq(i => i.TenantId, tenantId) & Filter.Eq(i => i.WorkflowId, workflowId);

        return await ListSummariesAsync(filter, limit, "ListWebhookIncomingsByWorkflow");
    }

    public async Task<long> TrimAsync(string tenantId, string integrationId, int keep)
    {
        var scope = ByIntegration(tenantId, integrationId);

        // The newest row past the cap is the cut-off; it and everything older is deleted.
        var cutoff = await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            await _collection.Find(scope)
                .Sort(NewestFirst)
                .Skip(Math.Max(0, keep))
                .Limit(1)
                .Project(i => new WebhookIncomingCursor(i.ReceivedAt, i.Id))
                .FirstOrDefaultAsync(),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: "FindWebhookIncomingTrimCutoff");

        if (cutoff == null)
        {
            return 0;
        }

        var result = await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            await _collection.DeleteManyAsync(scope & OlderThan(cutoff.ReceivedAt, cutoff.Id, inclusive: true)),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: "TrimWebhookIncomings");

        return result.DeletedCount;
    }

    public async Task<long> DeleteByIntegrationAsync(string tenantId, string integrationId)
    {
        var result = await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            await _collection.DeleteManyAsync(ByIntegration(tenantId, integrationId)),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteWebhookIncomingsByIntegration");

        return result.DeletedCount;
    }

    /// <summary>Newest-first summaries for a filter, loading only the listed fields and never more than a page.</summary>
    private async Task<List<WebhookIncomingSummary>> ListSummariesAsync(
        FilterDefinition<WebhookIncoming> filter, int limit, string operationName)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            await _collection.Find(filter)
                .Sort(NewestFirst)
                .Limit(Math.Clamp(limit, 1, MaxPageSize))
                .Project(i => new WebhookIncomingSummary(i.Id, i.RequestId, i.ReceivedAt))
                .ToListAsync(),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: operationName);
    }

    /// <summary>
    /// Stored times are UTC. A cursor rebuilt from a query string is often Unspecified, which the
    /// driver would read as local time and shift the page boundary.
    /// </summary>
    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static FilterDefinition<WebhookIncoming> ByIntegration(string tenantId, string integrationId) =>
        Filter.Eq(i => i.TenantId, tenantId) & Filter.Eq(i => i.IntegrationId, integrationId);

    /// <summary>Rows that sort after (receivedAt, id) in newest-first order, optionally including that row.</summary>
    private static FilterDefinition<WebhookIncoming> OlderThan(DateTime receivedAt, Guid id, bool inclusive) =>
        Filter.Or(
            Filter.Lt(i => i.ReceivedAt, receivedAt),
            Filter.And(
                Filter.Eq(i => i.ReceivedAt, receivedAt),
                inclusive ? Filter.Lte(i => i.Id, id) : Filter.Lt(i => i.Id, id)));
}
