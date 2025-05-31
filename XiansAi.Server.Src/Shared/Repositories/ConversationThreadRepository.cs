using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using XiansAi.Server.Shared.Data;
using Shared.Data;
using System.Linq;
using Shared.Utils;
using Microsoft.Extensions.Logging;

namespace Shared.Repositories;

public enum ConversationThreadStatus
{
    Active,
    Archived
}

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
    public required string WorkflowType { get; set; }

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

    [BsonElement("is_internal_thread")]
    public bool IsInternalThread { get; set; }
}

public interface IConversationThreadRepository
{
    Task<List<ConversationThread>> GetByTenantAndAgentAsync(string tenantId, string agent, int? page = null, int? pageSize = null);
    Task<string> CreateOrGetAsync(ConversationThread thread);
    Task<bool> UpdateWorkflowIdAndTypeAsync(string id, string workflowId, string workflowType);
    Task<bool> DeleteAsync(string id);
}

public class ConversationThreadRepository : IConversationThreadRepository
{
    private readonly IMongoCollection<ConversationThread> _collection;
    private readonly ILogger<ConversationThreadRepository> _logger;

    public ConversationThreadRepository(IDatabaseService databaseService, ILogger<ConversationThreadRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _collection = database.GetCollection<ConversationThread>("conversation_thread");
    }


    public async Task<bool> UpdateWorkflowIdAndTypeAsync(string id, string workflowId, string workflowType)
    {
        try
        {
            return await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            {
                var update = Builders<ConversationThread>.Update
                    .Set(x => x.WorkflowId, workflowId)
                    .Set(x => x.WorkflowType, workflowType)
                    .Set(x => x.UpdatedAt, DateTime.UtcNow);
                var result = await _collection.UpdateOneAsync(x => x.Id == id, update);
                return result.ModifiedCount > 0;
            }, _logger, operationName: "UpdateWorkflowIdAndTypeAsync");
        }
        catch (MongoBulkWriteException ex)
        {
            // Check if it's a duplicate key error (error code 11000)
            if (ex.WriteErrors.Any(e => e.Code == 11000))
            {
                // The combination of tenant_id, agent, workflow_type, and participant_id already exists
                // This means there's already another thread with the target workflow type
                // In this case, we should fail gracefully but log the issue
                
                // First, let's get the current thread to extract the composite key values
                var currentThread = await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
                if (currentThread != null)
                {
                    // Check if there's already a thread with the target workflow type
                    var existingThread = await GetByCompositeKeyAsync(
                        currentThread.TenantId, 
                        currentThread.Agent, 
                        workflowType, 
                        currentThread.ParticipantId);
                    
                    if (existingThread != null && existingThread.Id != id)
                    {
                        // There's already another thread with this combination
                        // This is a business logic issue that needs to be handled at a higher level
                        throw new InvalidOperationException(
                            $"Cannot update thread {id} to workflow type '{workflowType}' because thread {existingThread.Id} already exists with the same tenant, agent, workflow type, and participant combination.");
                    }
                }
                
                // If we couldn't determine the exact issue, rethrow the original exception
                throw;
            }
            // For other errors, rethrow
            throw;
        }
    }

    private async Task<ConversationThread?> GetByCompositeKeyAsync(string tenantId, string agent, string workflowType, string participantId)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.Agent, agent),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowType, workflowType),
            Builders<ConversationThread>.Filter.Eq(x => x.ParticipantId, participantId)
        );

        return await _collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<List<ConversationThread>> GetByTenantAndAgentAsync(string tenantId, string agent, int? page = null, int? pageSize = null)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.Agent, agent)
        );

        var query = _collection.Find(filter).Sort(Builders<ConversationThread>.Sort.Descending(x => x.UpdatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value);
        }

        return await query.ToListAsync();
    }


    public async Task<string> CreateOrGetAsync(ConversationThread thread)
    {
        var existingThread = await GetByCompositeKeyAsync(thread.TenantId, thread.Agent, thread.WorkflowType, thread.ParticipantId);
        if (existingThread != null)
        {
            return existingThread.Id;
        }
        
        try
        {
            await _collection.InsertOneAsync(thread);
            return thread.Id;
        }
        catch (MongoBulkWriteException ex)
        {
            // Check if it's a duplicate key error (error code 11000)
            if (ex.WriteErrors.Any(e => e.Code == 11000))
            {
                // Another thread created the record between our check and insert
                // Fetch and return the existing thread
                existingThread = await GetByCompositeKeyAsync(thread.TenantId, thread.Agent, thread.WorkflowType, thread.ParticipantId);
                if (existingThread != null)
                {
                    return existingThread.Id;
                }
                // If we still can't find it, something went wrong, rethrow
                throw;
            }
            // For other errors, rethrow
            throw;
        }
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _collection.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }
}
