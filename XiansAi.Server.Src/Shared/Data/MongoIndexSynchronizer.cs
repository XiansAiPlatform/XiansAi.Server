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

    /// <summary>
    /// Returns true when an index already present in MongoDB still matches this definition.
    /// Only the options this definition can express are compared, so unrelated server-side
    /// metadata is never mistaken for drift. "background" is excluded because MongoDB has
    /// ignored and stopped reporting it since 4.2.
    /// </summary>
    public bool MatchesExistingIndex(BsonDocument existingIndex)
    {
        if (existingIndex == null)
            return false;

        return KeysMatch(existingIndex)
            && existingIndex.GetValue("unique", false).ToBoolean() == (Unique ?? false)
            && existingIndex.GetValue("sparse", false).ToBoolean() == (Sparse ?? false)
            && ExpiryMatches(existingIndex);
    }

    private bool KeysMatch(BsonDocument existingIndex)
    {
        if (!existingIndex.TryGetValue("key", out var keyValue) || keyValue is not BsonDocument existingKeys)
            return false;

        if (existingKeys.ElementCount != Keys.Count)
            return false;

        return existingKeys
            .Zip(Keys, (existing, expected) =>
                existing.Name == expected.Key
                && existing.Value.IsNumeric
                && existing.Value.ToInt32() == DirectionValue(expected.Value))
            .All(keyMatches => keyMatches);
    }

    private static int DirectionValue(string direction) =>
        string.Equals(direction, "desc", StringComparison.OrdinalIgnoreCase) ? -1 : 1;

    private bool ExpiryMatches(BsonDocument existingIndex)
    {
        if (!existingIndex.TryGetValue("expireAfterSeconds", out var existingExpiry))
            return !ExpireAfter.HasValue;

        if (!ExpireAfter.HasValue || !existingExpiry.IsNumeric)
            return false;

        return Math.Abs(existingExpiry.ToDouble() - ExpireAfter.Value.TotalSeconds) < 1;
    }
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

        // Detect if we're using Cosmos DB by checking connection string or error patterns
        var isCosmosDb = await IsCosmosDbAsync(database);

        var expectedIndexes = await GetIndexDefinitionsAsync();
        foreach (var (collectionName, definitions) in expectedIndexes.OrderBy(kvp => kvp.Key))
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
                await SyncCollectionIndexesAsync(collection, collectionName, definitions, isCosmosDb);
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

    private async Task SyncCollectionIndexesAsync(
        IMongoCollection<object> collection,
        string collectionName,
        List<MongoIndexDefinition> definitions,
        bool isCosmosDb)
    {
        var existingIndexes = await (await collection.Indexes.ListAsync()).ToListAsync();
        var existingIndexesByName = existingIndexes.ToDictionary(index => index["name"].AsString);
        var expectedIndexNames = definitions.Select(definition => definition.Name).ToHashSet();

        if (!isCosmosDb)
        {
            await DropUnusedIndexesAsync(collection, collectionName, existingIndexes, expectedIndexNames);
            await DropMismatchedIndexesAsync(collection, collectionName, definitions, existingIndexesByName);
        }
        else
        {
            logger.LogInformation("Detected Cosmos DB - skipping index drops for collection {CollectionName}", collectionName);
            LogMismatchedIndexes(collectionName, definitions, existingIndexesByName);
        }

        // Create missing indexes with Cosmos DB error handling
        var indexesToCreate = definitions
            .Where(definition => !existingIndexesByName.ContainsKey(definition.Name))
            .Select(BuildIndexModel)
            .ToList();

        if (indexesToCreate.Count == 0)
        {
            return;
        }

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

    private async Task DropUnusedIndexesAsync(
        IMongoCollection<object> collection,
        string collectionName,
        List<BsonDocument> existingIndexes,
        HashSet<string> expectedIndexNames)
    {
        foreach (var index in existingIndexes)
        {
            var indexName = index["name"].AsString;
            if (indexName == "_id_" || expectedIndexNames.Contains(indexName))
            {
                continue;
            }

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

    /// <summary>
    /// Returns the indexes that already exist but whose options no longer match their definition.
    /// </summary>
    private static List<(MongoIndexDefinition Definition, BsonDocument ExistingIndex)> FindMismatchedIndexes(
        List<MongoIndexDefinition> definitions,
        Dictionary<string, BsonDocument> existingIndexesByName)
    {
        return definitions
            .Where(definition => existingIndexesByName.ContainsKey(definition.Name)
                && !definition.MatchesExistingIndex(existingIndexesByName[definition.Name]))
            .Select(definition => (definition, existingIndexesByName[definition.Name]))
            .ToList();
    }

    /// <summary>
    /// Drops indexes whose options no longer match their definition, so the create step rebuilds them.
    /// MongoDB cannot change options such as "unique" in place, and matching on name alone would let a
    /// stale constraint outlive the definition that created it.
    /// Successfully dropped names are removed from <paramref name="existingIndexesByName"/> so the
    /// caller recreates them.
    /// </summary>
    private async Task DropMismatchedIndexesAsync(
        IMongoCollection<object> collection,
        string collectionName,
        List<MongoIndexDefinition> definitions,
        Dictionary<string, BsonDocument> existingIndexesByName)
    {
        foreach (var (definition, existingIndex) in FindMismatchedIndexes(definitions, existingIndexesByName))
        {
            logger.LogWarning("Index {IndexName} on collection {CollectionName} no longer matches its definition " +
                            "and will be recreated. Existing definition: {ExistingIndex}",
                definition.Name, collectionName, existingIndex.ToJson());

            try
            {
                await collection.Indexes.DropOneAsync(definition.Name);
                existingIndexesByName.Remove(definition.Name);
            }
            catch (Exception dropEx)
            {
                logger.LogError(dropEx, "Failed to drop mismatched index {IndexName} from collection {CollectionName}. " +
                                "It keeps its current options until the drop succeeds",
                    definition.Name, collectionName);
            }
        }
    }

    /// <summary>
    /// Reports mismatched indexes without touching them. Cosmos DB refuses to modify a unique index,
    /// so correcting one there means recreating the collection with the intended index set; surfacing
    /// the drift is all the synchronizer can do.
    /// </summary>
    private void LogMismatchedIndexes(
        string collectionName,
        List<MongoIndexDefinition> definitions,
        Dictionary<string, BsonDocument> existingIndexesByName)
    {
        foreach (var (definition, existingIndex) in FindMismatchedIndexes(definitions, existingIndexesByName))
        {
            logger.LogWarning("Index {IndexName} on collection {CollectionName} does not match its definition and " +
                            "cannot be corrected automatically on Cosmos DB. Recreate the collection with the " +
                            "intended indexes to clear this. Existing definition: {ExistingIndex}",
                definition.Name, collectionName, existingIndex.ToJson());
        }
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

    private static CreateIndexModel<object> BuildIndexModel(MongoIndexDefinition def)
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