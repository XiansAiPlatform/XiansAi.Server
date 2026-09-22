using MongoDB.Bson;
using MongoDB.Driver;

namespace Shared.Data;

/// <summary>
/// Identifies what is actually serving the MongoDB wire protocol, so callers can adapt to engines
/// that are API-compatible but do not describe themselves the way a real mongod does.
/// </summary>
public static class MongoDeployment
{
    /// <summary>
    /// Whether the deployment can serve change streams.
    ///
    /// Deliberately fail-open: only a deployment positively identified as a standalone mongod
    /// answers false. Azure Cosmos DB and Azure DocumentDB serve change streams without reporting
    /// a replica set name or identifying as a mongos, and callers disable live message push for
    /// the life of the process when this returns false, so an unrecognised topology has to be left
    /// for the server itself to accept or reject.
    /// </summary>
    public static async Task<bool> SupportsChangeStreamsAsync(
        IMongoDatabase database,
        MongoProvider provider,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);

        if (provider == MongoProvider.DocumentDB)
        {
            logger.LogInformation("MongoDB provider is configured as DocumentDB; skipping the mongod topology check.");
            return true;
        }

        var topology = await DescribeTopologyAsync(database, logger, cancellationToken);
        if (topology == null)
        {
            logger.LogWarning("Could not describe the MongoDB topology; assuming change streams are available.");
            return true;
        }

        if (IsReplicaSetMember(topology) || IsMongos(topology))
        {
            return true;
        }

        if (await IsApiCompatibleEngineAsync(database, cancellationToken))
        {
            logger.LogInformation(
                "MongoDB-compatible engine detected (Cosmos DB / DocumentDB); assuming change streams are available.");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the server is Azure Cosmos DB or Azure DocumentDB rather than a real mongod. Used to
    /// skip operations mongod supports but these engines reject, such as dropping an index.
    /// </summary>
    public static async Task<bool> IsApiCompatibleEngineAsync(
        IMongoDatabase database,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(database);

        try
        {
            var buildInfo = await database.Client.GetDatabase("admin").RunCommandAsync<BsonDocument>(
                new BsonDocument("buildInfo", 1),
                cancellationToken: cancellationToken);

            return NamesCompatibleEngine(buildInfo.ToString());
        }
        catch (Exception)
        {
            // The server told us nothing, so fall back to the endpoint it is hosted on.
            return NamesCompatibleEngine(database.Client.Settings.ToString());
        }
    }

    /// <summary>
    /// Runs the topology command, or returns null when the server implements neither form of it.
    /// Connection and timeout failures are left to propagate so callers can retry them as the
    /// transient errors they are, rather than mistaking an outage for a topology answer.
    /// </summary>
    private static async Task<BsonDocument?> DescribeTopologyAsync(
        IMongoDatabase database,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // `hello` only exists from MongoDB 5.0 onwards; engines pinned to an older wire version
        // still answer the legacy `isMaster`.
        foreach (var commandName in new[] { "hello", "isMaster" })
        {
            try
            {
                return await database.RunCommandAsync<BsonDocument>(
                    new BsonDocument(commandName, 1),
                    cancellationToken: cancellationToken);
            }
            catch (MongoCommandException ex)
            {
                logger.LogDebug(ex, "MongoDB rejected `{Command}` while describing the topology.", commandName);
            }
        }

        return null;
    }

    private static bool IsReplicaSetMember(BsonDocument topology)
    {
        return topology.TryGetValue("setName", out var setName)
            && setName.IsString
            && !string.IsNullOrWhiteSpace(setName.AsString);
    }

    private static bool IsMongos(BsonDocument topology)
    {
        return topology.TryGetValue("msg", out var msg) && msg.IsString && msg.AsString == "isdbgrid";
    }

    private static bool NamesCompatibleEngine(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        return text.Contains("cosmos", StringComparison.OrdinalIgnoreCase)
            || text.Contains("documentdb", StringComparison.OrdinalIgnoreCase)
            || text.Contains("documents.azure.com", StringComparison.OrdinalIgnoreCase);
    }
}
