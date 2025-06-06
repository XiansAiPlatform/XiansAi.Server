using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Bson;
using System.Text.Json.Serialization;
using MongoDB.Driver;
using Shared.Repositories;
using Shared.Data;
using Polly.CircuitBreaker;

namespace Shared.Repositories
{
    public interface IConversationChangeListener
    {
        Task<ConversationMessage?> GetLatestConversationMessage(string tenantId, string threadId, string agent, string workflowType, string participantId, string workflowId);
    }

    public class ConversationChangeListener : IConversationChangeListener
    {
        private readonly IMongoCollection<ConversationMessage> _collection;
        private readonly IMongoCollection<ConversationThread> _threadCollection;
        private readonly IMongoDatabase _database;
        private readonly ILogger<ConversationMessageRepository> _logger;
     
        public ConversationChangeListener(
            IDatabaseService databaseService,
        ILogger<ConversationMessageRepository> logger) 
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            var database = databaseService.GetDatabase().GetAwaiter().GetResult();
            _database = database;
            _collection = database.GetCollection<ConversationMessage>("conversation_message");
            _threadCollection = database.GetCollection<ConversationThread>("conversation_thread");
            // Create indexes
            CreateIndexes();
        }

        private void CreateIndexes()
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
            _collection.Indexes.CreateMany(new[] {
            messageLookupIndexModel,
            channelLookupIndexModel,
            messageStatusIndexModel
            });
        }

        public async Task<ConversationMessage?> GetLatestConversationMessage(string tenantId, string threadId, string agent, string workflowType, string participantId, string workflowId)
        {
            try
            {
                _logger.LogInformation("Received request with parameters: TenantId={TenantId}, ThreadId={ThreadId}, Agent={Agent}, WorkflowType={WorkflowType}, ParticipantId={ParticipantId}, WorkflowId={WorkflowId}",
    tenantId, threadId, agent, workflowType, participantId, workflowId);
                var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<ConversationMessage>>()
                    .Match(change => 
                        change.OperationType == ChangeStreamOperationType.Insert &&
                        change.FullDocument.TenantId == tenantId &&
                        change.FullDocument.ThreadId == threadId &&
                        change.FullDocument.ParticipantId == participantId &&
                        change.FullDocument.WorkflowType == workflowType &&
                        change.FullDocument.WorkflowId == workflowId &&
                        change.FullDocument.Direction == MessageDirection.Outgoing
                    );

                using var cursor = await _collection.WatchAsync(pipeline);

                while (await cursor.MoveNextAsync() && cursor.Current.Count() == 0)
                {
                    // Continue waiting for changes
                    _logger.LogDebug("Waiting for matching message...");
                }
               
                if (cursor.Current.Any())
                {
                    var change = cursor.Current.First();
                    _logger.LogInformation("Found matching message in change stream");
                    var message = change.FullDocument;
                    
                    // Convert BSON metadata to native .NET types for proper JSON serialization
                    ConvertBsonMetadataToObject(message);
                    
                    return message;
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetLatestConversationMessage for tenant: {TenantId}, thread: {ThreadId}", tenantId, threadId);
                throw;
            }
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
                                message.Data = System.Text.Json.JsonSerializer.Deserialize<object>(strValue);
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
    }
}
