using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

/// <summary>
/// Persistence for GDPR data subject requests (collection <c>privacy_requests</c>). Provides an atomic,
/// lease-based claim so that across server instances exactly one runs each due erasure.
/// </summary>
/// <remarks>
/// Writes made while processing a claimed request take the claim's <see cref="PrivacyRequest.LeaseId"/> and
/// return false when it no longer matches. A worker that gets false has lost the request and must stop.
/// </remarks>
public interface IPrivacyRequestRepository
{
    /// <summary>Stores a new request. Safe to call again with the same request after an unclear failure.</summary>
    Task CreateAsync(PrivacyRequest request);

    Task<PrivacyRequest?> GetByIdAsync(string id);

    /// <summary>Requests made by one user, newest first, found by the pseudonymised user ID.</summary>
    Task<List<PrivacyRequest>> ListByRequesterAsync(string requesterUserHash, int limit);

    /// <summary>Requests covering a tenant, newest first, optionally limited to some statuses.</summary>
    Task<List<PrivacyRequest>> ListByTenantAsync(string tenantId, IReadOnlyCollection<PrivacyRequestStatus>? statuses, int limit);

    /// <summary>All requests, newest first, optionally limited to some statuses.</summary>
    Task<List<PrivacyRequest>> ListAllAsync(IReadOnlyCollection<PrivacyRequestStatus>? statuses, int limit);

    /// <summary>
    /// Cancels a request and scrubs its plain identifiers. Succeeds only while it is pending and only for the user
    /// who made it, identified by the pseudonymised user ID.
    /// </summary>
    Task<bool> CancelAsync(string id, string requesterUserHash, DateTime now);

    /// <summary>
    /// Stores the record of an export after its download finished. Exports are streamed straight to the requester,
    /// so the record is written already completed and scrubbed.
    /// </summary>
    Task RecordExportDeliveredAsync(PrivacyRequest export, DateTime deliveredAt);

    /// <summary>
    /// Atomically claims up to <paramref name="batchSize"/> erasures that are due and not actively leased,
    /// marking them running with a new lease ID and counting the attempt. Also reclaims requests whose lease
    /// expired, such as after a crash. The caller fails requests whose attempts exceed the limit.
    /// </summary>
    Task<List<PrivacyRequest>> ClaimDueBatchAsync(string claimedBy, DateTime now, TimeSpan lease, int batchSize);

    /// <summary>Stores the resolved identifier snapshot and the hash of its primary identifier.</summary>
    Task<bool> SaveResolvedSubjectAsync(string id, string leaseId, PrivacySubject subject, string subjectHash);

    /// <summary>Records one contributor's outcome and notes, replacing any earlier outcome for the same contributor.</summary>
    Task<bool> RecordResultAsync(string id, string leaseId, PrivacyContributorResult result);

    /// <summary>Releases the lease, ending the caller's hold, and schedules another attempt.</summary>
    Task<bool> MarkRetryAsync(string id, string leaseId, DateTime nextAttemptAt, string? error);

    /// <summary>
    /// Marks the request completed and scrubs every plain identifier, keeping the subject hash, scope and results.
    /// </summary>
    Task<bool> CompleteAsync(string id, string leaseId, DateTime now);

    /// <summary>
    /// Marks the request failed for operations follow-up. Identifiers are kept so it can be investigated, and
    /// <c>failed_at</c> is set so the record expires sooner than other closed requests.
    /// </summary>
    Task<bool> FailAsync(string id, string leaseId, DateTime now, string? error);
}

public class PrivacyRequestRepository : IPrivacyRequestRepository
{
    private const string CollectionName = "privacy_requests";
    private const int MaxErrorLength = 2000;

    private static readonly FilterDefinitionBuilder<PrivacyRequest> Filter = Builders<PrivacyRequest>.Filter;
    private static readonly UpdateDefinitionBuilder<PrivacyRequest> Update = Builders<PrivacyRequest>.Update;

    private readonly IMongoCollection<PrivacyRequest> _collection;
    private readonly ILogger<PrivacyRequestRepository> _logger;

    public PrivacyRequestRepository(IDatabaseService databaseService, ILogger<PrivacyRequestRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<PrivacyRequest>(CollectionName);
    }

