using MongoDB.Driver;
using MongoDB.Bson;
using Shared.Data;
using Microsoft.Extensions.Configuration;
using Mongo2Go;
using System.Threading;

namespace XiansAi.Server.Tests.TestUtils;

public class TestMongoDbContext : IMongoDbContext
{
    private readonly MongoDBConfig _config;

    public TestMongoDbContext(MongoDBConfig config)
    {
        _config = config;
    }

    public MongoDBConfig GetMongoDBConfig()
    {
        return _config;
    }
}

public class MongoDbFixture : IDisposable
{
    private readonly MongoDbRunner _runner;
    private readonly IMongoClient _client;
    private readonly IMongoDatabase _database;
    private readonly string _databaseName = "test_db";
    private readonly IMongoDbClientService _mongoClientService;

    public MongoDBConfig MongoConfig { get; }
    public IMongoDatabase Database => _database;
    public IMongoDbClientService MongoClientService => _mongoClientService;

    public MongoDbFixture()
    {
        try
        {
            // Start MongoDB instance with replica set
        _runner = MongoDbRunner.Start(singleNodeReplSet: true);
            
            // Wait for replica set to be ready
            WaitForReplicaSet();
        
        // Create client and database
            var settings = MongoClientSettings.FromConnectionString(_runner.ConnectionString);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(10);
            settings.ConnectTimeout = TimeSpan.FromSeconds(10);
            _client = new MongoClient(settings);
        _database = _client.GetDatabase(_databaseName);

        // Configure MongoDB settings
        MongoConfig = new MongoDBConfig
        {
            ConnectionString = _runner.ConnectionString,
            DatabaseName = _databaseName
        };

        // Create client service
        _mongoClientService = new MongoDbClientService(new TestMongoDbContext(MongoConfig));

        // Initialize collections and indexes
        InitializeCollections();

            // Verify database is accessible
            var ping = _database.RunCommand<BsonDocument>(new BsonDocument("ping", 1));
            if (!ping.Contains("ok") || ping["ok"].AsDouble != 1.0)
            {
                throw new Exception("Failed to connect to MongoDB test instance");
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to initialize MongoDB test instance: {ex.Message}", ex);
        }
    }

    private void WaitForReplicaSet()
    {
        var settings = MongoClientSettings.FromConnectionString(_runner.ConnectionString);
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(30);
        settings.ConnectTimeout = TimeSpan.FromSeconds(30);
        var tempClient = new MongoClient(settings);

        var maxAttempts = 30;
        var currentAttempt = 0;
        var delay = TimeSpan.FromSeconds(1);

        while (currentAttempt < maxAttempts)
        {
            try
            {
                var db = tempClient.GetDatabase("admin");
                var result = db.RunCommand<BsonDocument>(new BsonDocument("replSetGetStatus", 1));
                
                if (result["ok"].AsDouble == 1.0 && 
                    result["members"].AsBsonArray.Any(m => m["stateStr"].AsString == "PRIMARY"))
                {
                    return;
                }
            }
            catch
            {
                // Ignore exceptions and continue waiting
            }

            Thread.Sleep(delay);
            currentAttempt++;
        }

        throw new Exception("Timeout waiting for replica set to be ready");
    }

    private void InitializeCollections()
    {
        try
    {
        // Create collections with required indexes
        var collections = new[]
        {
            "agents",
            "conversations",
            "messages",
            "logs",
            "tenants",
            "users",
            "webhooks",
            "activity_history",
            "flow_definitions",
                "instructions",
                "knowledge"  // Added missing collection
        };

        foreach (var collectionName in collections)
        {
            if (!CollectionExists(collectionName))
            {
                _database.CreateCollection(collectionName);
            }
        }

        // Create indexes
        var messagesCollection = _database.GetCollection<BsonDocument>("messages");
        var conversationsCollection = _database.GetCollection<BsonDocument>("conversations");
        var logsCollection = _database.GetCollection<BsonDocument>("logs");
        var tenantsCollection = _database.GetCollection<BsonDocument>("tenants");
        var usersCollection = _database.GetCollection<BsonDocument>("users");
            var webhooksCollection = _database.GetCollection<BsonDocument>("webhooks");
            var knowledgeCollection = _database.GetCollection<BsonDocument>("knowledge");

        // Messages indexes
        messagesCollection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("thread_id")
                    .Ascending("created_at")),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("tenant_id"))
        });

        // Conversations indexes
        conversationsCollection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("tenant_id")),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("created_at"))
        });

        // Logs indexes
        logsCollection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("tenant_id")),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                        .Ascending("created_at")),
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending("workflow_run_id"))
        });
        
        // Tenants indexes
        tenantsCollection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("tenant_id"),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("domain"),
                new CreateIndexOptions { Unique = true })
        });

        // Users indexes
        usersCollection.Indexes.CreateMany(new[]
        {
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("user_id"),
                new CreateIndexOptions { Unique = true }),
            new CreateIndexModel<BsonDocument>(
                Builders<BsonDocument>.IndexKeys
                    .Ascending("email"),
                new CreateIndexOptions { Unique = true })
        });

            // Webhooks indexes
            webhooksCollection.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending("tenant_id")),
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending("workflow_id"))
            });

            // Knowledge indexes
            knowledgeCollection.Indexes.CreateMany(new[]
            {
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending("tenant_id")),
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending("agent_id")),
                new CreateIndexModel<BsonDocument>(
                    Builders<BsonDocument>.IndexKeys
                        .Ascending("name"))
            });
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to initialize collections and indexes: {ex.Message}", ex);
        }
    }

    private bool CollectionExists(string collectionName)
    {
        var filter = new BsonDocument("name", collectionName);
        var collections = _database.ListCollections(new ListCollectionsOptions { Filter = filter });
        return collections.Any();
    }

    public void Dispose()
    {
        try
    {
        _runner?.Dispose();
        }
        catch
        {
            // Ignore disposal errors
        }
    }
} 