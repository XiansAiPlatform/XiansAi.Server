using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models.Usage;
using Shared.Utils;

namespace Shared.Repositories;

public interface ITokenUsageLimitRepository
{
    Task<TokenUsageLimit?> GetTenantLimitAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<TokenUsageLimit?> GetUserLimitAsync(string tenantId, string userId, CancellationToken cancellationToken = default);
    Task<TokenUsageLimit?> GetEffectiveLimitAsync(string tenantId, string? userId, CancellationToken cancellationToken = default);
    Task<List<TokenUsageLimit>> GetLimitsForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<TokenUsageLimit> UpsertAsync(TokenUsageLimit limit, CancellationToken cancellationToken = default);
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);
}

public interface ITokenUsageWindowRepository
{
    Task<TokenUsageWindow?> GetWindowAsync(string tenantId, string userId, DateTime windowStart, int windowSeconds, CancellationToken cancellationToken = default);
    Task<TokenUsageWindow> IncrementWindowAsync(string tenantId, string userId, DateTime windowStart, int windowSeconds, long tokensToAdd, CancellationToken cancellationToken = default);
    Task<bool> ResetWindowAsync(string tenantId, string userId, DateTime windowStart, CancellationToken cancellationToken = default);
}

public interface ITokenUsageEventRepository
{
    Task InsertAsync(TokenUsageEvent usageEvent, CancellationToken cancellationToken = default);
    Task<List<TokenUsageEvent>> GetEventsAsync(string tenantId, string? userId, DateTime? since = null, CancellationToken cancellationToken = default);
}

public class TokenUsageLimitRepository : ITokenUsageLimitRepository
{
    private readonly IMongoCollection<TokenUsageLimit> _collection;
    private readonly ILogger<TokenUsageLimitRepository> _logger;

    public TokenUsageLimitRepository(IDatabaseService databaseService, ILogger<TokenUsageLimitRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<TokenUsageLimit>("token_usage_limits");
        _logger = logger;
    }

    public async Task<TokenUsageLimit?> GetTenantLimitAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(x => x.TenantId == tenantId && (x.UserId == null || x.UserId == string.Empty))
                .FirstOrDefaultAsync(cancellationToken);
        }, _logger, operationName: "GetTenantUsageLimit");
    }

    public async Task<TokenUsageLimit?> GetUserLimitAsync(string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return null;
        }

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(x => x.TenantId == tenantId && x.UserId == userId)
                .FirstOrDefaultAsync(cancellationToken);
        }, _logger, operationName: "GetUserUsageLimit");
    }

    public async Task<TokenUsageLimit?> GetEffectiveLimitAsync(string tenantId, string? userId, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(userId))
        {
            var userLimit = await GetUserLimitAsync(tenantId, userId, cancellationToken);
            if (userLimit?.Enabled == true)
            {
                return userLimit;
            }
        }

        var tenantLimit = await GetTenantLimitAsync(tenantId, cancellationToken);
        return tenantLimit?.Enabled == true ? tenantLimit : null;
    }

    public async Task<List<TokenUsageLimit>> GetLimitsForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            return await _collection.Find(x => x.TenantId == tenantId)
                .SortBy(x => x.UserId)
                .ToListAsync(cancellationToken);
        }, _logger, operationName: "GetUsageLimitsForTenant");
    }

    public async Task<TokenUsageLimit> UpsertAsync(TokenUsageLimit limit, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var normalizedUserId = string.IsNullOrWhiteSpace(limit.UserId) ? null : limit.UserId;
            limit.UserId = normalizedUserId;

            var tenantFilter = Builders<TokenUsageLimit>.Filter.Eq(x => x.TenantId, limit.TenantId);
            FilterDefinition<TokenUsageLimit> userFilter = normalizedUserId == null
                ? Builders<TokenUsageLimit>.Filter.Or(
                    Builders<TokenUsageLimit>.Filter.Eq(x => x.UserId, null),
                    Builders<TokenUsageLimit>.Filter.Eq(x => x.UserId, string.Empty))
                : Builders<TokenUsageLimit>.Filter.Eq(x => x.UserId, normalizedUserId);

            var filter = Builders<TokenUsageLimit>.Filter.And(tenantFilter, userFilter);

            var now = DateTime.UtcNow;

            var update = Builders<TokenUsageLimit>.Update
                .Set(x => x.MaxTokens, limit.MaxTokens)
                .Set(x => x.WindowSeconds, limit.WindowSeconds)
                .Set(x => x.Enabled, limit.Enabled)
                .Set(x => x.EffectiveFrom, limit.EffectiveFrom == default ? now : limit.EffectiveFrom)
                .Set(x => x.UpdatedAt, now)
                .Set(x => x.UpdatedBy, limit.UpdatedBy)
                .Set(x => x.UserId, normalizedUserId)
                .SetOnInsert(x => x.CreatedAt, now)
                .SetOnInsert(x => x.TenantId, limit.TenantId);

            var options = new FindOneAndUpdateOptions<TokenUsageLimit>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            return await _collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        }, _logger, operationName: "UpsertUsageLimit");
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<TokenUsageLimit>.Filter.Eq(x => x.Id, id);
            var result = await _collection.DeleteOneAsync(filter, cancellationToken);
            return result.DeletedCount > 0;
        }, _logger, operationName: "DeleteUsageLimit");
    }
}

