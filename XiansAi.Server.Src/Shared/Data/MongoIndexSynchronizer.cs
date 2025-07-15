using MongoDB.Driver;
using MongoDB.Bson;
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

                // Detect if we're using Cosmos DB by checking connection string or error patterns
                var isCosmosDb = await IsCosmosDbAsync(database);

                if (!isCosmosDb)
                {
                    // For regular MongoDB, drop unused indexes (except _id_)
                    foreach (var index in existingIndexes)
                    {
                        var indexName = index["name"].AsString;
                        if (indexName != "_id_" && !expectedIndexNames.Contains(indexName))
                        {
                            logger.LogInformation("Dropping unused index {IndexName} from collection {CollectionName}", 
                                indexName, collectionName);
                            try
                            {
                                await collection.Indexes.DropOneAsync(indexName);
                            }
                            catch (Exception dropEx)
                            {
                                logger.LogWarning(dropEx, "Failed to drop index {IndexName} from collection {CollectionName}, continuing", 
                                    indexName, collectionName);
                            }
                        }
                    }
                }
                else
                {
                    logger.LogInformation("Detected Cosmos DB - skipping index drops for collection {CollectionName}", collectionName);
                }

                // Create missing indexes with Cosmos DB error handling
                var indexesToCreate = indexes
                    .Where(i => !existingIndexNames.Contains(i.Options.Name))
                    .ToList();

                if (indexesToCreate.Count != 0)
                {
                    logger.LogInformation("Creating {Count} indexes for collection {CollectionName}", 
                        indexesToCreate.Count, collectionName);
                    
                    try
                    {
                        await collection.Indexes.CreateManyAsync(indexesToCreate);
                    }
                    catch (MongoCommandException ex) when (IsCosmosDbUniqueIndexError(ex))
                    {
                        logger.LogWarning("Cosmos DB unique index restriction encountered for collection {CollectionName}. " +
                                        "Indexes may already exist with different constraints. Continuing without recreating indexes. " +
                                        "Error: {Message}", collectionName, ex.Message);
                        // Continue execution - don't let index creation failures stop the app
                    }
                }
            }
            catch (MongoCommandException ex) when (IsCosmosDbUniqueIndexError(ex))
            {
                logger.LogWarning("Cosmos DB unique index restriction for collection {CollectionName}. " +
                                "This is expected when indexes already exist. Continuing. Error: {Message}", 
                                collectionName, ex.Message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to ensure indexes for collection: {CollectionName}", collectionName);
                
                // For production environments, don't let index failures stop the application
                if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production")
                {
                    logger.LogWarning("Continuing application startup despite index creation failure in production environment");
                    continue; // Continue with next collection
                }
                throw;
            }
        }
        
        logger.LogInformation("Index synchronization completed");
    }

    /// <summary>
    /// Detects if we're connected to Cosmos DB by checking for specific characteristics
    /// </summary>
    private static async Task<bool> IsCosmosDbAsync(IMongoDatabase database)
    {
        try
        {
            // Cosmos DB has specific admin database characteristics
            var adminDb = database.Client.GetDatabase("admin");
            var result = await adminDb.RunCommandAsync<BsonDocument>(
                new BsonDocument("buildInfo", 1));
            
            // Check if response contains Cosmos DB indicators
            return result.Contains("version") && 
                   (result["version"]?.ToString()?.Contains("cosmos") ?? false || 
                    result.ToString().Contains("DocumentDB"));
        }
        catch
        {
            // If we can't determine, check connection string as fallback
            var connectionString = database.Client.Settings.ToString();
            return connectionString?.Contains("cosmos") == true || 
                   connectionString?.Contains("documents.azure.com") == true ||
                   connectionString?.Contains("mongo.cosmos.azure.com") == true;
        }
    }

    /// <summary>
    /// Checks if the exception is related to Cosmos DB unique index restrictions
    /// </summary>
    private static bool IsCosmosDbUniqueIndexError(MongoCommandException ex)
    {
        return ex.Code == 13 && // Forbidden
               (ex.Message.Contains("unique index cannot be modified") ||
                ex.Message.Contains("Forbidden") ||
                ex.Message.Contains("remove the collection and re-create") ||
                ex.Message.Contains("The unique index cannot be modified"));
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