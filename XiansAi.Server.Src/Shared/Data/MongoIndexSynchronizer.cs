using MongoDB.Driver;

namespace Shared.Data;

public interface IMongoIndexSynchronizer
{
    Task EnsureIndexesAsync();
}

public class MongoIndexSynchronizer(
    IDatabaseService databaseService,
    ILogger<MongoIndexSynchronizer> logger) : IMongoIndexSynchronizer
{ 
    public async Task EnsureIndexesAsync()
    { 
        var database = await databaseService.GetDatabaseAsync();
        
        var expectedIndexes = GetExpectedIndexes();
        foreach (var (collectionName, indexes) in expectedIndexes)
        {
            try
            {
                var collection = database.GetCollection<object>(collectionName);
                
                // Get existing indexes
                var existingIndexes = await (await collection.Indexes.ListAsync()).ToListAsync();
                var existingIndexNames = existingIndexes.Select(i => i["name"].AsString).ToHashSet();
                var expectedIndexNames = indexes.Select(i => i.Options.Name).ToHashSet();

                // Drop indexes that are not in expected set (except _id_)
                foreach (var index in existingIndexes)
                {
                    var indexName = index["name"].AsString;
                    if (indexName != "_id_" && !expectedIndexNames.Contains(indexName))
                    {
                        logger.LogInformation("Dropping unused index {IndexName} from collection {CollectionName}", 
                            indexName, collectionName);
                        await collection.Indexes.DropOneAsync(indexName);
                    }
                }

                // Create missing indexes
                var indexesToCreate = indexes
                    .Where(i => !existingIndexNames.Contains(i.Options.Name))
                    .ToList();

                if (indexesToCreate.Any())
                {
                    logger.LogInformation("Creating {Count} indexes for collection {CollectionName}", 
                        indexesToCreate.Count, collectionName);
                    await collection.Indexes.CreateManyAsync(indexesToCreate);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to ensure indexes for collection: {CollectionName}", collectionName);
                throw;
            }
        }
    }

    private static Dictionary<string, List<CreateIndexModel<object>>> GetExpectedIndexes()
    {
        return new Dictionary<string, List<CreateIndexModel<object>>>
        {
            {
                "agents",
                [
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("name")
                            .Ascending("tenant"),
                        new CreateIndexOptions { Unique = true, Name = "name_1_tenant_1" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Descending("tenant"),
                        new CreateIndexOptions { Name = "tenant_-1" }
                    )
                ]
            },
            {
                "flow_definitions",
                [
                    new(
                        Builders<object>.IndexKeys.Descending("created_at"),
                        new CreateIndexOptions { Name = "created_at_-1" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Ascending("agent"),
                        new CreateIndexOptions { Name = "agent_1" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Ascending("workflow_type"),
                        new CreateIndexOptions { Name = "workflow_type_1" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Descending("updated_at"),
                        new CreateIndexOptions { Name = "updated_at_-1" }
                    )
                ]
            },
            {
                "conversation_message",
                [
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("tenant_id")
                            .Ascending("thread_id")
                            .Ascending("participant_id")
                            .Descending("created_at"),
                        new CreateIndexOptions { Name = "thread_participant_message_lookup" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Ascending("tenant_id"),
                        new CreateIndexOptions { Name = "tenant_lookup" }
                    ),
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("tenant_id")
                            .Ascending("status"),
                        new CreateIndexOptions { Name = "message_status" }
                    )
                ]
            },
            {
                "conversation_thread",
                [
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("tenant_id")
                            .Ascending("status"),
                        new CreateIndexOptions { Name = "thread_status_lookup" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Descending("updated_at"),
                        new CreateIndexOptions { Name = "thread_updated_at" }
                    ),
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("tenant_id")
                            .Ascending("agent")
                            .Ascending("workflow_type")
                            .Ascending("participant_id"),
                        new CreateIndexOptions { Unique = true, Name = "thread_composite_key" }
                    ),
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("tenant_id")
                            .Ascending("agent"),
                        new CreateIndexOptions { Name = "tenant_agent_lookup" }
                    )
                ]
            },
            {
                "webhooks",
                [
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("tenant_id"),
                        new CreateIndexOptions { Name = "tenant_id_1" }
                    ),
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("workflow_id")
                            .Ascending("tenant_id"),
                        new CreateIndexOptions { Name = "workflow_id_1_tenant_id_1", Sparse = true }
                    )
                ]
            },
            {
                "certificates",
                [
                    new(
                        Builders<object>.IndexKeys.Ascending("Thumbprint"),
                        new CreateIndexOptions { Unique = true, Name = "Thumbprint_1" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Ascending("TenantId"),
                        new CreateIndexOptions { Name = "TenantId_1" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Ascending("ExpiresAt"),
                        new CreateIndexOptions { Name = "ExpiresAt_1" }
                    )
                ]
            },
            {
                "logs",
                [
                    new(
                        Builders<object>.IndexKeys
                            .Ascending("level")
                            .Ascending("tenant_id")
                            .Descending("created_at")
                            .Ascending("workflow_type"),
                        new CreateIndexOptions { Name = "level_1_tenant_id_1_created_at_-1_workflow_type_1_autocreated" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Ascending("workflow_run_id"),
                        new CreateIndexOptions { Name = "workflow_run_id_1_autocreated" }
                    )
                ]
            },
            {
                "tenants",
                [
                    new(
                        Builders<object>.IndexKeys.Ascending("tenant_id"),
                        new CreateIndexOptions { Unique = true, Name = "tenant_id_1" }
                    ),
                    new(
                        Builders<object>.IndexKeys.Ascending("domain"),
                        new CreateIndexOptions { Unique = true, Name = "domain_1" }
                    )
                ]
            }
        };
    }
} 