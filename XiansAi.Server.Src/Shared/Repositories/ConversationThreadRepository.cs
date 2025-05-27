using MongoDB.Driver;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using XiansAi.Server.Shared.Data;
using Shared.Data;

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

    public ConversationThreadRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _collection = database.GetCollection<ConversationThread>("conversation_thread");

        // Create indexes
        CreateIndexes();
    }

    private void CreateIndexes()
    {
        try
        {
            // Drop existing index if it exists
            _collection.Indexes.DropOne("thread_composite_key");
        }
        catch (MongoCommandException)
        {
            // Index might not exist, ignore the error
        }

        // Composite key index (tenant_id, agent, workflow_type, participant_id)
        var compositeKeyIndex = Builders<ConversationThread>.IndexKeys
            .Ascending(x => x.TenantId)
            .Ascending(x => x.Agent)
            .Ascending(x => x.WorkflowType)
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

    public async Task<bool> UpdateWorkflowIdAndTypeAsync(string id, string workflowId, string workflowType)
    {
        var update = Builders<ConversationThread>.Update
            .Set(x => x.WorkflowId, workflowId)
            .Set(x => x.WorkflowType, workflowType);
        var result = await _collection.UpdateOneAsync(x => x.Id == id, update);
        return result.ModifiedCount > 0;
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
        await _collection.InsertOneAsync(thread);
        return thread.Id;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        var result = await _collection.DeleteOneAsync(x => x.Id == id);
        return result.DeletedCount > 0;
    }
}
