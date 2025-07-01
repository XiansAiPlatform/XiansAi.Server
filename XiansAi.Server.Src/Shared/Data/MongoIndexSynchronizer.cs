using MongoDB.Driver;
using Shared.Utils.Serialization;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;
using System.Reflection;

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
    public TimeSpan? ExpireAfter { get; init; }
}

public class MongoIndexSynchronizer(
    IDatabaseService databaseService,
    ILogger<MongoIndexSynchronizer> logger) : IMongoIndexSynchronizer
{ 
    private const string EmbeddedResourceFileName = "mongodb-indexes.yaml";

    public async Task EnsureIndexesAsync()
    { 
        logger.LogInformation("Starting index synchronization...");

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
        var indexDefinitions = await GetIndexDefinitionsAsync();

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
                if (def.ExpireAfter.HasValue) options.ExpireAfter = def.ExpireAfter;

                return new CreateIndexModel<object>(indexKeys, options);
            }).ToList()
        );
    }

    private async Task<Dictionary<string, List<MongoIndexDefinition>>> GetIndexDefinitionsAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var assemblyName = assembly.GetName().Name!;
        var embeddedResourceName = $"{assemblyName}.{EmbeddedResourceFileName}";
        await using var stream = assembly.GetManifestResourceStream(embeddedResourceName) ??
                                 throw new InvalidOperationException($"Embedded resource '{embeddedResourceName}' not found.");

        using var reader = new StreamReader(stream);
        var yamlContent = await reader.ReadToEndAsync();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .WithTypeConverter(new TimeSpanTypeConverter())
            .Build();

        return deserializer.Deserialize<Dictionary<string, List<MongoIndexDefinition>>>(yamlContent);
    }
} 