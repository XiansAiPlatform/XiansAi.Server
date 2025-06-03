using Microsoft.AspNetCore.SignalR;
using MongoDB.Driver;
using Shared.Data;
using Shared.Repositories;
using XiansAi.Server.Shared.Websocket;
using MongoDB.Bson;
using System.Text.Json;
using MongoDB.Driver.Linq;
using System.Text.Json.Serialization;

namespace XiansAi.Server.Shared.Services
{
    // DTO for SignalR to handle metadata as a JSON string
    public class ConversationMessageSignalRDTO
    {
        public string Id { get; set; } = null!;
        public string ThreadId { get; set; } = null!;
        public string TenantId { get; set; } = null!;
        public DateTime CreatedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string CreatedBy { get; set; } = null!;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MessageDirection Direction { get; set; }
        public string? Content { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MessageStatus? Status { get; set; }
        public string? MetadataJson { get; set; } // Metadata as JSON string
        public List<MessageLogEvent>? Logs { get; set; }
        public string ParticipantId { get; set; } = null!;
        public string WorkflowId { get; set; } = null!;
        public string WorkflowType { get; set; } = null!;

        public static ConversationMessageSignalRDTO FromConversationMessage(ConversationMessage message, ILogger logger)
        {
            string? metadataJson = null;
            if (message.Metadata != null)
            {
                try
                {
                    metadataJson = System.Text.Json.JsonSerializer.Serialize(message.Metadata);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "MongoChangeStreamService: Could not serialize metadata for message {MessageId}. Sending null for MetadataJson.", message.Id);
                    metadataJson = null;
                }
            }

            return new ConversationMessageSignalRDTO
            {
                Id = message.Id,
                ThreadId = message.ThreadId,
                TenantId = message.TenantId,
                CreatedAt = message.CreatedAt,
                UpdatedAt = message.UpdatedAt,
                CreatedBy = message.CreatedBy,
                Direction = message.Direction,
                Content = message.Content,
                Status = message.Status,
                MetadataJson = metadataJson,
                Logs = message.Logs,
                ParticipantId = message.ParticipantId,
                WorkflowId = message.WorkflowId,
                WorkflowType = message.WorkflowType
            };
        }
    }

    public class MongoChangeStreamService : BackgroundService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MongoChangeStreamService> _logger;

        public MongoChangeStreamService(
            IHubContext<ChatHub> hubContext,
            IServiceScopeFactory scopeFactory,
            ILogger<MongoChangeStreamService> logger
            )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                    var conversationMessageRepository = scope.ServiceProvider.GetRequiredService<IConversationMessageRepository>();

                    var database = await databaseService.GetDatabase();
                    var collection = database.GetCollection<ConversationMessage>("conversation_message");

                    var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<ConversationMessage>>()
                        .Match(change =>
                        change.OperationType == ChangeStreamOperationType.Insert &&
                        change.FullDocument.Direction == MessageDirection.Outgoing
                        );

                    using var cursor = await collection.WatchAsync(pipeline, cancellationToken: stoppingToken);
                    
                    // Iterate using MoveNextAsync and Current
                    while (await cursor.MoveNextAsync(stoppingToken))
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        var changeBatch = cursor.Current;
                        if (changeBatch == null) continue;

                        foreach(var changeDoc in changeBatch) 
                        {
                            if (stoppingToken.IsCancellationRequested) break;

                            var message = changeDoc.FullDocument;
                            if (message == null) continue;

                            ConvertBsonMetadataToObjectInternal(message);

                            var messageDTO = ConversationMessageSignalRDTO.FromConversationMessage(message, _logger);

                            if (string.IsNullOrEmpty(messageDTO.Content))
                            {
                                await _hubContext.Clients.Group(messageDTO.WorkflowId + messageDTO.ParticipantId + messageDTO.TenantId)
                                .SendAsync("ReceiveMetadata", messageDTO, cancellationToken: stoppingToken);
                            }
                            else
                            {
                                await _hubContext.Clients.Group(messageDTO.WorkflowId + messageDTO.ParticipantId + messageDTO.TenantId)
                                    .SendAsync("ReceiveMessage", messageDTO, cancellationToken: stoppingToken);
                            }

                            _logger.LogDebug("Sent DTO message to group {GroupId}: {MessageId}",
                                messageDTO.WorkflowId + messageDTO.ParticipantId, messageDTO.Id);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("MongoChangeStreamService is stopping.");
                    break; 
                }
                catch (MongoException ex) when (ex.HasErrorLabel("TransientTransactionError") || ex is MongoConnectionException || ex is MongoNotPrimaryException || ex is MongoNodeIsRecoveringException)
                {
                    _logger.LogWarning(ex, "MongoChangeStreamService: Transient MongoDB error. Retrying in 5 seconds...");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "MongoChangeStreamService: Unhandled exception. Restarting watch in 10 seconds.");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }

        private void ConvertBsonMetadataToObjectInternal(ConversationMessage message)
        {
            if (message.Metadata is BsonDocument bsonDoc)
            {
                if (bsonDoc.Contains("value") && bsonDoc.ElementCount == 1)
                {
                    var valueElement = bsonDoc["value"];
                    if (valueElement.IsString)
                    {
                        string strValue = valueElement.AsString;
                        if ((strValue.StartsWith("{") && strValue.EndsWith("}")) ||
                            (strValue.StartsWith("[") && strValue.EndsWith("]")))
                        {
                            try
                            {
                                message.Metadata = System.Text.Json.JsonSerializer.Deserialize<object>(strValue);
                                return;
                            }
                            catch(Exception ex)
                            {
                                _logger.LogWarning(ex, "MongoChangeStreamService: Failed to deserialize string metadata value for message {MessageId}. Using raw string.", message.Id);
                                message.Metadata = strValue;
                                return;
                            }
                        }
                        message.Metadata = strValue;
                        return;
                    }
                    message.Metadata = ConvertBsonToNativeObjectInternal(valueElement);
                    return;
                }
                message.Metadata = ConvertBsonToNativeObjectInternal(bsonDoc);
            }
        }

        private object? ConvertBsonToNativeObjectInternal(BsonValue bsonValue)
        {
            switch (bsonValue.BsonType)
            {
                case BsonType.Document:
                    var doc = bsonValue.AsBsonDocument;
                    var dict = new Dictionary<string, object?>();
                    foreach (var element in doc.Elements)
                    {
                        dict[element.Name] = ConvertBsonToNativeObjectInternal(element.Value);
                    }
                    return dict;
                case BsonType.Array:
                    return bsonValue.AsBsonArray.Select(ConvertBsonToNativeObjectInternal).ToList();
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
                    return (decimal)bsonValue.AsDecimal128;
                case BsonType.DateTime:
                    return bsonValue.ToUniversalTime();
                case BsonType.Null:
                case BsonType.Undefined:
                    return null;
                case BsonType.ObjectId:
                    return bsonValue.AsObjectId.ToString();
                default:
                    _logger.LogWarning("MongoChangeStreamService: Unhandled BSON type {BsonType} in metadata conversion. Converting to string.", bsonValue.BsonType);
                    return bsonValue.ToString();
            }
        }
    }
}
