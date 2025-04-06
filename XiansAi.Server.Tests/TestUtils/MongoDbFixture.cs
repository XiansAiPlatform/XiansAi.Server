using Mongo2Go;
using MongoDB.Driver;
using XiansAi.Server.Database;

namespace XiansAi.Server.Tests.TestUtils;

// Test implementation of IMongoDbContext for tests
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
    public IMongoDatabase Database { get; }
    public MongoDBConfig MongoConfig { get; }
    public IMongoDbClientService MongoClientService { get; }

    public MongoDbFixture()
    {
        // Start MongoDB
        _runner = MongoDbRunner.Start();
        
        // Create configuration
        MongoConfig = new MongoDBConfig
        {
            ConnectionString = _runner.ConnectionString,
            DatabaseName = "IntegrationTest"
        };
        
        // Create MongoDB client service
        var context = new TestMongoDbContext(MongoConfig);
        MongoClientService = new MongoDbClientService(context);
        
        // Get database
        Database = MongoClientService.GetDatabase();
    }

    public void Dispose()
    {
        _runner.Dispose();
    }
} 