public class TokenUsageWindowRepository : ITokenUsageWindowRepository
{
    private readonly IMongoCollection<TokenUsageWindow> _collection;
    private readonly ILogger<TokenUsageWindowRepository> _logger;

    public TokenUsageWindowRepository(IDatabaseService databaseService, ILogger<TokenUsageWindowRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<TokenUsageWindow>("token_usage_windows");
        _logger = logger;
    }

    public async Task<TokenUsageWindow?> GetWindowAsync(string tenantId, string userId, DateTime windowStart, int windowSeconds, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<TokenUsageWindow>.Filter.And(
                Builders<TokenUsageWindow>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.UserId, userId),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.WindowStart, windowStart),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.WindowSeconds, windowSeconds)
            );

            return await _collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        }, _logger, operationName: "GetUsageWindow");
    }

    public async Task<TokenUsageWindow> IncrementWindowAsync(string tenantId, string userId, DateTime windowStart, int windowSeconds, long tokensToAdd, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var now = DateTime.UtcNow;

            var filter = Builders<TokenUsageWindow>.Filter.And(
                Builders<TokenUsageWindow>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.UserId, userId),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.WindowStart, windowStart),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.WindowSeconds, windowSeconds)
            );

            var update = Builders<TokenUsageWindow>.Update
                .Inc(x => x.TokensUsed, tokensToAdd)
                .Set(x => x.UpdatedAt, now)
                .SetOnInsert(x => x.TenantId, tenantId)
                .SetOnInsert(x => x.UserId, userId)
                .SetOnInsert(x => x.WindowStart, windowStart)
                .SetOnInsert(x => x.WindowSeconds, windowSeconds);

            var options = new FindOneAndUpdateOptions<TokenUsageWindow>
            {
                IsUpsert = true,
                ReturnDocument = ReturnDocument.After
            };

            return await _collection.FindOneAndUpdateAsync(filter, update, options, cancellationToken);
        }, _logger, operationName: "IncrementUsageWindow");
    }

    public async Task<bool> ResetWindowAsync(string tenantId, string userId, DateTime windowStart, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<TokenUsageWindow>.Filter.And(
                Builders<TokenUsageWindow>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.UserId, userId),
                Builders<TokenUsageWindow>.Filter.Eq(x => x.WindowStart, windowStart)
            );

            var result = await _collection.DeleteOneAsync(filter, cancellationToken);
            return result.DeletedCount > 0;
        }, _logger, operationName: "ResetUsageWindow");
    }
}

public class TokenUsageEventRepository : ITokenUsageEventRepository
{
    private readonly IMongoCollection<TokenUsageEvent> _collection;
    private readonly ILogger<TokenUsageEventRepository> _logger;

    public TokenUsageEventRepository(IDatabaseService databaseService, ILogger<TokenUsageEventRepository> logger)
    {
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<TokenUsageEvent>("token_usage_events");
        _logger = logger;
    }

    public async Task InsertAsync(TokenUsageEvent usageEvent, CancellationToken cancellationToken = default)
    {
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            usageEvent.CreatedAt = usageEvent.CreatedAt == default ? DateTime.UtcNow : usageEvent.CreatedAt;
            await _collection.InsertOneAsync(usageEvent, cancellationToken: cancellationToken);
        }, _logger, operationName: "InsertUsageEvent");
    }

    public async Task<List<TokenUsageEvent>> GetEventsAsync(string tenantId, string? userId, DateTime? since = null, CancellationToken cancellationToken = default)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var builder = Builders<TokenUsageEvent>.Filter;
            var filters = new List<FilterDefinition<TokenUsageEvent>>
            {
                builder.Eq(x => x.TenantId, tenantId)
            };

            if (!string.IsNullOrWhiteSpace(userId))
            {
                filters.Add(builder.Eq(x => x.UserId, userId));
            }

            if (since.HasValue)
            {
                filters.Add(builder.Gt(x => x.CreatedAt, since.Value));
            }

            var filter = builder.And(filters);
            return await _collection.Find(filter)
                .SortByDescending(x => x.CreatedAt)
                .Limit(1000)
                .ToListAsync(cancellationToken);
        }, _logger, operationName: "GetUsageEvents");
    }
}

