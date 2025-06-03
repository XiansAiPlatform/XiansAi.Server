using Microsoft.AspNetCore.SignalR;
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
        private readonly ILogger<ConversationMessageRepository> _logger;

        public MongoChangeStreamService(
            IHubContext<ChatHub> hubContext,
            IServiceScopeFactory scopeFactory,
            ILogger<ConversationMessageRepository> logger
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
                    var collection = database.GetCollection<ConversationMessage>("conversation_message");

                    var pipeline = new EmptyPipelineDefinition<ChangeStreamDocument<ConversationMessage>>()
                        .Match(change =>
                        change.OperationType == ChangeStreamOperationType.Insert &&
                        change.FullDocument.Direction == MessageDirection.Outgoing
                         
                        );

                    using var cursor = await collection.WatchAsync(pipeline, cancellationToken: stoppingToken);

                    while (cursor.MoveNext() && cursor.Current.Count() == 0) { } // keep calling MoveNext until we've read the first batch
                    var next = cursor.Current.First();

                    // Convert the message to a clean JSON object before sending
                    var message = next.FullDocument;

                    await _hubContext.Clients.Group(message.WorkflowId + message.ParticipantId + message.TenantId)
                        .SendAsync("ReceiveMessage", message);

                    _logger.LogDebug("Sent message to group {GroupId}: {MessageId}", 
                        message.WorkflowId + message.ParticipantId, message.Id);                   
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in MongoChangeStreamService");

                }
            }
        }
    }
}
