using Mongo2Go;
using MongoDB.Driver;
using XiansAi.Server.Database;
using Features.Shared.Auth;
using Moq;

namespace XiansAi.Server.Tests.TestUtils;

public class MongoDbFixture : IDisposable
{
    private readonly MongoDbRunner _runner;
    public IMongoDatabase Database { get; }
    public MongoDBConfig MongoConfig { get; }
    public Mock<ITenantContext> TenantContextMock { get; }
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
        
        // Set up mocks
        TenantContextMock = new Mock<ITenantContext>();
        TenantContextMock.Setup(x => x.TenantId).Returns("test-tenant");
        TenantContextMock.Setup(x => x.GetMongoDBConfig()).Returns(MongoConfig);
        
        // Create MongoDB client service
        MongoClientService = new MongoDbClientService(MongoConfig, TenantContextMock.Object);
        
        // Get database
        Database = MongoClientService.GetDatabase();
    }

    public void Dispose()
    {
        _runner.Dispose();
    }
} 