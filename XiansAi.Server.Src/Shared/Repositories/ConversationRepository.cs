using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using System.Text.Json.Serialization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Shared.Data;
using Shared.Utils;
using Shared.Auth;
using Shared.Services;

namespace Shared.Repositories;

// Enums
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
    Webhook
}

public enum ConversationThreadStatus
{
    Active,
    Archived
}

// Message Log Event
public class MessageLogEvent
{
    [BsonElement("timestamp")]
    public required DateTime Timestamp { get; set; }

    [BsonElement("event")]
    public required string Event { get; set; }

    [BsonElement("details")]
    public string? Details { get; set; }
}

// ConversationMessage model
[BsonIgnoreExtraElements]
public class ConversationMessage
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("thread_id")]
    public required string ThreadId { get; set; }

    [BsonElement("request_id")]
    public string? RequestId { get; set; }

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

    [BsonElement("scope")]
    public string? Scope { get; set; }

    [BsonElement("hint")]
    public string? Hint { get; set; }

    [BsonElement("task_id")]
    public string? TaskId { get; set; }

    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }

    [BsonElement("workflow_type")]
    public required string WorkflowType { get; set; }

    [BsonElement("message_type")]
    [BsonRepresentation(BsonType.String)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MessageType? MessageType { get; set; }
    
    [BsonElement("origin")]
    public string? Origin { get; set; }
}

// ConversationThread model
[BsonIgnoreExtraElements]
public class ConversationThread
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = null!;

    [BsonElement("tenant_id")]
    public required string TenantId { get; set; }

    [BsonElement("workflow_id")]
    public required string WorkflowId { get; set; }

    [BsonElement("workflow_type")]
    public string? WorkflowType { get; set; }

    [BsonElement("agent")]
    public required string Agent { get; set; }

    [BsonElement("participant_id")]
    public required string ParticipantId { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }

    [BsonElement("updated_at")]
    public DateTime? UpdatedAt { get; set; }

    [BsonElement("created_by")]
    public required string CreatedBy { get; set; }

    [BsonElement("status")]
    [BsonRepresentation(BsonType.String)]
    public required ConversationThreadStatus Status { get; set; }
}

/// <summary>
/// Unified conversation repository that combines thread and message operations
/// with optimized performance using transactions and atomic operations
/// </summary>
public class TopicInfo
{
    public string? Scope { get; set; }
    public int MessageCount { get; set; }
    public DateTime LastMessageAt { get; set; }
}

public class TopicsResult
{
    public required List<TopicInfo> Topics { get; set; }
    public required PaginationMetadata Pagination { get; set; }
}

public class PaginationMetadata
{
    public int CurrentPage { get; set; }
    public int PageSize { get; set; }
    public int TotalTopics { get; set; }
    public int TotalPages { get; set; }
    public bool HasMore { get; set; }
}

public interface IConversationRepository
{
    // Thread operations
    Task<string> CreateOrGetThreadIdAsync(ConversationThread thread);
    Task<List<ConversationThread>> GetByTenantAndAgentAsync(string tenantId, string agent, int? page = null, int? pageSize = null);
    Task<bool> DeleteThreadAsync(string threadId, string? tenantId = null);
    Task<string> GetThreadIdAsync(string tenantId, string workflowId, string participantId);


    // Message operations
    Task<string> SaveMessageAsync(ConversationMessage message);
    Task<List<ConversationMessage>> GetMessagesByThreadIdAsync(string tenantId, string threadId, int? page = null, int? pageSize = null, string? scope = null, bool chatOnly = false);
    Task<List<ConversationMessage>> GetMessagesByWorkflowAndParticipantAsync(string workflowId, string participantId, int page, int pageSize, string? scope = null, string sortOrder = "desc");
    Task<bool> DeleteMessagesByThreadIdAsync(string threadId);
    Task<bool> DeleteMessagesByWorkflowParticipantAndScopeAsync(string tenantId, string workflowId, string participantId, string? scope);

    // Topics operations
    Task<TopicsResult> GetTopicsByThreadIdAsync(string tenantId, string threadId, int page, int pageSize);


    // Task ID operations
    Task<string?> GetLastTaskIdAsync(string tenantId, string workflowId, string participantId, string? scope = null);

