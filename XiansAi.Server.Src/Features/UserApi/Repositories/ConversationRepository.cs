using MongoDB.Driver;
using MongoDB.Bson;
using Shared.Data;
using Shared.Repositories;
using Shared.Utils;
using Shared.Auth;

namespace Features.UserApi.Repositories;

/// <summary>
/// High-performance conversation repository optimized for bot interactions
/// Combines thread and message operations to reduce database round trips
/// </summary>
public interface IConversationRepository
{
    Task<ConversationThreadInfo> CreateOrGetThreadAsync(string workflowId, string participantId);
    Task<string> SaveMessageAsync(MessageRequest message);
    Task<List<ConversationMessage>> GetMessagesAsync(string workflowId, string participantId, int page, int pageSize, string? scope = null);
    Task<ConversationThreadInfo?> GetThreadInfoAsync(string workflowId, string participantId);
}

/// <summary>
/// Optimized request structure for message saving
/// </summary>
public class MessageRequest
{
    public required string TenantId { get; set; }
    public required string ThreadId { get; set; }
    public required string ParticipantId { get; set; }
    public required string WorkflowId { get; set; }
    public required string WorkflowType { get; set; }
    public required string CreatedBy { get; set; }
    public required MessageDirection Direction { get; set; }
    public required MessageType MessageType { get; set; }
    public string? RequestId { get; set; }
    public string? Text { get; set; }
    public object? Data { get; set; }
    public string? Hint { get; set; }
    public string? Scope { get; set; }
}

/// <summary>
/// Optimized thread information structure
/// </summary>
public class ConversationThreadInfo
{
    public required string Id { get; set; }
    public required string TenantId { get; set; }
    public required string WorkflowId { get; set; }
    public required string WorkflowType { get; set; }
    public required string ParticipantId { get; set; }
    public required DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public bool IsNew { get; set; }
}

public class ConversationRepository : IConversationRepository
{
    private readonly IMongoCollection<ConversationMessage> _messagesCollection;
    private readonly IMongoCollection<ConversationThread> _threadsCollection;
    private readonly ILogger<ConversationRepository> _logger;
    private readonly ITenantContext _tenantContext;

