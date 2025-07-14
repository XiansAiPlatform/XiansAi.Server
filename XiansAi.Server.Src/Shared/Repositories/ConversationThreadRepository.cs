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

}

public interface IConversationThreadRepository
{
    Task<List<ConversationThread>> GetByTenantAndAgentAsync(string tenantId, string agent, int? page = null, int? pageSize = null);
    Task<string> CreateOrGetAsync(ConversationThread thread);
    Task<bool> DeleteAsync(string id);
}

public class ConversationThreadRepository : IConversationThreadRepository
{
    private readonly IMongoCollection<ConversationThread> _collection;
    private readonly ILogger<ConversationThreadRepository> _logger;

    public ConversationThreadRepository(IDatabaseService databaseService, ILogger<ConversationThreadRepository> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        var database = databaseService.GetDatabaseAsync().GetAwaiter().GetResult();
        _collection = database.GetCollection<ConversationThread>("conversation_thread");
    }


    private async Task<ConversationThread?> GetByCompositeKeyAsync(string tenantId, string workflowId, string participantId)
    {
        var filter = Builders<ConversationThread>.Filter.And(
            Builders<ConversationThread>.Filter.Eq(x => x.TenantId, tenantId),
            Builders<ConversationThread>.Filter.Eq(x => x.WorkflowId, workflowId),
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
        var existingThread = await GetByCompositeKeyAsync(thread.TenantId, thread.WorkflowId, thread.ParticipantId);
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
                existingThread = await GetByCompositeKeyAsync(thread.TenantId, thread.WorkflowId, thread.ParticipantId);
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
