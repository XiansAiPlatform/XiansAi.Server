# MongoDB Connection Handling Fix

## Issue Identified

The application was experiencing critical MongoDB/Cosmos DB connection issues that caused the database to become unresponsive, requiring application restarts to resolve. 

### Root Cause Analysis

The issue was caused by improper MongoDB connection management:

1. **MongoClient Anti-Pattern**: A new `MongoClient` instance was created on every database operation
2. **Connection Pool Exhaustion**: No connection pooling or reuse
3. **Resource Leaks**: MongoClient instances were never disposed
4. **Service Scope Issues**: MongoDB service was registered as `Scoped` instead of `Singleton`

### Impact

- Connection pool exhaustion in Cosmos DB
- Memory leaks in the application
- Database becoming unresponsive under load
- Requiring application restarts to clear stuck connections

## Fix Implemented

### 1. Singleton MongoClient Pattern

- **Before**: New `MongoClient` created on every operation
- **After**: Single `MongoClient` instance created once and reused (thread-safe)

### 2. Proper Connection Pool Configuration

Added configurable connection pool settings optimized for Cosmos DB:

```csharp
// Connection Pool Settings
settings.MaxConnectionPoolSize = Config.MaxConnectionPoolSize; // Default: 100
settings.MinConnectionPoolSize = Config.MinConnectionPoolSize; // Default: 5
settings.MaxConnectionIdleTime = Config.MaxConnectionIdleTime; // Default: 10 minutes
settings.WaitQueueTimeout = Config.WaitQueueTimeout; // Default: 30 seconds
```

### 3. Operation Timeouts

Added configurable timeouts to prevent hung connections:

```csharp
// Operation Timeouts
settings.ConnectTimeout = Config.ConnectTimeout; // Default: 30 seconds
settings.SocketTimeout = Config.SocketTimeout; // Default: 30 seconds
settings.ServerSelectionTimeout = Config.ServerSelectionTimeout; // Default: 30 seconds
```

### 4. Health Monitoring

Added heartbeat configuration for connection health checks:

```csharp
// Heartbeat Settings
settings.HeartbeatInterval = Config.HeartbeatInterval; // Default: 10 seconds
settings.HeartbeatTimeout = Config.HeartbeatTimeout; // Default: 10 seconds
```

### 5. Proper Resource Disposal

Implemented `IDisposable` pattern for proper cleanup:

```csharp
public void Dispose()
{
    if (_mongoClient is IDisposable disposableClient)
    {
        disposableClient.Dispose();
    }
}
```

## Configuration Options

You can customize MongoDB connection settings in your `appsettings.json`:

```json
{
  "MongoDB": {
    "ConnectionString": "your-cosmos-db-connection-string",
    "DatabaseName": "your-database-name",
    
    // Connection Pool Settings (optional)
    "MaxConnectionPoolSize": 100,
    "MinConnectionPoolSize": 5,
    "MaxConnectionIdleTime": "00:10:00",
    "WaitQueueTimeout": "00:00:30",
    
    // Operation Timeouts (optional)
    "ConnectTimeout": "00:00:30",
    "SocketTimeout": "00:00:30",
    "ServerSelectionTimeout": "00:00:30",
    
    // Heartbeat Settings (optional)
    "HeartbeatInterval": "00:00:10",
    "HeartbeatTimeout": "00:00:10",
    
    // Retry Settings (optional)
    "RetryWrites": true
  }
}
```

## Default Values

If not specified in configuration, the following defaults are used:

| Setting | Default Value | Description |
|---------|---------------|-------------|
| `MaxConnectionPoolSize` | 100 | Maximum connections in pool |
| `MinConnectionPoolSize` | 5 | Minimum connections maintained |
| `MaxConnectionIdleTime` | 10 minutes | Close idle connections after |
| `WaitQueueTimeout` | 30 seconds | Timeout waiting for available connection |
| `ConnectTimeout` | 30 seconds | Timeout for establishing connection |
| `SocketTimeout` | 30 seconds | Timeout for socket operations |
| `ServerSelectionTimeout` | 30 seconds | Timeout for server selection |
| `HeartbeatInterval` | 10 seconds | Frequency of health checks |
| `HeartbeatTimeout` | 10 seconds | Timeout for health checks |
| `RetryWrites` | true | Enable automatic write retries |

## Monitoring and Troubleshooting

### Logging

The fix includes comprehensive logging:

- MongoDB client initialization
- Connection pool configuration
- Disposal events
- Configuration loading

### Key Log Messages

```
MongoDB client initialized with connection to {DatabaseName}
Creating MongoDB client with connection pool: Max={MaxPool}, Min={MinPool}, IdleTimeout={IdleTimeout}
MongoDB configuration loaded: MaxPool={MaxPool}, MinPool={MinPool}, ConnectTimeout={ConnectTimeout}
Disposing MongoDB client
```

### Health Checks

The heartbeat configuration provides automatic health monitoring:
- Detects connection issues early
- Triggers reconnection when needed
- Prevents accumulation of dead connections

## Best Practices

1. **Monitor Connection Pool Usage**: Watch for pool exhaustion warnings
2. **Tune Pool Size**: Adjust based on your application's concurrency needs
3. **Configure Appropriate Timeouts**: Balance responsiveness vs. reliability
4. **Use Retry Logic**: Leverage the existing `MongoRetryHelper` for operations
5. **Monitor Heartbeat Logs**: Watch for connection health issues

## Files Modified

- `XiansAi.Server.Src/Shared/Data/MongoDbClientService.cs`
- `XiansAi.Server.Src/Shared/Data/MongoDBConfig.cs`
- `XiansAi.Server.Src/Shared/Data/MongoDbContext.cs`
- `XiansAi.Server.Src/Shared/Configuration/SharedServices.cs`

## Verification

After deployment:

1. Monitor connection pool metrics
2. Check for MongoDB client disposal logs on shutdown
3. Verify no connection exhaustion errors
4. Confirm stable database performance under load

This fix should resolve the Cosmos DB connection issues and eliminate the need for application restarts due to database connectivity problems. 