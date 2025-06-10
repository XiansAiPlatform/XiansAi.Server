using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using System.Text.Json;
using Shared.Data;
using Shared.Utils;


namespace Shared.Repositories;

public enum MessageDirection
{
    Incoming,
    Outgoing,
    [Obsolete("Use MessageDirection.Handoff instead")]
    Handover
}

public enum MessageStatus
{
    FailedToDeliverToWorkflow,
    DeliveredToWorkflow,
}


public enum MessageType
{
    Chat,
    Data,
    Handoff,
    [Obsolete("Use MessageType.Handoff instead")]
    Handover
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

[BsonIgnoreExtraElements]
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

    [BsonElement("text")]
    public string? Text { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageStatus? Status { get; set; }

    [BsonElement("data")]
    public object? Data { get; set; }

    [BsonElement("participant_id")]
    public required string ParticipantId { get; set; }

    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }

    [BsonElement("workflow_type")]
    public required string WorkflowType { get; set; }

    [BsonElement("message_type")]
    [BsonRepresentation(BsonType.String)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageType? MessageType { get; set; }

}

public interface IConversationMessageRepository
{
    Task<ConversationMessage?> GetByIdAsync(string id);
    Task<List<ConversationMessage>> GetByStatusAsync(string tenantId, MessageStatus status, int? page = null, int? pageSize = null);
    Task<string> CreateAsync(ConversationMessage message);
    Task<List<string>> CreateManyAsync(List<ConversationMessage> messages);
    Task<string> CreateAndUpdateThreadAsync(ConversationMessage message, string threadId, DateTime timestamp);
    Task<List<string>> CreateManyAndUpdateThreadsAsync(List<ConversationMessage> messages, Dictionary<string, DateTime> threadTimestamps);
    Task<bool> UpdateStatusAsync(string id, MessageStatus status);
    Task<List<ConversationMessage>> GetByThreadIdAsync(string tenantId, string threadId, int? page = null, int? pageSize = null);
    Task<List<ConversationMessage>> GetByAgentAndParticipantAsync(string tenantId, string workflowType, string participantId, int? page = null, int? pageSize = null, bool includeMetadata = false);
    Task<string> CreateAndUpdateThreadWithOptimisticConcurrencyAsync(ConversationMessage message, string threadId, DateTime timestamp);
}

public class ConversationMessageRepository : IConversationMessageRepository
{
    private readonly IMongoCollection<ConversationMessage> _collection;
    private readonly IMongoCollection<ConversationThread> _threadCollection;
    private readonly IMongoDatabase _database;
    private readonly ILogger<ConversationMessageRepository> _logger;
    private readonly Lazy<Task> _indexCreationTask;
    private volatile bool _indexesCreated = false;
    
