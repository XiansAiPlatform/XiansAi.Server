

namespace Shared.Data;

public interface IMongoDbContext
{
    MongoDBConfig GetMongoDBConfig();
}

public class MongoDbContext : IMongoDbContext
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<MongoDbContext> _logger;

    public MongoDbContext(IConfiguration configuration, ILogger<MongoDbContext> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public MongoDBConfig GetMongoDBConfig()
    {
        var mongoConfig = new MongoDBConfig
        {
            ConnectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
                ?? throw new InvalidOperationException("MongoDB connection string not found"),

            DatabaseName = _configuration.GetSection("MongoDB:DatabaseName").Value
                ?? throw new InvalidOperationException("MongoDB database name not found")
        };

        _configuration.GetSection("MongoDB").Bind(mongoConfig);

        _logger.LogDebug("MongoDB configuration loaded: MaxPool={MaxPool}, MinPool={MinPool}, ConnectTimeout={ConnectTimeout}",
            mongoConfig.MaxConnectionPoolSize, mongoConfig.MinConnectionPoolSize, mongoConfig.ConnectTimeout);

        return mongoConfig;
    }
}