    // Origin operations
    Task<string?> GetLastIncomingOriginAsync(string threadId, string tenantId);
    Task<object?> GetLastIncomingDataAsync(string threadId, string tenantId);

    // Statistics operations
    Task<(int totalMessages, int activeUsers)> GetMessagingStatsAsync(string tenantId, DateTime startDate, DateTime endDate, string? participantId = null);

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
    private readonly IMongoDatabase _database;
    private readonly ILogger<ConversationRepository> _logger;
    private readonly ITenantContext _tenantContext;
    private readonly ISecureEncryptionService _encryptionService;
    private readonly string _uniqueSecret;

    public ConversationRepository(
        IDatabaseService databaseService, 
        ILogger<ConversationRepository> logger, 
        ITenantContext tenantContext,
        ISecureEncryptionService encryptionService,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _tenantContext = tenantContext ?? throw new ArgumentNullException(nameof(tenantContext));
        _encryptionService = encryptionService ?? throw new ArgumentNullException(nameof(encryptionService));
        
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _database = database;
        _messagesCollection = database.GetCollection<ConversationMessage>("conversation_message");
        _threadsCollection = database.GetCollection<ConversationThread>("conversation_thread");
        
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

    #region Thread Operations

    public async Task<string> CreateOrGetThreadIdAsync(ConversationThread thread)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var existingThread = await GetByCompositeKeyAsync(thread.TenantId, thread.WorkflowId, thread.ParticipantId);
            if (existingThread != null)
            {
                _logger.LogInformation("Found existing thread {ThreadId} for tenantId {TenantId}, workflowId {WorkflowId}, and participantId {ParticipantId}", 
                    existingThread.Id, thread.TenantId, thread.WorkflowId, thread.ParticipantId);
                return existingThread.Id;
            }

            thread.CreatedAt = DateTime.UtcNow;
            thread.UpdatedAt = DateTime.UtcNow;

            try
            {
                await _threadsCollection.InsertOneAsync(thread);
                _logger.LogInformation("Created new thread {ThreadId} for tenantId {TenantId}, workflowId {WorkflowId}, and participantId {ParticipantId}", 
                    thread.Id, thread.TenantId, thread.WorkflowId, thread.ParticipantId);
                return thread.Id;
            }
            catch (MongoWriteException ex) when (ex.WriteError?.Code == 11000)
            {
                // Handle duplicate key error - another thread was created concurrently
                _logger.LogWarning("Duplicate key error when creating thread. Attempting to retrieve existing thread.");
                existingThread = await GetByCompositeKeyAsync(thread.TenantId, thread.WorkflowId, thread.ParticipantId);
                if (existingThread != null)
                {
                    return existingThread.Id;
                }
                throw;
            }
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "CreateOrGetThreadId");
    }

    public async Task<List<ConversationThread>> GetByTenantAndAgentAsync(string tenantId, string agent, int? page = null, int? pageSize = null)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filter = Builders<ConversationThread>.Filter.And(
                Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ConversationThread>.Filter.Eq(x => x.Agent, agent)
            );

            var query = _threadsCollection.Find(filter).Sort(Builders<ConversationThread>.Sort.Descending(x => x.UpdatedAt));

            if (page.HasValue && pageSize.HasValue)
            {
                var skip = (page.Value - 1) * pageSize.Value;
                _logger.LogDebug("Applying pagination: page={Page}, pageSize={PageSize}, skip={Skip}, limit={Limit}", 
                    page.Value, pageSize.Value, skip, pageSize.Value);
                query = query.Skip(skip).Limit(pageSize.Value);
            }
            else
            {
                _logger.LogDebug("No pagination applied: page={Page}, pageSize={PageSize}", page, pageSize);
            }

            var results = await query.ToListAsync();
            _logger.LogDebug("GetByTenantAndAgentAsync returned {Count} threads for tenant {TenantId} and agent {Agent}", 
                results.Count, tenantId, agent);
            
            return results;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetByTenantAndAgent");
    }

    public async Task<bool> DeleteThreadAsync(string id, string? tenantId = null)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // First check if thread exists
            var thread = await _threadsCollection.Find(x => x.Id == id).FirstOrDefaultAsync();
            if (thread == null)
            {
                _logger.LogWarning("Thread {ThreadId} not found", id);
                return false;
            }

            // Validate tenant ownership (skip check if tenantId is null - SysAdmin action)
            if (tenantId != null && thread.TenantId != tenantId)
            {
                _logger.LogWarning("Thread {ThreadId} does not belong to tenant {TenantId}. IDOR attempt detected.", id, tenantId);
                return false;
            }

            // Use transaction to delete thread and its messages atomically
            using var session = await _database.Client.StartSessionAsync();
            
            try
            {
                var result = await session.WithTransactionAsync(async (session, cancellationToken) =>
                {
                    // Delete all messages in the thread
                    var messageDeleteResult = await _messagesCollection.DeleteManyAsync(
                        session, 
                        Builders<ConversationMessage>.Filter.Eq(m => m.ThreadId, id), 
                        cancellationToken: cancellationToken);
                    
                    _logger.LogInformation("Deleted {MessageCount} messages from thread {ThreadId}", 
                        messageDeleteResult.DeletedCount, id);

                    // Delete the thread
                    var threadDeleteResult = await _threadsCollection.DeleteOneAsync(
                        session, 
                        Builders<ConversationThread>.Filter.Eq(t => t.Id, id), 
                        cancellationToken: cancellationToken);

                    return threadDeleteResult.DeletedCount > 0;
                });

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting thread {ThreadId}", id);
                throw;
            }
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "DeleteThread");
    }

    #endregion

    #region Message Operations

    public async Task<string> SaveMessageAsync(ConversationMessage message)
    {
        var now = DateTime.UtcNow;
        message.CreatedAt = now;
        message.UpdatedAt = now;
        
        // Encrypt the Text property if it's not null or empty
        if (!string.IsNullOrEmpty(message.Text))
        {
            try
            {
                // Use a combination of tenant ID and message ID as the unique secret
                var messageSpecificSecret = $"{_uniqueSecret}";
                message.Text = _encryptionService.Encrypt(message.Text, messageSpecificSecret);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to encrypt message text for message {MessageId}", message.Id);
                throw new InvalidOperationException("Failed to encrypt message text", ex);
            }
        }
        
        // Convert data to BsonDocument if needed
        if (message.Data != null)
        {
            message.Data = ConvertToBsonDocument(message.Data);
        }

        // Use MongoDB's atomic operations for optimal performance
        await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var session = await _database.Client.StartSessionAsync();
            try
            {
                await session.WithTransactionAsync(async (session, cancellationToken) =>
                {
                    // Insert message
                    await _messagesCollection.InsertOneAsync(session, message, cancellationToken: cancellationToken);
                    
                    // Update thread timestamp atomically
                    var threadFilter = Builders<ConversationThread>.Filter.Eq(t => t.Id, message.ThreadId);
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

    public async Task<List<ConversationMessage>> GetMessagesByThreadIdAsync(
string tenantId, string threadId, int? page = null, int? pageSize = null, string? scope = null, bool chatOnly = false)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Build message filter
            var messageFilter = Builders<ConversationMessage>.Filter.And(
                Builders<ConversationMessage>.Filter.Eq(x => x.TenantId, tenantId),
                Builders<ConversationMessage>.Filter.Eq(x => x.ThreadId, threadId)
            );

            // Handle scope filtering:
            // - If scope is not provided (null): return all messages (no filtering)
            // - If scope is empty string: return only messages with null scope
            // - If scope has a value: return only messages with that exact scope
            if (scope != null)
            {
                if (string.IsNullOrEmpty(scope))
                {
                    _logger.LogDebug("Filtering messages with no scope (null)");
                    messageFilter = Builders<ConversationMessage>.Filter.And(
                        messageFilter,
                        Builders<ConversationMessage>.Filter.Eq(x => x.Scope, null));
                }
                else
                {
                    _logger.LogDebug("Filtering messages by scope `{Scope}`", scope);
                    messageFilter = Builders<ConversationMessage>.Filter.And(
                        messageFilter, 
                        Builders<ConversationMessage>.Filter.Eq(x => x.Scope, scope));
                }
            }

            if (chatOnly)
            {
                messageFilter = Builders<ConversationMessage>.Filter.And(
                    messageFilter, 
                    Builders<ConversationMessage>.Filter.Eq(x => x.MessageType, MessageType.Chat));
            }

            var query = _messagesCollection.Find(messageFilter)
                .Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt));

            if (page.HasValue && pageSize.HasValue)
            {
                var skip = (page.Value - 1) * pageSize.Value;
                _logger.LogDebug("Applying pagination to messages: page={Page}, pageSize={PageSize}, skip={Skip}, limit={Limit}", 
                    page.Value, pageSize.Value, skip, pageSize.Value);
                query = query.Skip(skip).Limit(pageSize.Value);
            }
            else
            {
                _logger.LogDebug("No pagination applied to messages: page={Page}, pageSize={PageSize}", page, pageSize);
            }

            // Project only the fields we need
            var projection = Builders<ConversationMessage>.Projection
                .Include(x => x.Id)
                .Include(x => x.ThreadId)
                .Include(x => x.TenantId)
                .Include(x => x.ParticipantId)
                .Include(x => x.WorkflowId)
                .Include(x => x.WorkflowType)
                .Include(x => x.CreatedAt)
                .Include(x => x.UpdatedAt)
                .Include(x => x.CreatedBy)
                .Include(x => x.Direction)
                .Include(x => x.MessageType)
                .Include(x => x.Text)
                .Include(x => x.Data)
                .Include(x => x.Status)
                .Include(x => x.Hint)
                .Include(x => x.TaskId)
                .Include(x => x.Scope)
                .Include(x => x.RequestId)
                .Include(x => x.Origin);

            var messages = await query.Project<ConversationMessage>(projection).ToListAsync();
            
            // Convert BSON data back to objects and decrypt text
            foreach (var message in messages)
            {
                ConvertBsonDataToObject(message);
                DecryptMessageText(message);
            }
            
            _logger.LogDebug("Found history of {Count} messages for thread {ThreadId}", messages.Count, threadId);
            return messages;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetMessagesByThreadId");
    }

    public async Task<List<ConversationMessage>> GetMessagesByWorkflowAndParticipantAsync(
        string workflowId, string participantId, int page, int pageSize, string? scope = null, string sortOrder = "desc")
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            // Single optimized query using compound index
            var filterBuilder = Builders<ConversationMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, _tenantContext.TenantId),
                filterBuilder.Eq(x => x.WorkflowId, workflowId),
                filterBuilder.Eq(x => x.ParticipantId, participantId)
            );

            // Handle scope filtering:
            // - If scope is not provided (null) or empty string: return only messages with null scope
            // - If scope has a value: return only messages with that exact scope
            if (string.IsNullOrEmpty(scope))
            {
                _logger.LogDebug("Filtering messages with no scope (null) for workflowId {WorkflowId}", workflowId);
                filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.Scope, null));
            }
            else
            {
                _logger.LogDebug("Filtering messages by scope `{Scope}` for workflowId {WorkflowId}", scope, workflowId);
                filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.Scope, scope));
            }

            // Optimized projection for better memory usage
            var projection = Builders<ConversationMessage>.Projection
                .Include(x => x.Id)
                .Include(x => x.ThreadId)
                .Include(x => x.TenantId)
                .Include(x => x.ParticipantId)
                .Include(x => x.WorkflowId)
                .Include(x => x.WorkflowType)
                .Include(x => x.CreatedAt)
                .Include(x => x.UpdatedAt)
                .Include(x => x.CreatedBy)
                .Include(x => x.Direction)
                .Include(x => x.MessageType)
                .Include(x => x.Text)
                .Include(x => x.Data)
                .Include(x => x.Status)
                .Include(x => x.Hint)
                .Include(x => x.TaskId)
                .Include(x => x.Scope)
                .Include(x => x.RequestId)
                .Include(x => x.Origin);

            // Apply sort order based on parameter
            var sort = sortOrder.ToLowerInvariant() == "asc" 
                ? Builders<ConversationMessage>.Sort.Ascending(x => x.CreatedAt)
                : Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt);

            var messages = await _messagesCollection
                .Find(filter)
                .Project<ConversationMessage>(projection)
                .Sort(sort)
                .Skip((page - 1) * pageSize)
                .Limit(pageSize)
                .ToListAsync();

            // Convert BSON data efficiently and decrypt text
            foreach (var message in messages)
            {
                ConvertBsonDataToObject(message);
                DecryptMessageText(message);
            }

            return messages;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetMessagesByWorkflowAndParticipant");
    }

    public async Task<bool> DeleteMessagesByThreadIdAsync(string threadId)
    {
        try
        {
            var filter = Builders<ConversationMessage>.Filter.And(
                Builders<ConversationMessage>.Filter.Eq(x => x.ThreadId, threadId)
            );

            var result = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _messagesCollection.DeleteManyAsync(filter),
                _logger,
                operationName: "DeleteMessagesByThreadId");

            _logger.LogInformation("Deleted {DeletedCount} messages for thread {ThreadId}", 
                result.DeletedCount, threadId);
            
            return result.DeletedCount >= 0; // Return true even if no messages were found
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting messages for thread {ThreadId}", threadId);
            throw;
        }
    }

    public async Task<bool> DeleteMessagesByWorkflowParticipantAndScopeAsync(string tenantId, string workflowId, string participantId, string? scope)
    {
        try
        {
            // First, get the thread ID
            string threadId;
            try
            {
                threadId = await GetThreadIdAsync(tenantId, workflowId, participantId);
            }
            catch (KeyNotFoundException)
            {
                _logger.LogWarning("Thread not found for workflowId {WorkflowId}, participant {ParticipantId}, tenant {TenantId}. No messages to delete.", 
                    workflowId, participantId, tenantId);
                return true; // Consider success if thread doesn't exist
            }

            // Build filter for messages with specific scope (or null scope)
            var filters = new List<FilterDefinition<ConversationMessage>>
            {
                Builders<ConversationMessage>.Filter.Eq(x => x.ThreadId, threadId),
                Builders<ConversationMessage>.Filter.Eq(x => x.TenantId, tenantId)
            };

            // Add scope filter - handle null scope explicitly
            if (scope == null)
            {
                filters.Add(Builders<ConversationMessage>.Filter.Eq(x => x.Scope, null));
            }
            else
            {
                filters.Add(Builders<ConversationMessage>.Filter.Eq(x => x.Scope, scope));
            }

            var filter = Builders<ConversationMessage>.Filter.And(filters);

            var result = await MongoRetryHelper.ExecuteWithRetryAsync(
                async () => await _messagesCollection.DeleteManyAsync(filter),
                _logger,
                operationName: "DeleteMessagesByWorkflowParticipantAndScope");

            _logger.LogInformation("Deleted {DeletedCount} messages for workflowId {WorkflowId}, participant {ParticipantId}, scope {Scope}", 
                result.DeletedCount, workflowId, participantId, scope ?? "null");
            
            return result.DeletedCount >= 0; // Return true even if no messages were found
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting messages for workflowId {WorkflowId}, participant {ParticipantId}, scope {Scope}", 
                workflowId, participantId, scope ?? "null");
            throw;
        }
    }

    public async Task<string> GetThreadIdAsync(string tenantId, string workflowId, string participantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var thread = await GetByCompositeKeyAsync(tenantId, workflowId, participantId);
            if (thread == null)
            {
                throw new KeyNotFoundException($"No conversation thread found for tenant '{tenantId}', workflow '{workflowId}', and participant '{participantId}'.");
            }
            return thread.Id;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetThreadId");
    }

    public async Task<TopicsResult> GetTopicsByThreadIdAsync(string tenantId, string threadId, int page, int pageSize)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            _logger.LogDebug("Getting topics for thread {ThreadId}, page={Page}, pageSize={PageSize}", 
                threadId, page, pageSize);

            var skip = (page - 1) * pageSize;

            // OPTIMIZATION: Single aggregation using $facet to get count and data in one query
            // This eliminates the need for two separate aggregations, cutting query time in half
            var pipeline = new[]
            {
                // Match documents for this thread
                new BsonDocument("$match", new BsonDocument
                {
                    { "tenant_id", tenantId },
                    { "thread_id", threadId }
                }),
                // Group by scope to get topic statistics
                new BsonDocument("$group", new BsonDocument
                {
                    { "_id", "$scope" },
                    { "message_count", new BsonDocument("$sum", 1) },
                    { "last_message_at", new BsonDocument("$max", "$created_at") }
                }),
                // Use $facet to get both count and paginated data in single pass
                new BsonDocument("$facet", new BsonDocument
                {
                    // Count total topics
                    { "totalCount", new BsonArray
                        {
                            new BsonDocument("$count", "count")
                        }
                    },
                    // Get paginated topic data
                    { "data", new BsonArray
                        {
                            new BsonDocument("$sort", new BsonDocument
                            {
                                { "last_message_at", -1 },  // Most recent first
                                { "_id", 1 }                  // Stable sort by scope name
                            }),
                            new BsonDocument("$skip", skip),
                            new BsonDocument("$limit", pageSize)
                        }
                    }
                })
            };

            // OPTIMIZATION: AllowDiskUse prevents memory errors on large datasets
            // OPTIMIZATION: MaxTime prevents runaway queries
            var aggregateOptions = new AggregateOptions 
            { 
                AllowDiskUse = true,
                MaxTime = TimeSpan.FromSeconds(30)
            };

            var result = await _messagesCollection
                .Aggregate<BsonDocument>(pipeline, aggregateOptions)
                .FirstOrDefaultAsync();

            // Extract count from facet result
            var totalTopics = 0;
            if (result != null && result.Contains("totalCount"))
            {
                var countArray = result["totalCount"].AsBsonArray;
                if (countArray.Count > 0)
                {
                    totalTopics = countArray[0]["count"].ToInt32();
                }
            }

            // Extract data from facet result
            var dataArray = result?["data"]?.AsBsonArray ?? new BsonArray();
            var topics = dataArray.Select(doc => new TopicInfo
            {
                Scope = doc["_id"].IsBsonNull ? null : doc["_id"].AsString,
                MessageCount = doc["message_count"].ToInt32(),
                LastMessageAt = doc["last_message_at"].ToUniversalTime()
            }).ToList();

            // Calculate pagination metadata
            var totalPages = totalTopics > 0 ? (int)Math.Ceiling((double)totalTopics / pageSize) : 0;
            var hasMore = page < totalPages;

            stopwatch.Stop();
            
            _logger.LogDebug("Found {Count} topics for thread {ThreadId} (page {Page} of {TotalPages}) in {Duration}ms", 
                topics.Count, threadId, page, totalPages, stopwatch.ElapsedMilliseconds);

            // Log slow queries for monitoring
            if (stopwatch.ElapsedMilliseconds > 1000)
            {
                _logger.LogWarning("SLOW QUERY: Topics aggregation took {Duration}ms for thread {ThreadId}, page {Page}",
                    stopwatch.ElapsedMilliseconds, threadId, page);
            }

            return new TopicsResult
            {
                Topics = topics,
                Pagination = new PaginationMetadata
                {
                    CurrentPage = page,
                    PageSize = pageSize,
                    TotalTopics = totalTopics,
                    TotalPages = totalPages,
                    HasMore = hasMore
                }
            };
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetTopicsByThreadId");
    }

    public async Task<string?> GetLastIncomingOriginAsync(string threadId, string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filterBuilder = Builders<ConversationMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.ThreadId, threadId),
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Eq(x => x.Direction, MessageDirection.Incoming),
                filterBuilder.Ne(x => x.Origin, null),
                filterBuilder.Ne(x => x.Origin, "")
            );

            // Get the most recent incoming message with an origin
            var projection = Builders<ConversationMessage>.Projection.Include(x => x.Origin);
            
            var message = await _messagesCollection
                .Find(filter)
                .Project<ConversationMessage>(projection)
                .Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt))
                .Limit(1)
                .FirstOrDefaultAsync();

            _logger.LogDebug("Last incoming origin for thread {ThreadId}: {Origin}",
                threadId, message?.Origin ?? "none");

            return message?.Origin;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetLastIncomingOrigin");
    }

    public async Task<object?> GetLastIncomingDataAsync(string threadId, string tenantId)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filterBuilder = Builders<ConversationMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.ThreadId, threadId),
                filterBuilder.Eq(x => x.TenantId, tenantId),
                filterBuilder.Eq(x => x.Direction, MessageDirection.Incoming),
                filterBuilder.Ne(x => x.Data, null)
            );

            // Get the most recent incoming message with data
            var projection = Builders<ConversationMessage>.Projection.Include(x => x.Data);
            
            var message = await _messagesCollection
                .Find(filter)
                .Project<ConversationMessage>(projection)
                .Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt))
                .Limit(1)
                .FirstOrDefaultAsync();

            _logger.LogDebug("Last incoming data for thread {ThreadId}: {HasData}",
                threadId, message?.Data != null ? "yes" : "none");

            return message?.Data;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetLastIncomingData");
    }


    public async Task<string?> GetLastTaskIdAsync(string tenantId, string workflowId, string participantId, string? scope = null)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            var filterBuilder = Builders<ConversationMessage>.Filter;
            // Match exact workflow_id or full Temporal run id (workflow_id starting with workflowId + ":")
            var workflowFilter = filterBuilder.Or(
                filterBuilder.Eq(x => x.WorkflowId, workflowId),
                filterBuilder.Regex(x => x.WorkflowId, new BsonRegularExpression("^" + Regex.Escape(workflowId) + ":"))
            );
            var filter = filterBuilder.And(
                filterBuilder.Eq(x => x.TenantId, tenantId),
                workflowFilter,
                filterBuilder.Eq(x => x.ParticipantId, participantId),
                filterBuilder.Ne(x => x.TaskId, null),
                filterBuilder.Ne(x => x.TaskId, "")
            );

            if (scope != null)
            {
                if (string.IsNullOrEmpty(scope))
                {
                    _logger.LogDebug("Filtering messages with no scope (null) for last task id");
                    filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.Scope, null));
                }
                else
                {
                    _logger.LogDebug("Filtering messages by scope `{Scope}` for last task id", scope);
                    filter = filterBuilder.And(filter, filterBuilder.Eq(x => x.Scope, scope));
                }
            }

            var projection = Builders<ConversationMessage>.Projection.Include(x => x.TaskId);

            var message = await _messagesCollection
                .Find(filter)
                .Project<ConversationMessage>(projection)
                .Sort(Builders<ConversationMessage>.Sort.Descending(x => x.CreatedAt))
                .Limit(1)
                .FirstOrDefaultAsync();

            _logger.LogDebug("Last task id for workflow {WorkflowId}, participant {ParticipantId}, scope {Scope}: {TaskId}",
                workflowId, participantId, scope ?? "null", message?.TaskId ?? "none");

            return message?.TaskId;
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetLastTaskId");
    }

    public async Task<(int totalMessages, int activeUsers)> GetMessagingStatsAsync(
        string tenantId, 
        DateTime startDate, 
        DateTime endDate, 
        string? participantId = null)
    {
        return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
        {
            _logger.LogDebug(
                "Getting messaging stats for tenantId {TenantId}, dateRange {StartDate} to {EndDate}, participantId {ParticipantId}",
                tenantId, startDate, endDate, participantId ?? "null");

            // Build filter for messages in date range
            var filterBuilder = Builders<ConversationMessage>.Filter;
            var filter = filterBuilder.And(
                filterBuilder.Eq(m => m.TenantId, tenantId),
                filterBuilder.Gte(m => m.CreatedAt, startDate),
                filterBuilder.Lte(m => m.CreatedAt, endDate)
            );

            // Add participant filter if specified
            if (!string.IsNullOrEmpty(participantId))
            {
                filter = filterBuilder.And(filter, filterBuilder.Eq(m => m.ParticipantId, participantId));
            }

            // Count total messages
            var totalMessages = await _messagesCollection.CountDocumentsAsync(filter);

            // Count distinct active users (participants who sent messages)
            var distinctParticipants = await _messagesCollection
                .DistinctAsync<string>("participant_id", filter);
            
            var activeUsersList = await distinctParticipants.ToListAsync();
            var activeUsers = activeUsersList.Count;

            _logger.LogDebug(
                "Messaging stats retrieved - TotalMessages: {TotalMessages}, ActiveUsers: {ActiveUsers}",
                totalMessages, activeUsers);

            return ((int)totalMessages, activeUsers);
        }, _logger, maxRetries: 3, baseDelayMs: 100, operationName: "GetMessagingStats");
    }

    #endregion

    #region Private Helper Methods

    private async Task<ConversationThread?> GetByCompositeKeyAsync(string tenantId, string workflowId, string participantId)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowId, workflowId),
            Builders<ConversationThread>.Filter.Eq(x => x.ParticipantId, participantId)
        );

        return await _threadsCollection.Find(filter).FirstOrDefaultAsync();
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
                _logger.LogTrace("Successfully decrypted message {MessageId}", message.Id);
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

    private BsonDocument? ConvertToBsonDocument(object? obj)
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

    private void ConvertBsonDataToObject(ConversationMessage message)
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

    #endregion
}
