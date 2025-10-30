namespace Shared.Data;

public interface IMongoDBConfig
{
    string ConnectionString { get; set; }
    string DatabaseName { get; set; }
    
    // Connection Pool Settings
    int MaxConnectionPoolSize { get; set; }
    int MinConnectionPoolSize { get; set; }
    TimeSpan MaxConnectionIdleTime { get; set; }
    TimeSpan WaitQueueTimeout { get; set; }
    
    // Operation Timeouts
    TimeSpan ConnectTimeout { get; set; }
    TimeSpan SocketTimeout { get; set; }
    TimeSpan ServerSelectionTimeout { get; set; }
    
    // Heartbeat Settings
    TimeSpan HeartbeatInterval { get; set; }
    TimeSpan HeartbeatTimeout { get; set; }
    
    // Retry Settings
    bool RetryWrites { get; set; }
    bool RetryReads { get; set; }
}

public class MongoDBConfig: IMongoDBConfig
{
    public required string ConnectionString { get; set; }
    public required string DatabaseName { get; set; }
    
    // Connection Pool Settings - Default values optimized for Cosmos DB
    public int MaxConnectionPoolSize { get; set; } = 100;
    public int MinConnectionPoolSize { get; set; } = 5;
    public TimeSpan MaxConnectionIdleTime { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan WaitQueueTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    // Operation Timeouts - Increased for better resilience during high load or transient network issues
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan SocketTimeout { get; set; } = TimeSpan.FromSeconds(60);
    public TimeSpan ServerSelectionTimeout { get; set; } = TimeSpan.FromSeconds(60);
    
    // Heartbeat Settings - Regular health checks with increased timeout for reliability
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(20);
    
    // Retry Settings - Important for Cosmos DB reliability
    public bool RetryWrites { get; set; } = true;
    public bool RetryReads { get; set; } = true;
}
