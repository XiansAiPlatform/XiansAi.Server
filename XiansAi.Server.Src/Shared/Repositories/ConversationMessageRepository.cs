using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using XiansAi.Server.Shared.Data;
using System.Text.Json.Serialization;

namespace Shared.Repositories;

public enum MessageDirection
{
    Incoming,
    Outgoing
}

public enum MessageStatus
{
    FailedToDeliverToWorkflow,
    DeliveredToWorkflow,
}

public class MessageLogEvent
{
    [BsonElement("timestamp")]
    public required DateTime Timestamp { get; set; }

    [BsonElement("event")]
    public required string Event { get; set; }

    [BsonElement("details")]
    public string? Details { get; set; }
}

public class ConversationMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("thread_id")]
    public required string ThreadId { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("direction")]
    [BsonRepresentation(BsonType.String)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public required MessageDirection Direction { get; set; }

    [BsonElement("content")]
    public required string Content { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageStatus? Status { get; set; }

    [BsonElement("metadata")]
    public string? Metadata { get; set; }

    [BsonElement("logs")]
    public List<MessageLogEvent>? Logs { get; set; }

    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }
}

public interface IConversationMessageRepository
{
    Task<ConversationMessage?> GetByIdAsync(string id);
    Task<List<ConversationMessage>> GetByThreadIdAsync(string tenantId, string threadId, int? page = null, int? pageSize = null);
    Task<List<ConversationMessage>> GetByStatusAsync(string tenantId, MessageStatus status, int? page = null, int? pageSize = null);
    Task<string> CreateAsync(ConversationMessage message);
    Task<bool> UpdateStatusAsync(string id, MessageStatus status);
    Task<bool> AddMessageLogAsync(string id, MessageLogEvent logEvent);
}

public class ConversationMessageRepository : IConversationMessageRepository
{
    private readonly IMongoCollection<ConversationMessage> _collection;
    
    public ConversationMessageRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _collection = database.GetCollection<ConversationMessage>("conversation_message");

        // Create indexes
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        // Message lookup index (tenant_id, thread_id, created_at)
        var messageLookupIndex = Builders<ConversationMessage>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.ThreadId)
            .Descending(x => x.CreatedAt);
        
        var messageLookupIndexModel = new CreateIndexModel<ConversationMessage>(
            messageLookupIndex, 
            new CreateIndexOptions { Background = true, Name = "message_lookup" }
        );

        // Channel lookup index (tenant_id, channel, channel_key)
        var channelLookupIndex = Builders<ConversationMessage>.IndexKeys
            .Ascending(x => x.TenantId);
        
        var channelLookupIndexModel = new CreateIndexModel<ConversationMessage>(
            channelLookupIndex,
            new CreateIndexOptions { Background = true, Name = "tenant_lookup" }
        );

        // Message status index (tenant_id, status)
        var messageStatusIndex = Builders<ConversationMessage>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.Status);
        
        var messageStatusIndexModel = new CreateIndexModel<ConversationMessage>(
            messageStatusIndex,
            new CreateIndexOptions { Background = true, Name = "message_status" }
        );

        // Create all indexes
        _collection.Indexes.CreateMany(new[] { 
            messageLookupIndexModel,
            channelLookupIndexModel,
            messageStatusIndexModel
        });
    }

    public async Task<ConversationMessage?> GetByIdAsync(string id)
    {
        return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<ConversationMessage>> GetByThreadIdAsync(string tenantId, string threadId, int? page = null, int? pageSize = null)
    {
        var filter = Builders<ConversationMessage>.Filter.And(
            Builders<ConversationMessage>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationMessage>.Filter.Eq(x => x.ThreadId, threadId)
        );

        var query = _collection.Find(filter).Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<List<ConversationMessage>> GetByParticipantChannelIdAsync(string tenantId, int? page = null, int? pageSize = null)
    {
        var filter = Builders<ConversationMessage>.Filter.And(
            Builders<ConversationMessage>.Filter.Eq(x => x.TenantId, tenantId)
        );

        var query = _collection.Find(filter).Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<List<ConversationMessage>> GetByStatusAsync(string tenantId, MessageStatus status, int? page = null, int? pageSize = null)
    {
        var filter = Builders<ConversationMessage>.Filter.And(
            Builders<ConversationMessage>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationMessage>.Filter.Eq(x => x.Status, status)
        );

        var query = _collection.Find(filter).Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<string> CreateAsync(ConversationMessage message)
    {
        if (message.Logs == null)
        {
            message.Logs = new List<MessageLogEvent>
            {
                new MessageLogEvent
                {
                    Timestamp = DateTime.UtcNow,
                    Event = "created"
                }
            };
        }

        await _collection.InsertOneAsync(message);
        return message.Id;
    }

    public async Task<bool> UpdateStatusAsync(string id, MessageStatus status)
    {
        var update = Builders<ConversationMessage>.Update
            .Set(x => x.Status, status)
            .Push(x => x.Logs, new MessageLogEvent
            {
                Timestamp = DateTime.UtcNow,
                Event = status.ToString()
            });

        var result = await _collection.UpdateOneAsync(x => x.Id == id, update);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> AddMessageLogAsync(string id, MessageLogEvent logEvent)
    {
        var update = Builders<ConversationMessage>.Update
            .Push(x => x.Logs, logEvent)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(x => x.Id == id, update);
        return result.ModifiedCount > 0;
    }
}
