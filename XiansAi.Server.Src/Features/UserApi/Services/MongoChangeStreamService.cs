using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Repositories;
using Features.UserApi.Websocket;
using System.Text.Json;
using MongoDB.Driver.Linq;
using Shared.Services;


namespace Features.UserApi.Services
{
    public class MongoChangeStreamService : BackgroundService
    {
        private readonly IHubContext<ChatHub> _hubContext;
        private readonly IHubContext<TenantChatHub> _tenantHubContext;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<MongoChangeStreamService> _logger;
        private readonly IMessageEventPublisher _messageEventPublisher;
        private readonly ISecureEncryptionService _encryptionService;
        private readonly string _uniqueSecret;

        public MongoChangeStreamService(
            IHubContext<ChatHub> hubContext,
            IHubContext<TenantChatHub> tenantHubContext,
            IServiceScopeFactory scopeFactory,
            ILogger<MongoChangeStreamService> logger,
            IMessageEventPublisher messageEventPublisher,
            ISecureEncryptionService encryptionService,
            IConfiguration configuration
            )
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
            _hubContext = hubContext;
            _tenantHubContext = tenantHubContext;
            _messageEventPublisher = messageEventPublisher ?? throw new ArgumentNullException(nameof(messageEventPublisher));
            _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
            
            // Get the unique secret for conversation messages
            _uniqueSecret = configuration["EncryptionKeys:UniqueSecrets:ConversationMessageKey"] ?? string.Empty;
            if (string.IsNullOrWhiteSpace(_uniqueSecret))
            {
                _logger.LogWarning("EncryptionKeys:UniqueSecrets:ConversationMessageKey is not configured. Using the base secret value.");
                var baseSecret = configuration["EncryptionKeys:BaseSecret"];
                if (string.IsNullOrWhiteSpace(baseSecret))
                {
                    throw new InvalidOperationException("EncryptionKeys:BaseSecret is not configured");
                }
                _uniqueSecret = baseSecret;
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var databaseService = scope.ServiceProvider.GetRequiredService<IDatabaseService>();
                    var pendingRequestService = scope.ServiceProvider.GetRequiredService<IPendingRequestService>();

                    var database = await databaseService.GetDatabaseAsync();
                    var collectionName = "conversation_message";
                    var collection = database.GetCollection<ConversationMessage>(collectionName);

                    // Ensure collection exists
                    //var filter = Builders<BsonDocument>.Filter.Empty;
                    var collections = await database.ListCollectionNamesAsync(cancellationToken: stoppingToken);
                    var exists = await collections.ToListAsync(stoppingToken);
                    if (!exists.Contains(collectionName))
                    {
                        await database.CreateCollectionAsync(collectionName, cancellationToken: stoppingToken);
                    }

                    var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<ConversationMessage>>()
                       .Match(change =>
                                    change.OperationType == ChangeStreamOperationType.Insert ||
                                    change.OperationType == ChangeStreamOperationType.Update ||
                                    change.OperationType == ChangeStreamOperationType.Replace
                        )
                        .Project(change => new
                        {
                            Id = change.ResumeToken, // this is the resume token (_id)
                            FullDocument = change.FullDocument,
                            OperationType = change.OperationType
                        });
                    var options = new ChangeStreamOptions
                    {
                        FullDocument = ChangeStreamFullDocumentOption.UpdateLookup
                    };

                    using var cursor = await collection.WatchAsync(pipeline, options, cancellationToken: stoppingToken);
                    
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

                            // Convert BSON metadata to native .NET objects before sending via SignalR
                            ConvertBsonMetadataToObjectInternal(message);
                            
                            // Decrypt the message text
                            DecryptMessageText(message);

                            var groupId = message.WorkflowId + message.ParticipantId + message.TenantId;
                            var tenantGroupId = message.WorkflowId + message.TenantId;

                            // Check if this is a response to a pending synchronous request
                            if (message.Direction == MessageDirection.Outgoing && !string.IsNullOrEmpty(message.RequestId))
                            {
                                try
                                {
                                    _logger.LogDebug("Completing pending request {RequestId} with outgoing message {MessageId}", 
                                        message.RequestId, message.Id);
                                    pendingRequestService.CompleteRequest(message.RequestId, message, message.MessageType);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Error completing pending request {RequestId}", message.RequestId);
                                }
                            }

                            // Existing SignalR broadcasting logic (updated for proper handoff routing)
                            if (message.Direction == MessageDirection.Outgoing || message.MessageType == MessageType.Handoff)
                            {
                                // Route messages based on their type first, then content
                                if (message.MessageType == MessageType.Handoff)
                                {
                                    // Handoff messages get their own dedicated event
                                    _logger.LogDebug("Sending handoff message to group {GroupId}: {Message}",
                                        groupId, JsonSerializer.Serialize(message));
                                    await _hubContext.Clients.Group(groupId)
                                        .SendAsync("ReceiveHandoff", message, cancellationToken: stoppingToken);
                                    await _tenantHubContext.Clients.Group(tenantGroupId)
                                        .SendAsync("ReceiveHandoff", message, cancellationToken: stoppingToken);
                                }
                                else if (message.MessageType == MessageType.Data)
                                {
                                    // Data/Metadata messages (no text content)
                                    _logger.LogDebug("Sending metadata to group {GroupId}: {Message}",
                                        groupId, JsonSerializer.Serialize(message));
                                    await _hubContext.Clients.Group(groupId)
                                        // TODO: Remove the backward compatibility ReceiveMetadata later
                                        .SendAsync("ReceiveMetadata", message, cancellationToken: stoppingToken);
                                    await _hubContext.Clients.Group(groupId)
                                        // New method names
                                        .SendAsync("ReceiveData", message, cancellationToken: stoppingToken);
                                    await _tenantHubContext.Clients.Group(tenantGroupId)
                                        .SendAsync("ReceiveData", message, cancellationToken: stoppingToken);
                                }
                                else
                                {
                                    // Chat messages (with text content)
                                    _logger.LogDebug("Sending message to group {GroupId}: {Message}",
                                        groupId, JsonSerializer.Serialize(message));
                                    // TODO: Remove the backward compatibility ReceiveMessage later
                                    await _hubContext.Clients.Group(groupId)
                                        .SendAsync("ReceiveMessage", message, cancellationToken: stoppingToken);
                                    // New method names
                                    await _hubContext.Clients.Group(groupId)
                                        .SendAsync("ReceiveChat", message, cancellationToken: stoppingToken);
                                    await _tenantHubContext.Clients.Group(tenantGroupId)
                                        .SendAsync("ReceiveChat", message, cancellationToken: stoppingToken);
                                }

                                // Publish to SSE subscribers
                                try
                                {
                                    var messageEvent = new MessageStreamEvent
                                    {
                                        Message = message,
                                        GroupId = groupId,
                                        TenantGroupId = tenantGroupId
                                    };
                                    await _messageEventPublisher.PublishMessageAsync(messageEvent, stoppingToken);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning(ex, "Error publishing message event for SSE subscribers: {MessageId}", message.Id);
                                }
                            }
                            _logger.LogDebug("Sent message to group {GroupId}: {MessageId}",
                                message.WorkflowId + message.ParticipantId, message.Id);
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
            if (message.Data is BsonDocument bsonDoc)
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
                                message.Data = System.Text.Json.JsonSerializer.Deserialize<object>(strValue);
                                return;
                            }
                            catch(Exception ex)
                            {
                                _logger.LogWarning(ex, "MongoChangeStreamService: Failed to deserialize string metadata value for message {MessageId}. Using raw string.", message.Id);
                                message.Data = strValue;
                                return;
                            }
                        }
                        message.Data = strValue;
                        return;
                    }
                    message.Data = ConvertBsonToNativeObjectInternal(valueElement);
                    return;
                }
                message.Data = ConvertBsonToNativeObjectInternal(bsonDoc);
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

        private void DecryptMessageText(ConversationMessage message)
        {
            if (!string.IsNullOrEmpty(message.Text))
            {
                try
                {
                    // Try to decrypt
                    var messageSpecificSecret = $"{_uniqueSecret}";
                    var decryptedText = _encryptionService.Decrypt(message.Text, messageSpecificSecret);
                    message.Text = decryptedText;
                    _logger.LogInformation("Successfully decrypted message {MessageId}. Decrypted text: {Text}", message.Id, decryptedText);
                }
                catch (FormatException)
                {
                    // Not a valid Base64 string - this is plain text
                    _logger.LogDebug("Message {MessageId} is not encrypted (invalid Base64), treating as plain text", message.Id);
                    // Leave message.Text as-is
                }
                catch (System.Security.Cryptography.AuthenticationTagMismatchException)
                {
                    // This might be Base64 data that wasn't encrypted by our system
                    _logger.LogWarning("Message {MessageId} appears to be Base64 but decryption failed (authentication tag mismatch). This might be legacy data or corrupted encryption.", message.Id);
                    // Leave message.Text as-is
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error decrypting message {MessageId}. Text will remain as-is.", message.Id);
                    // Leave message.Text as-is
                }
            }
        }

    }
}