    public ConversationRepository(IDatabaseService databaseService, ILogger<ConversationRepository> logger, ITenantContext tenantContext)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _messagesCollection = database.GetCollection<ConversationMessage>("conversation_message");
        _threadsCollection = database.GetCollection<ConversationThread>("conversation_thread");
        _tenantContext = tenantContext;
    }

    public async Task<ConversationThreadInfo> CreateOrGetThreadAsync(string workflowId, string participantId)
    {
        // Fast lookup using optimized composite index
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, _tenantContext.TenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowId, workflowId),
            Builders<ConversationThread>.Filter.Eq(x => x.ParticipantId, participantId)
        );

        // Project only required fields to reduce memory usage
        var projection = Builders<ConversationThread>.Projection
            .Include(x => x.Id)
            .Include(x => x.TenantId)
            .Include(x => x.WorkflowId)
            .Include(x => x.WorkflowType)
            .Include(x => x.ParticipantId)
            .Include(x => x.CreatedAt)
            .Include(x => x.UpdatedAt);

        var existingThread = await _threadsCollection
            .Find(filter)
            .Project<ConversationThread>(projection)
            .FirstOrDefaultAsync();

        if (existingThread != null)
        {
            return new ConversationThreadInfo
            {
                Id = existingThread.Id,
                TenantId = existingThread.TenantId,
                WorkflowId = existingThread.WorkflowId,
                WorkflowType = existingThread.WorkflowType,
                ParticipantId = existingThread.ParticipantId,
                CreatedAt = existingThread.CreatedAt,
                UpdatedAt = existingThread.UpdatedAt,
                IsNew = false
            };
        }

        var workflow = new WorkflowIdentifier(workflowId, _tenantContext);

        var workflowType = workflow.WorkflowType;

        var newThread = new ConversationThread
        {
            TenantId = _tenantContext.TenantId,
            WorkflowId = workflowId,
            WorkflowType = workflowType,
            Agent = workflow.AgentName,
            ParticipantId = participantId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = _tenantContext.LoggedInUser,
            Status = ConversationThreadStatus.Active
        };

        try
        {
            await _threadsCollection.InsertOneAsync(newThread);
            
            return new ConversationThreadInfo
            {
                Id = newThread.Id,
                TenantId = newThread.TenantId,
                WorkflowId = newThread.WorkflowId,
                WorkflowType = newThread.WorkflowType,
                ParticipantId = newThread.ParticipantId,
                CreatedAt = newThread.CreatedAt,
                UpdatedAt = newThread.UpdatedAt,
                IsNew = true
            };
        }
        catch (MongoBulkWriteException ex) when (ex.WriteErrors.Any(e => e.Code == 11000))
        {
            // Handle race condition - another thread created it
            var raceConditionThread = await _threadsCollection
                .Find(filter)
                .Project<ConversationThread>(projection)
                .FirstOrDefaultAsync();

            if (raceConditionThread != null)
            {
                return new ConversationThreadInfo
                {
                    Id = raceConditionThread.Id,
                    TenantId = raceConditionThread.TenantId,
                    WorkflowId = raceConditionThread.WorkflowId,
                    WorkflowType = raceConditionThread.WorkflowType,
                    ParticipantId = raceConditionThread.ParticipantId,
                    CreatedAt = raceConditionThread.CreatedAt,
                    UpdatedAt = raceConditionThread.UpdatedAt,
                    IsNew = false
                };
            }
            throw;
        }
    }

    public async Task<string> SaveMessageAsync(MessageRequest request)
    {
        var now = DateTime.UtcNow;
        
        var message = new ConversationMessage
        {
            ThreadId = request.ThreadId,
            ParticipantId = request.ParticipantId,
            TenantId = request.TenantId,
            RequestId = request.RequestId,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = request.CreatedBy,
            Direction = request.Direction,
            Text = request.Text,
            Data = ConvertToBsonDocument(request.Data),
            WorkflowId = request.WorkflowId,
            WorkflowType = request.WorkflowType,
            MessageType = request.MessageType,
            Hint = request.Hint,
            Scope = request.Scope
        };

        // Use MongoDB's atomic operations for optimal performance
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var session = await _messagesCollection.Database.Client.StartSessionAsync();
            try
            {
                await session.WithTransactionAsync(async (session, cancellationToken) =>
                {
                    // Insert message
                    await _messagesCollection.InsertOneAsync(session, message, cancellationToken: cancellationToken);
                    
                    // Update thread timestamp atomically
                    var threadFilter = Builders<ConversationThread>.Filter.Eq(t => t.Id, request.ThreadId);
                    var threadUpdate = Builders<ConversationThread>.Update.Set(t => t.UpdatedAt, now);
                    await _threadsCollection.UpdateOneAsync(session, threadFilter, threadUpdate, cancellationToken: cancellationToken);
                    
                    return "completed";
                });
            }
            finally
            {
                session.Dispose();
            }
        }, _logger, operationName: "SaveMessageWithThreadUpdate");

        return message.Id;
    }

    public async Task<List<ConversationMessage>> GetMessagesAsync(string workflowId, string participantId, int page, int pageSize, string? scope = null)
    {
        // Single optimized query using compound index
        var filterBuilder = Builders<ConversationMessage>.Filter;
        var filter = filterBuilder.And(
            filterBuilder.Eq(x => x.TenantId, _tenantContext.TenantId),
            filterBuilder.Eq(x => x.WorkflowId, workflowId),
            filterBuilder.Eq(x => x.ParticipantId, participantId)
        );

        if (!string.IsNullOrEmpty(scope))
        {
            filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.Scope, scope));
        }

        // Optimized projection for better memory usage
        var projection = Builders<ConversationMessage>.Projection
            .Include(x => x.Id)
            .Include(x => x.CreatedAt)
            .Include(x => x.Direction)
            .Include(x => x.MessageType)
            .Include(x => x.Text)
            .Include(x => x.Data)
            .Include(x => x.Hint)
            .Include(x => x.RequestId);

        var messages = await _messagesCollection
            .Find(filter)
            .Project<ConversationMessage>(projection)
            .Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt))
            .Skip((page - 1) * pageSize)
            .Limit(pageSize)
            .ToListAsync();

        // Convert BSON data efficiently
        foreach (var message in messages)
        {
            ConvertBsonDataToObject(message);
        }

        return messages;
    }

    public async Task<ConversationThreadInfo?> GetThreadInfoAsync(string workflowId, string participantId)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, _tenantContext.TenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowId, workflowId),
            Builders<ConversationThread>.Filter.Eq(x => x.ParticipantId, participantId)
        );

        var projection = Builders<ConversationThread>.Projection
            .Include(x => x.Id)
            .Include(x => x.TenantId)
            .Include(x => x.WorkflowId)
            .Include(x => x.WorkflowType)
            .Include(x => x.ParticipantId)
            .Include(x => x.CreatedAt)
            .Include(x => x.UpdatedAt);

        var thread = await _threadsCollection
            .Find(filter)
            .Project<ConversationThread>(projection)
            .FirstOrDefaultAsync();

        if (thread == null) return null;

        return new ConversationThreadInfo
        {
            Id = thread.Id,
            TenantId = thread.TenantId,
            WorkflowId = thread.WorkflowId,
            WorkflowType = thread.WorkflowType,
            ParticipantId = thread.ParticipantId,
            CreatedAt = thread.CreatedAt,
            UpdatedAt = thread.UpdatedAt,
            IsNew = false
        };
    }



    private static BsonDocument? ConvertToBsonDocument(object? obj)
    {
        if (obj == null) return null;
        
        try
        {
            return obj switch
            {
                BsonDocument bsonDoc => bsonDoc,
                IDictionary<string, object> dict => new BsonDocument(dict),
                _ => BsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(obj))
            };
        }
        catch
        {
            return new BsonDocument("raw", obj.ToString());
        }
    }

    private static void ConvertBsonDataToObject(ConversationMessage message)
    {
        if (message.Data is BsonDocument bsonDoc)
        {
            try
            {
                message.Data = MongoDB.Bson.Serialization.BsonSerializer.Deserialize<object>(bsonDoc);
            }
            catch
            {
                // Keep as BsonDocument if conversion fails
            }
        }
    }
} 