using Microsoft.AspNetCore.SignalR;
using MongoDB.Bson;
using MongoDB.Driver;
using Shared.Data;
using Shared.Repositories;
using XiansAi.Server.Shared.Websocket;

namespace XiansAi.Server.Shared.Services
{
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
                    var database = await databaseService.GetDatabase();
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

                    using var cursor = await collection.WatchAsync(pipeline, options, stoppingToken);

                    _logger.LogInformation("Change stream started. Listening for outgoing messages...");

                    while (await cursor.MoveNextAsync(stoppingToken))
                    {
                        foreach (var change in cursor.Current)
                        {
                            if (change?.FullDocument == null)
                            {
                                _logger.LogWarning("Received a change with no full document. Skipping.");
                                continue;
                            }

                            var message = change.FullDocument;

                            try
                            {
                                if (message.Direction == MessageDirection.Outgoing)
                                {
                                    if (string.IsNullOrEmpty(message.Content))
                                    {
                                        await _hubContext.Clients.Group(message.WorkflowId + message.ParticipantId + message.TenantId)
                                        .SendAsync("ReceiveMetadata", message, stoppingToken);

                                        _logger.LogDebug("Sent message to group {GroupId}: {MessageId}",
                                            message.WorkflowId + message.ParticipantId + message.TenantId, message.Id);
                                    }
                                    else
                                    {
                                        await _hubContext.Clients.Group(message.WorkflowId + message.ParticipantId + message.TenantId)
                                        .SendAsync("ReceiveMessage", message, stoppingToken);

                                        _logger.LogDebug("Sent message to group {GroupId}: {MessageId}",
                                            message.WorkflowId + message.ParticipantId + message.TenantId, message.Id);
                                    }

                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to send message to SignalR group.");
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("MongoChangeStreamService is shutting down due to cancellation.");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in MongoChangeStreamService. Retrying in 5 seconds...");
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }
    }
}