    public async Task CreateAsync(PrivacyRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _collection.InsertOneAsync(request),
                _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreatePrivacyRequest");
        }
        catch (MongoWriteException ex) when (ex.WriteError?.Category == ServerErrorCategory.DuplicateKey)
        {
            // An earlier attempt can commit before its error reaches us, so the request may already exist.
            if (await GetByIdAsync(request.Id) == null)
            {
                throw;
            }
        }
    }

    public async Task<PrivacyRequest?> GetByIdAsync(string id)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _collection.Find(r => r.Id == id).FirstOrDefaultAsync(),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetPrivacyRequest");
    }

    public Task<List<PrivacyRequest>> ListByRequesterAsync(string requesterUserHash, int limit)
    {
        return ListAsync(Filter.Eq(r => r.RequestedBy.UserHash, requesterUserHash), limit, "ListPrivacyRequestsByRequester");
    }

    public Task<List<PrivacyRequest>> ListByTenantAsync(
        string tenantId, IReadOnlyCollection<PrivacyRequestStatus>? statuses, int limit)
    {
        var filter = Filter.AnyEq(r => r.Scope.TenantIds, tenantId) & StatusFilter(statuses);
        return ListAsync(filter, limit, "ListPrivacyRequestsByTenant");
    }

    public Task<List<PrivacyRequest>> ListAllAsync(IReadOnlyCollection<PrivacyRequestStatus>? statuses, int limit)
    {
        return ListAsync(StatusFilter(statuses), limit, "ListPrivacyRequests");
    }

    public Task<bool> CancelAsync(string id, string requesterUserHash, DateTime now)
    {
        var filter = Filter.Eq(r => r.Id, id)
                     & Filter.Eq(r => r.Status, PrivacyRequestStatus.Pending)
                     & Filter.Eq(r => r.RequestedBy.UserHash, requesterUserHash);

        var fields = ScrubbedFields();
        fields.Add("status", nameof(PrivacyRequestStatus.Cancelled));
        fields.Add("cancelled_at", now);
        fields.Add("closed_at", now);

        return UpdateOneAsync(filter, Pipeline(fields), "CancelPrivacyRequest");
    }

    public async Task RecordExportDeliveredAsync(PrivacyRequest export, DateTime deliveredAt)
    {
        ArgumentNullException.ThrowIfNull(export);
        if (export.Type != PrivacyRequestType.Export)
        {
            throw new ArgumentException("Only export requests are recorded on delivery.", nameof(export));
        }

        export.Status = PrivacyRequestStatus.Completed;
        export.RunAfter = export.CreatedAt;
        export.CompletedAt = deliveredAt;
        export.ClosedAt = deliveredAt;
        export.Subject.Value = null;
        export.ResolvedSubject = null;
        if (export.RequestedBy.ActorType == PrivacyActorType.Self)
        {
            export.RequestedBy.UserId = null;
        }

        await CreateAsync(export);
    }

    public async Task<List<PrivacyRequest>> ClaimDueBatchAsync(string claimedBy, DateTime now, TimeSpan lease, int batchSize)
    {
        // Running requests stay claimable so a retry, or a request abandoned by a crashed instance, is picked up.
        var filter = Filter.Eq(r => r.Type, PrivacyRequestType.Erasure)
                     & Filter.In(r => r.Status, [PrivacyRequestStatus.Pending, PrivacyRequestStatus.Running])
                     & Filter.Lte(r => r.RunAfter, now)
                     & (Filter.Eq(r => r.NextAttemptAt, null) | Filter.Lte(r => r.NextAttemptAt, now))
                     & (Filter.Eq(r => r.LeaseExpiresAt, null) | Filter.Lte(r => r.LeaseExpiresAt, now));

        var options = new FindOneAndUpdateOptions<PrivacyRequest>
        {
            ReturnDocument = ReturnDocument.After,
            Sort = Builders<PrivacyRequest>.Sort.Ascending(r => r.RunAfter)
        };

        var claimed = new List<PrivacyRequest>();
        for (var i = 0; i < batchSize; i++)
        {
            var update = Update
                .Set(r => r.Status, PrivacyRequestStatus.Running)
                .Set(r => r.ClaimedBy, claimedBy)
                .Set(r => r.LeaseExpiresAt, now.Add(lease))
                .Set(r => r.LeaseId, Guid.NewGuid().ToString("N"))
                .Inc(r => r.Attempts, 1);

            // No retry here. The driver already retries this write safely, and a second attempt after a lost reply
            // would claim another request and leave the first leased with an attempt used.
            var request = await _collection.FindOneAndUpdateAsync(filter, update, options);

            if (request == null)
            {
                break;
            }

            claimed.Add(request);
        }

        return claimed;
    }

    public Task<bool> SaveResolvedSubjectAsync(string id, string leaseId, PrivacySubject subject, string subjectHash)
    {
        ArgumentNullException.ThrowIfNull(subject);

        var update = Update
            .Set(r => r.ResolvedSubject, subject)
            .Set(r => r.SubjectHash, subjectHash);

        return UpdateOneAsync(Held(id, leaseId), update, "SavePrivacyRequestSubject");
    }

    public Task<bool> RecordResultAsync(string id, string leaseId, PrivacyContributorResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        result.Error = Truncate(result.Error);

        // A pipeline replaces the contributor's earlier result in one step, which a pull plus push cannot do. The notes
        // travel inside the result, so applying the same write twice cannot duplicate them.
        var replaced = new BsonDocument("$concatArrays", new BsonArray
        {
            new BsonDocument("$filter", new BsonDocument
            {
                { "input", new BsonDocument("$ifNull", new BsonArray { "$results", new BsonArray() }) },
                { "cond", new BsonDocument("$ne", new BsonArray { "$$this.name", result.Name }) }
            }),
            new BsonArray { new BsonDocument("$literal", result.ToBsonDocument()) }
        });

        return UpdateOneAsync(Held(id, leaseId), Pipeline(new BsonDocument("results", replaced)),
            "RecordPrivacyContributorResult");
    }

    public Task<bool> MarkRetryAsync(string id, string leaseId, DateTime nextAttemptAt, string? error)
    {
        var update = Update
            .Set(r => r.NextAttemptAt, nextAttemptAt)
            .Set(r => r.LeaseExpiresAt, null)
            .Set(r => r.LeaseId, null)
            .Set(r => r.LastError, Truncate(error));

        return UpdateOneAsync(Held(id, leaseId), update, "MarkPrivacyRequestRetry");
    }

    public Task<bool> CompleteAsync(string id, string leaseId, DateTime now)
    {
        // One update, so a crash cannot leave a completed request with some identifiers still on it.
        var fields = ScrubbedFields();
        fields.Add("status", nameof(PrivacyRequestStatus.Completed));
        fields.Add("completed_at", now);
        fields.Add("closed_at", now);
        fields.Add("lease_expires_at", BsonNull.Value);
        fields.Add("last_error", BsonNull.Value);

        return UpdateOneAsync(Held(id, leaseId), Pipeline(fields), "CompletePrivacyRequest");
    }

    public Task<bool> FailAsync(string id, string leaseId, DateTime now, string? error)
    {
        var update = Update
            .Set(r => r.Status, PrivacyRequestStatus.Failed)
            .Set(r => r.ClosedAt, now)
            .Set(r => r.FailedAt, now)
            .Set(r => r.LeaseExpiresAt, null)
            .Set(r => r.LastError, Truncate(error));

        return UpdateOneAsync(Held(id, leaseId), update, "FailPrivacyRequest");
    }

    // A self-service requester is the subject, so their user ID is scrubbed too. Admin requesters are kept.
    private static BsonDocument ScrubbedFields()
    {
        var requesterUserId = new BsonDocument("$cond", new BsonArray
        {
            new BsonDocument("$eq", new BsonArray { "$requested_by.actor_type", nameof(PrivacyActorType.Self) }),
            BsonNull.Value,
            "$requested_by.user_id"
        });

        return new BsonDocument
        {
            { "subject.value", BsonNull.Value },
            { "resolved_subject", BsonNull.Value },
            { "requested_by.user_id", requesterUserId }
        };
    }

    private static FilterDefinition<PrivacyRequest> Held(string id, string leaseId)
    {
        return Filter.Eq(r => r.Id, id)
               & Filter.Eq(r => r.Status, PrivacyRequestStatus.Running)
               & Filter.Eq(r => r.LeaseId, leaseId);
    }

    private static UpdateDefinition<PrivacyRequest> Pipeline(BsonDocument fields)
    {
        return new PipelineUpdateDefinition<PrivacyRequest>(
            new BsonDocumentStagePipelineDefinition<PrivacyRequest, PrivacyRequest>([new BsonDocument("$set", fields)]));
    }

    private async Task<List<PrivacyRequest>> ListAsync(FilterDefinition<PrivacyRequest> filter, int limit, string operationName)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _collection.Find(filter)
                .SortByDescending(r => r.CreatedAt)
                .Limit(Math.Max(1, limit))
                .ToListAsync(),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: operationName);
    }

    private static FilterDefinition<PrivacyRequest> StatusFilter(IReadOnlyCollection<PrivacyRequestStatus>? statuses)
    {
        return statuses == null || statuses.Count == 0
            ? Filter.Empty
            : Filter.In(r => r.Status, statuses);
    }

    private async Task<bool> UpdateOneAsync(
        FilterDefinition<PrivacyRequest> filter, UpdateDefinition<PrivacyRequest> update, string operationName)
    {
        var result = await MongoRetryHelper.ExecuteWithRetryAsync(
            async () => await _collection.UpdateOneAsync(filter, update),
            _logger, maxRetries: 3, baseDelayMs: 100, operationName: operationName);

        return result.MatchedCount > 0;
    }

    private static string? Truncate(string? value)
    {
        return string.IsNullOrEmpty(value) || value.Length <= MaxErrorLength ? value : value[..MaxErrorLength];
    }
}
