using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using XiansAi.Server.Shared.Data;
using System.Text.Json.Serialization;
using System.Text.Json;

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

    [BsonElement("thread_id")]
    public required string ThreadId { get; set; }

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

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
    public object? Metadata { get; set; }

    [BsonElement("logs")]
    public List<MessageLogEvent>? Logs { get; set; }

    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }

    [BsonElement("participant_id")]
    public required string ParticipantId { get; set; }

    [BsonElement("handed_over_to")]
    public string? HandedOverTo { get; set; }

    [BsonElement("handed_over_by")]
    public string? HandedOverBy { get; set; }
}

public interface IConversationMessageRepository
{
    Task<ConversationMessage?> GetByIdAsync(string id);
    Task<List<ConversationMessage>> GetByStatusAsync(string tenantId, MessageStatus status, int? page = null, int? pageSize = null);
    Task<string> CreateAsync(ConversationMessage message);
    Task<bool> UpdateStatusAsync(string id, MessageStatus status);
    Task<bool> AddMessageLogAsync(string id, MessageLogEvent logEvent);

    Task<List<ConversationMessage>> GetByThreadIdAsync(string tenantId, string threadId, int? page = null, int? pageSize = null);
}

public class ConversationMessageRepository : IConversationMessageRepository
{
    private readonly IMongoCollection<ConversationMessage> _collection;
    private readonly ILogger<ConversationMessageRepository> _logger;
    
    public ConversationMessageRepository(
        IDatabaseService databaseService,
        ILogger<ConversationMessageRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            .Ascending(x => x.WorkflowId)
            .Ascending(x => x.ParticipantId)
            .Descending(x => x.CreatedAt);
        
        var messageLookupIndexModel = new CreateIndexModel<ConversationMessage>(
            messageLookupIndex, 
            new CreateIndexOptions { Background = true, Name = "workflow_participant_message_lookup" }
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
        var message = await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
        if (message != null)
        {
            ConvertBsonMetadataToObject(message);
        }
        return message;
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

        var messages = await query.ToListAsync();
        foreach (var message in messages)
        {
            ConvertBsonMetadataToObject(message);
        }
        return messages;
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

        var messages = await query.ToListAsync();
        foreach (var message in messages)
        {
            ConvertBsonMetadataToObject(message);
        }
        return messages;
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

        var messages = await query.ToListAsync();
        foreach (var message in messages)
        {
            ConvertBsonMetadataToObject(message);
        }
        return messages;
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

        // Convert metadata to BsonDocument if needed
        if (message.Metadata != null)
        {
            message.Metadata = ConvertToBsonDocument(message.Metadata);
        }

        await _collection.InsertOneAsync(message);
        return message.Id;
    }

    // Helper method to convert any object to BsonDocument
    public BsonDocument? ConvertToBsonDocument(object? obj)
    {
        if (obj == null) return null;
        
        // If it's already a BsonDocument, just return it
        if (obj is BsonDocument bsonDoc)
        {
            return bsonDoc;
        }
        
        // If the object is already a string, ensure it's a valid JSON object
        if (obj is string stringValue)
        {
            // If it looks like a JSON object, parse it directly
            if ((stringValue.StartsWith("{") && stringValue.EndsWith("}")) ||
                (stringValue.StartsWith("[") && stringValue.EndsWith("]")))
            {
                try 
                {
                    return BsonDocument.Parse(stringValue);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse string as JSON. Storing as simple string value.");
                    // If parsing fails, wrap it in an object
                    return new BsonDocument("value", stringValue);
                }
            }
            
            // If it's just a string, wrap it in a document
            return new BsonDocument("value", stringValue);
        }
        
        try 
        {
            // If it's a JsonElement, handle it specially
            if (obj is JsonElement jsonElement)
            {
                return ConvertJsonElementToBson(jsonElement);
            }
            
            // Convert the object to JSON, then to BsonDocument
            var json = JsonSerializer.Serialize(obj);
            return BsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to convert object to BsonDocument. Storing as string representation.");
            // If parsing fails, create a simpler BsonDocument with the string representation
            return new BsonDocument("value", obj.ToString() ?? "");
        }
    }
    
    // Helper method to convert JsonElement to BsonDocument
    private BsonDocument ConvertJsonElementToBson(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new BsonDocument("value", element.GetRawText());
        }
        
        var document = new BsonDocument();
        foreach (var property in element.EnumerateObject())
        {
            document[property.Name] = ConvertJsonElementToBsonValue(property.Value);
        }
        return document;
    }
    
    // Helper method to convert JsonElement to BsonValue
    private BsonValue ConvertJsonElementToBsonValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                return ConvertJsonElementToBson(element);
            case JsonValueKind.Array:
                var array = new BsonArray();
                foreach (var item in element.EnumerateArray())
                {
                    array.Add(ConvertJsonElementToBsonValue(item));
                }
                return array;
            case JsonValueKind.String:
                return new BsonString(element.GetString() ?? string.Empty);
            case JsonValueKind.Number:
                if (element.TryGetInt32(out int intValue))
                    return new BsonInt32(intValue);
                if (element.TryGetInt64(out long longValue))
                    return new BsonInt64(longValue);
                if (element.TryGetDecimal(out decimal decimalValue))
                    return new BsonDecimal128(decimalValue);
                return new BsonDouble(element.GetDouble());
            case JsonValueKind.True:
                return BsonBoolean.True;
            case JsonValueKind.False:
                return BsonBoolean.False;
            case JsonValueKind.Null:
                return BsonNull.Value;
            default:
                return BsonNull.Value;
        }
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

    // Helper method to convert BsonDocument metadata back to the original object format
    private void ConvertBsonMetadataToObject(ConversationMessage message)
    {
        if (message.Metadata is BsonDocument bsonDoc)
        {
            // If it's a simple wrapper with a "value" field, extract the value
            if (bsonDoc.Contains("value") && bsonDoc.ElementCount == 1)
            {
                var valueElement = bsonDoc["value"];
                if (valueElement.IsString)
                {
                    // Try to deserialize if it looks like JSON
                    string strValue = valueElement.AsString;
                    if ((strValue.StartsWith("{") && strValue.EndsWith("}")) ||
                        (strValue.StartsWith("[") && strValue.EndsWith("]")))
                    {
                        try
                        {
                            message.Metadata = JsonSerializer.Deserialize<object>(strValue);
                            return;
                        }
                        catch
                        {
                            // If parsing fails, just use the string value
                            message.Metadata = strValue;
                            return;
                        }
                    }
                    
                    // It's just a string
                    message.Metadata = strValue;
                    return;
                }
            }
            
            // Convert the BsonDocument to a string representation then deserialize
            string json = bsonDoc.ToJson();
            try
            {
                message.Metadata = JsonSerializer.Deserialize<object>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                // If all else fails, keep the original metadata
            }
        }
    }

}
