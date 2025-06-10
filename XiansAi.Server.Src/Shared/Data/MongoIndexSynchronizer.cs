using MongoDB.Driver;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Shared.Data;

public interface IMongoIndexSynchronizer
{
    Task EnsureIndexesAsync();
}

public class MongoIndexDefinition
{
    public required string Name { get; init; }
    public required Dictionary<string, string> Keys { get; init; } = [];
    public bool? Unique { get; init; }
    public bool? Sparse { get; init; }
    public bool? Background { get; init; }
}

public class MongoIndexSynchronizer(
    IDatabaseService databaseService,
    ILogger<MongoIndexSynchronizer> logger) : IMongoIndexSynchronizer
{ 
    private static readonly string IndexDefinitionPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mongodb-indexes.yaml");

    public async Task EnsureIndexesAsync()
    { 
        var database = await databaseService.GetDatabaseAsync();
        
        var collectionsCursor = await database.ListCollectionNamesAsync();
        var collections = await collectionsCursor.ToListAsync();

        var expectedIndexes = await GetExpectedIndexesAsync();
        foreach (var (collectionName, indexes) in expectedIndexes)
        {
            try
            {
                // Check if collection exists
                if (!collections.Contains(collectionName))
                {
                    logger.LogInformation("Creating collection {CollectionName} before ensuring indexes", collectionName);
                    await database.CreateCollectionAsync(collectionName);
                }

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

                if (indexesToCreate.Count != 0)
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

    private async Task<Dictionary<string, List<CreateIndexModel<object>>>> GetExpectedIndexesAsync()
    {
        if (!File.Exists(IndexDefinitionPath))
        {
            throw new FileNotFoundException($"MongoDB index definition file not found at: {IndexDefinitionPath}");
        }

        var yamlContent = await File.ReadAllTextAsync(IndexDefinitionPath);
        
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        var indexDefinitions = deserializer.Deserialize<Dictionary<string, List<MongoIndexDefinition>>>(yamlContent);

        return indexDefinitions.OrderBy(kvp => kvp.Key).ToDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value.Select(def =>
            {
                var indexKeysBuilder = new IndexKeysDefinitionBuilder<object>();
                var indexKeys = indexKeysBuilder.Combine(
                    def.Keys.Select(k => k.Value == "asc"
                        ? indexKeysBuilder.Ascending(k.Key)
                        : indexKeysBuilder.Descending(k.Key))
                );

                var options = new CreateIndexOptions { Name = def.Name };
                if (def.Unique.HasValue) options.Unique = def.Unique.Value;
                if (def.Sparse.HasValue) options.Sparse = def.Sparse.Value;
                if (def.Background.HasValue) options.Background = def.Background.Value;

                return new CreateIndexModel<object>(indexKeys, options);
            }).ToList()
        );
    }
} 