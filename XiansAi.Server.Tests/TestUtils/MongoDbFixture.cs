using Mongo2Go;
using MongoDB.Driver;
using XiansAi.Server.Database;

namespace XiansAi.Server.Tests.TestUtils;

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
        MongoClientService = new MongoDbClientService(MongoConfig);
        
        // Get database
        Database = MongoClientService.GetDatabase();
    }

    public void Dispose()
    {
        _runner.Dispose();
    }
} 