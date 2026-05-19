using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Data.Models;
using Shared.Utils;

namespace Shared.Repositories;

public interface IFeedbackRepository
{
    Task<string> SaveFeedbackAsync(MessageFeedbackDocument feedback);
    Task<MessageFeedbackDocument?> GetFeedbackByMessageIdAsync(string messageId, string tenantId);
    Task<Dictionary<string, MessageFeedbackDocument>> GetFeedbackByMessageIdsAsync(IEnumerable<string> messageIds, string tenantId);
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
}
