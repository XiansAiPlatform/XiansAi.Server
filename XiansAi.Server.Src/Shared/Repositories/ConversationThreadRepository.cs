using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using XiansAi.Server.Shared.Data;

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
    Task<ConversationThread?> GetByIdAsync(string id);
    Task<ConversationThread?> GetByCompositeKeyAsync(string tenantId, string workflowId, string participantId);
    Task<List<ConversationThread>> GetByWorkflowIdAsync(string tenantId, string workflowId);
    Task<List<ConversationThread>> GetByTenantAndWorkflowAsync(string tenantId, string workflowId, int? page = null, int? pageSize = null);
    Task<List<ConversationThread>> GetByTenantAndParticipantAsync(string tenantId, string participantId, int? page = null, int? pageSize = null);
    Task<List<ConversationThread>> GetByStatusAsync(string tenantId, ConversationThreadStatus status, int? page = null, int? pageSize = null);
    Task<string> CreateAsync(ConversationThread thread);
    Task<bool> UpdateAsync(ConversationThread thread);
    Task<bool> UpdateStatusAsync(string id, ConversationThreadStatus status);
    Task<bool> UpdateLastActivityAsync(string id, DateTime timestamp);
}

public class ConversationThreadRepository : IConversationThreadRepository
{
    private readonly IMongoCollection<ConversationThread> _collection;

    public ConversationThreadRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _collection = database.GetCollection<ConversationThread>("conversation_thread");

        // Create indexes
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        // Composite key index (tenant_id, workflow_id, participant_id)
        var compositeKeyIndex = Builders<ConversationThread>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.WorkflowId)
            .Ascending(x => x.ParticipantId);
        
        var compositeKeyIndexModel = new CreateIndexModel<ConversationThread>(
            compositeKeyIndex,
            new CreateIndexOptions { Background = true, Name = "thread_composite_key", Unique = true }
        );

        // Status lookup index
        var statusIndex = Builders<ConversationThread>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.Status);
        
        var statusIndexModel = new CreateIndexModel<ConversationThread>(
            statusIndex,
            new CreateIndexOptions { Background = true, Name = "thread_status_lookup" }
        );

        // Updated at index
        var updatedAtIndex = Builders<ConversationThread>.IndexKeys
            .Descending(x => x.UpdatedAt);
        
        var updatedAtIndexModel = new CreateIndexModel<ConversationThread>(
            updatedAtIndex,
            new CreateIndexOptions { Background = true, Name = "thread_updated_at" }
        );

        // Create all indexes
        _collection.Indexes.CreateMany(new[] { 
            compositeKeyIndexModel, 
            statusIndexModel, 
            updatedAtIndexModel 
        });
    }

    public async Task<List<ConversationThread>> GetByWorkflowIdAsync(string tenantId, string workflowId)
    {
        return await _collection.Find(x => x.TenantId == tenantId && x.WorkflowId == workflowId)
            .Sort(Builders<ConversationThread>.Sort.Descending(x => x.UpdatedAt))
            .ToListAsync();
    }
    
    public async Task<ConversationThread?> GetByIdAsync(string id)
    {
        return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    public async Task<ConversationThread?> GetByCompositeKeyAsync(string tenantId, string workflowId, string participantId)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowId, workflowId),
            Builders<ConversationThread>.Filter.Eq(x => x.ParticipantId, participantId)
        );

        return await _collection.Find(filter).FirstOrDefaultAsync();
    }

    public async Task<List<ConversationThread>> GetByTenantAndWorkflowAsync(string tenantId, string workflowId, int? page = null, int? pageSize = null)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowId, workflowId)
        );

        var query = _collection.Find(filter).Sort(Builders<ConversationThread>.Sort.Descending(x => x.UpdatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<List<ConversationThread>> GetByTenantAndParticipantAsync(string tenantId, string participantId, int? page = null, int? pageSize = null)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.ParticipantId, participantId)
        );

        var query = _collection.Find(filter).Sort(Builders<ConversationThread>.Sort.Descending(x => x.UpdatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<List<ConversationThread>> GetByStatusAsync(string tenantId, ConversationThreadStatus status, int? page = null, int? pageSize = null)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.Status, status)
        );

        var query = _collection.Find(filter).Sort(Builders<ConversationThread>.Sort.Descending(x => x.UpdatedAt));

        if (page.HasValue && pageSize.HasValue)
        {
            query = query.Skip((page.Value - 1) * pageSize.Value).Limit(pageSize.Value);
        }

        return await query.ToListAsync();
    }

    public async Task<string> CreateAsync(ConversationThread thread)
    {
        await _collection.InsertOneAsync(thread);
        return thread.Id;
    }

    public async Task<bool> UpdateAsync(ConversationThread thread)
    {
        thread.UpdatedAt = DateTime.UtcNow;
        var result = await _collection.ReplaceOneAsync(x => x.Id == thread.Id, thread);
        return result.ModifiedCount > 0;
    }

    public async Task<bool> UpdateStatusAsync(string id, ConversationThreadStatus status)
    {
        var update = Builders<ConversationThread>.Update
            .Set(x => x.Status, status)
            .Set(x => x.UpdatedAt, DateTime.UtcNow);

        var result = await _collection.UpdateOneAsync(x => x.Id == id, update);
        return result.ModifiedCount > 0;
    }

    /// <summary>
    /// Updates the thread's UpdatedAt timestamp to indicate new activity
    /// </summary>
    /// <param name="id">The thread ID</param>
    /// <param name="timestamp">The timestamp to set</param>
    /// <returns>True if the update was successful, false otherwise</returns>
    public async Task<bool> UpdateLastActivityAsync(string id, DateTime timestamp)
    {
        var update = Builders<ConversationThread>.Update
            .Set(x => x.UpdatedAt, timestamp);
        
        var result = await _collection.UpdateOneAsync(x => x.Id == id, update);
        return result.ModifiedCount > 0;
    }

}