    public ConversationMessageRepository(
        IDatabaseService databaseService,
        ILogger<ConversationMessageRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _database = database;
        _collection = database.GetCollection<ConversationMessage>("conversation_message");
        _threadCollection = database.GetCollection<ConversationThread>("conversation_thread");

        // Initialize indexes asynchronously without blocking constructor
        _indexCreationTask = new Lazy<Task>(() => InitializeIndexesAsync());
        
        // Start index creation in background (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _indexCreationTask.Value;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background index creation failed during repository initialization");
            }
        });
    }

    /// <summary>
    /// Initializes indexes asynchronously with resilient error handling.
    /// This method won't throw exceptions and allows the application to continue running even if MongoDB is temporarily unavailable.
    /// </summary>
    private async Task InitializeIndexesAsync()
    {
        if (_indexesCreated) return;

        var success = await MongoRetryHelper.ExecuteWithGracefulRetryAsync(async () =>
        {
            // Message lookup index (tenant_id, thread_id, created_at)
            var messageLookupIndex = Builders<ConversationMessage>.IndexKeys
                .Ascending(x => x.TenantId)
                .Ascending(x => x.ThreadId)
                .Ascending(x => x.ParticipantId)
                .Descending(x => x.CreatedAt);
            
            var messageLookupIndexModel = new CreateIndexModel<ConversationMessage>(
                messageLookupIndex, 
                new CreateIndexOptions { Background = true, Name = "thread_participant_message_lookup" }
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
            await _collection.Indexes.CreateManyAsync(new[] { 
                messageLookupIndexModel,
                channelLookupIndexModel,
                messageStatusIndexModel
            });

        }, _logger, maxRetries: 2, baseDelayMs: 2000, operationName: "CreateIndexes");

        if (success)
        {
            _indexesCreated = true;
            _logger.LogInformation("Successfully created indexes for ConversationMessage collection");
        }
        else
        {
            _logger.LogWarning("Failed to create indexes for ConversationMessage collection, but repository will continue to function. Indexes will be retried on next operation.");
        }
    }

    /// <summary>
    /// Ensures indexes are created before performing operations (lazy initialization).
    /// This method is called by repository methods that benefit from having indexes.
    /// </summary>
    private async Task EnsureIndexesAsync()
    {
        if (!_indexesCreated)
        {
            try
            {
                await _indexCreationTask.Value;
            }
            catch (Exception ex)
            {
                // Log but don't throw - the operation can continue without indexes
                _logger.LogDebug(ex, "Index creation not yet complete, continuing with operation");
            }
        }
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
        // Ensure indexes are created for optimal query performance
        await EnsureIndexesAsync();
        
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
        // Ensure indexes are created for optimal query performance
        await EnsureIndexesAsync();
        
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
        // Convert metadata to BsonDocument if needed
        if (message.Data != null)
        {
            message.Data = ConvertToBsonDocument(message.Data);
        }

        await _collection.InsertOneAsync(message);
        return message.Id;
    }

    public async Task<List<string>> CreateManyAsync(List<ConversationMessage> messages)
    {
        if (messages == null || !messages.Any())
        {
            return new List<string>();
        }

        // Prepare each message for insertion
        foreach (var message in messages)
        {
            // Convert metadata to BsonDocument if needed
            if (message.Data != null)
            {
                message.Data = ConvertToBsonDocument(message.Data);
            }
        }

        // Insert all messages in a single operation
        await _collection.InsertManyAsync(messages);
        
        // Return all message IDs
        return messages.Select(m => m.Id).ToList();
    }

    public async Task<string> CreateAndUpdateThreadAsync(ConversationMessage message, string threadId, DateTime timestamp)
    {
        // Convert metadata to BsonDocument if needed
        if (message.Data != null)
        {
            message.Data = ConvertToBsonDocument(message.Data);
        }

        // Critical operation: Create the message with retry logic
        var messageId = await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            await _collection.InsertOneAsync(message);
            return message.Id;
        }, _logger, operationName: "CreateMessage");

        // Best effort: Update thread timestamp (non-critical)
        // Use fire-and-forget pattern to avoid blocking the message creation result
        _ = Task.Run(async () =>
        {
            try
            {
                await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
                {
                    var threadFilter = Builders<ConversationThread>.Filter.Eq(t => t.Id, threadId);
                    var threadUpdate = Builders<ConversationThread>.Update.Set(t => t.UpdatedAt, timestamp);
                    var result = await _threadCollection.UpdateOneAsync(threadFilter, threadUpdate);
                    
                    if (result.MatchedCount == 0)
                    {
                        _logger.LogDebug("Thread {ThreadId} not found during timestamp update", threadId);
                    }
                }, _logger, maxRetries: 2, operationName: "UpdateThreadTimestamp");
            }
            catch (Exception ex)
            {
                // Log but don't throw - this is non-critical
                _logger.LogWarning(ex, "Failed to update thread {ThreadId} timestamp after message creation, but message {MessageId} was created successfully", 
                    threadId, messageId);
            }
        });

        return messageId;
    }

    public async Task<List<string>> CreateManyAndUpdateThreadsAsync(List<ConversationMessage> messages, Dictionary<string, DateTime> threadTimestamps)
    {
        if (messages == null || !messages.Any())
        {
            return new List<string>();
        }

        // Prepare each message for insertion
        foreach (var message in messages)
        {
            // Convert metadata to BsonDocument if needed
            if (message.Data != null)
            {
                message.Data = ConvertToBsonDocument(message.Data);
            }
        }

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Use a session to perform all operations in a transaction
            using var session = await _database.Client.StartSessionAsync();
            
            session.StartTransaction();

            try
            {
                // Insert all messages in a single operation
                await _collection.InsertManyAsync(session, messages);
                var messageIds = messages.Select(m => m.Id).ToList();

                // Update all thread timestamps
                foreach (var threadUpdate in threadTimestamps)
                {
                    var filter = Builders<ConversationThread>.Filter.Eq(t => t.Id, threadUpdate.Key);
                    var update = Builders<ConversationThread>.Update.Set(t => t.UpdatedAt, threadUpdate.Value);
                    await _threadCollection.UpdateOneAsync(session, filter, update);
                }

                // Commit the transaction
                await session.CommitTransactionAsync();
                return messageIds;
            }
            catch
            {
                await session.AbortTransactionAsync();
                throw;
            }
        }, _logger, operationName: "CreateManyAndUpdateThreadsAsync");
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
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var update = Builders<ConversationMessage>.Update
                .Set(x => x.Status, status);

            var result = await _collection.UpdateOneAsync(x => x.Id == id, update);
            return result.ModifiedCount > 0;
        }, _logger, operationName: "UpdateStatusAsync");
    }

    // Helper method to convert BsonDocument metadata back to the original object format
    private void ConvertBsonMetadataToObject(ConversationMessage message)
    {
        if (message.Data is BsonDocument bsonDoc)
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
                            message.Data = JsonSerializer.Deserialize<object>(strValue);
                            return;
                        }
                        catch
                        {
                            // If parsing fails, just use the string value
                            message.Data = strValue;
                            return;
                        }
                    }
                    
                    // It's just a string
                    message.Data = strValue;
                    return;
                }
            }
            
            // Convert BsonDocument to native .NET types properly
            message.Data = ConvertBsonToNativeObject(bsonDoc);
        }
    }

    // Helper method to convert BSON types to native .NET types for proper JSON serialization
    private object? ConvertBsonToNativeObject(BsonValue bsonValue)
    {
        switch (bsonValue.BsonType)
        {
            case BsonType.Document:
                var doc = bsonValue.AsBsonDocument;
                var dict = new Dictionary<string, object?>();
                foreach (var element in doc)
                {
                    dict[element.Name] = ConvertBsonToNativeObject(element.Value);
                }
                return dict;
                
            case BsonType.Array:
                var array = bsonValue.AsBsonArray;
                return array.Select(ConvertBsonToNativeObject).ToList();
                
            case BsonType.String:
                return bsonValue.AsString;
                
            case BsonType.Boolean:
                return bsonValue.AsBoolean;
                
            case BsonType.Int32:
                return bsonValue.AsInt32;
                
            case BsonType.Int64:
                return bsonValue.AsInt64;
                
            case BsonType.Double:
                return bsonValue.AsDouble;
                
            case BsonType.Decimal128:
                return bsonValue.AsDecimal;
                
            case BsonType.DateTime:
                return bsonValue.ToUniversalTime();
                
            case BsonType.Null:
            case BsonType.Undefined:
                return null;
                
            default:
                // For any other types, convert to string as fallback
                return bsonValue.ToString();
        }
    }

    public async Task<List<ConversationMessage>> GetByAgentAndParticipantAsync(string tenantId,string workflowType, string participantId, int? page = null, int? pageSize = null, bool includeMetadata = false)
    {
        // Ensure indexes are created for optimal query performance
        await EnsureIndexesAsync();
        
        _logger.LogDebug("Querying messages directly by participantId {ParticipantId}", participantId);
        
        // First, get the thread ID for the given workflow and participant
        var threadFilter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowType, workflowType),
            Builders<ConversationThread>.Filter.Eq(x => x.ParticipantId, participantId)
        );
        
        var threadProjection = Builders<ConversationThread>.Projection.Include(x => x.Id);
        var threadResult = await _threadCollection.Find(threadFilter)
                                                 .Project<BsonDocument>(threadProjection)
                                                 .FirstOrDefaultAsync();

        // If no thread exists, return empty list
        if (threadResult == null)
        {
            _logger.LogInformation("No thread found for workflowType {WorkflowType} and participantId {ParticipantId}", workflowType, participantId);
            return new List<ConversationMessage>();
        }

        string threadId = threadResult["_id"].AsObjectId.ToString();
        
        // Build message filter
        var messageFilter = Builders<ConversationMessage>.Filter.And(
            Builders<ConversationMessage>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationMessage>.Filter.Eq(x => x.ThreadId, threadId)
        );

        // If includeMetadata is false, filter out messages with null or empty content
        if (!includeMetadata)
        {
            var contentFilter = Builders<ConversationMessage>.Filter.And(
                Builders<ConversationMessage>.Filter.Ne(x => x.Text, null),
                Builders<ConversationMessage>.Filter.Ne(x => x.Text, "")
            );
            messageFilter = Builders<ConversationMessage>.Filter.And(messageFilter, contentFilter);
        }

        var query = _collection.Find(messageFilter).Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt));

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

    /// <summary>
    /// Alternative approach using optimistic concurrency control
    /// </summary>
    public async Task<string> CreateAndUpdateThreadWithOptimisticConcurrencyAsync(ConversationMessage message, string threadId, DateTime timestamp)
    {
        // Convert metadata to BsonDocument if needed
        if (message.Data != null)
        {
            message.Data = ConvertToBsonDocument(message.Data);
        }

        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // First, get the current thread to check its current UpdatedAt
            var currentThread = await _threadCollection.Find(t => t.Id == threadId)
                .Project(t => new { t.Id, t.UpdatedAt })
                .FirstOrDefaultAsync();
            
            if (currentThread == null)
            {
                _logger.LogWarning("Thread {ThreadId} not found during message creation", threadId);
                // Still create the message even if thread doesn't exist
                await _collection.InsertOneAsync(message);
                return message.Id;
            }

            // Create the message first
            await _collection.InsertOneAsync(message);
            
            // Update thread only if UpdatedAt hasn't changed (optimistic concurrency)
            var threadFilter = Builders<ConversationThread>.Filter.And(
                Builders<ConversationThread>.Filter.Eq(t => t.Id, threadId),
                Builders<ConversationThread>.Filter.Eq(t => t.UpdatedAt, currentThread.UpdatedAt)
            );
            var threadUpdate = Builders<ConversationThread>.Update.Set(t => t.UpdatedAt, timestamp);
            var threadResult = await _threadCollection.UpdateOneAsync(threadFilter, threadUpdate);
            
            if (threadResult.MatchedCount == 0)
            {
                _logger.LogDebug("Thread {ThreadId} was modified by another operation, timestamp update skipped", threadId);
            }

            return message.Id;
        }, _logger, operationName: "CreateAndUpdateThreadWithOptimisticConcurrencyAsync");
    }
}
