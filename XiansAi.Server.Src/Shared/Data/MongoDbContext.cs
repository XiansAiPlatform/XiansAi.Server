

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
        var connectionString = _configuration.GetSection("MongoDB:ConnectionString").Value
            ?? throw new InvalidOperationException("MongoDB connection string not found");

        var databaseName = _configuration.GetSection("MongoDB:DatabaseName").Value
            ?? throw new InvalidOperationException("MongoDB database name not found");
        
        // Create a new mongo config with default values
        var mongoConfig = new MongoDBConfig
        {
            ConnectionString = connectionString,
            DatabaseName = databaseName
        };

        // Load optional connection pool settings with fallbacks to defaults
        if (int.TryParse(_configuration["MongoDB:MaxConnectionPoolSize"], out var maxPool))
            mongoConfig.MaxConnectionPoolSize = maxPool;
            
        if (int.TryParse(_configuration["MongoDB:MinConnectionPoolSize"], out var minPool))
            mongoConfig.MinConnectionPoolSize = minPool;
            
        if (TimeSpan.TryParse(_configuration["MongoDB:MaxConnectionIdleTime"], out var idleTime))
            mongoConfig.MaxConnectionIdleTime = idleTime;
            
        if (TimeSpan.TryParse(_configuration["MongoDB:WaitQueueTimeout"], out var waitTimeout))
            mongoConfig.WaitQueueTimeout = waitTimeout;
            
        // Load optional operation timeout settings
        if (TimeSpan.TryParse(_configuration["MongoDB:ConnectTimeout"], out var connectTimeout))
            mongoConfig.ConnectTimeout = connectTimeout;
            
        if (TimeSpan.TryParse(_configuration["MongoDB:SocketTimeout"], out var socketTimeout))
            mongoConfig.SocketTimeout = socketTimeout;
            
        if (TimeSpan.TryParse(_configuration["MongoDB:ServerSelectionTimeout"], out var serverTimeout))
            mongoConfig.ServerSelectionTimeout = serverTimeout;
            
        // Load optional heartbeat settings
        if (TimeSpan.TryParse(_configuration["MongoDB:HeartbeatInterval"], out var heartbeatInterval))
            mongoConfig.HeartbeatInterval = heartbeatInterval;
            
        if (TimeSpan.TryParse(_configuration["MongoDB:HeartbeatTimeout"], out var heartbeatTimeout))
            mongoConfig.HeartbeatTimeout = heartbeatTimeout;
            
        // Load optional retry settings
        if (bool.TryParse(_configuration["MongoDB:RetryWrites"], out var retryWrites))
            mongoConfig.RetryWrites = retryWrites;

        _logger.LogDebug("MongoDB configuration loaded: MaxPool={MaxPool}, MinPool={MinPool}, ConnectTimeout={ConnectTimeout}",
            mongoConfig.MaxConnectionPoolSize, mongoConfig.MinConnectionPoolSize, mongoConfig.ConnectTimeout);

        return mongoConfig;
    }
}
