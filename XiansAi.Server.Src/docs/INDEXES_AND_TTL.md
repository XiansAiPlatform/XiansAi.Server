# MongoDB Indexes and TTL Configuration

## Overview

This document explains MongoDB index management and Time-to-Live (TTL) functionality in the XiansAi Server system. Our system uses a centralized YAML-based approach for defining and synchronizing MongoDB indexes, including TTL indexes for automatic data expiration.

## Index Management System

### Configuration File

Indexes are defined in the centralized configuration file:
- **Location**: `XiansAi.Server.Src/mongodb-indexes.yaml`
- **Schema**: Validated against `XiansAi.Server.Src/mongodb-indexes.schema.json`
- **Synchronization**: Automatically applied during application startup

### Index Synchronizer

The `MongoIndexSynchronizer` service handles:
- Reading index definitions from YAML
- Creating missing indexes
- Dropping unused indexes (MongoDB only, skipped for Cosmos DB)
- Handling TTL index creation
- Error handling for Cosmos DB compatibility

**Service Registration**: `SharedServices.AddInfrastructureServices()` → `IMongoIndexSynchronizer`

**Execution**: Called during application startup in `Program.cs`

## Time-to-Live (TTL) Indexes

### What is TTL?

TTL (Time-to-Live) indexes automatically delete documents from a collection after a specified time period. MongoDB runs a background task approximately every 60 seconds to remove expired documents.

### How TTL Works

1. **Index Creation**: A TTL index is created on a date field
2. **Document Expiration**: Documents are deleted when `document[date_field] + TTL_duration < current_time`
3. **Background Process**: MongoDB handles deletion automatically, no application code required
4. **Performance**: Efficient deletion without impacting application performance

### TTL Requirements

- **Date Field**: The indexed field must contain a valid `Date` type
- **Single Field**: TTL indexes can only be on a single field
- **Background**: TTL deletion happens in MongoDB's background process

## Current TTL Settings

### Logs Collection (30 days)

```yaml
logs:
  - name: logs_ttl_created_at
    keys:
      created_at: asc
    expire_after: 30d
    background: true
```

**Purpose**: Automatic cleanup of system logs to prevent unbounded growth
**Field**: `created_at` - when the log entry was created
**Retention**: 30 days

### Activity History Collection (90 days)

```yaml
activity_history:
  - name: activity_history_ttl
    keys:
      started_time: asc
    expire_after: 90d
    background: true
```

**Purpose**: Cleanup of workflow activity history for storage management
**Field**: `started_time` - when the activity started
**Retention**: 90 days

### Conversation Messages Collection (180 days)

```yaml
conversation_message:
  - name: conversation_message_ttl
    keys:
      created_at: asc
    expire_after: 180d
    background: true
```

**Purpose**: Cleanup of old conversation messages while retaining recent chat history
**Field**: `created_at` - when the message was created
**Retention**: 180 days (6 months)

## TTL Time Format

### Supported Units

The system supports flexible time formats via `TimeSpanTypeConverter`:

- `s` - seconds: `3600s`
- `m` - minutes: `60m`
- `h` - hours: `24h`
- `d` - days: `30d`
- `w` - weeks: `2w`

### Combined Formats

You can combine multiple units:

```yaml
expire_after: 5d 6h        # 5 days and 6 hours
expire_after: 1w 3d 12h    # 1 week, 3 days, and 12 hours
expire_after: 2w 1d        # 2 weeks and 1 day
```

### Format Validation

The format is validated by regex: `^((\\d+)\\s*([smhdw])\\s*)+$`

**Valid Examples**:
- `30d`
- `12h`
- `5d 6h`
- `1w 2d 12h 30m`

**Invalid Examples**:
- `30 days` (use `30d`)
- `12hours` (use `12h`)
- `5d6h` (need space: `5d 6h`)

## Adding New TTL Indexes

### Step 1: Update YAML Configuration

Add a TTL index to the appropriate collection in `mongodb-indexes.yaml`:

```yaml
your_collection:
  # ... existing indexes ...
  - name: your_collection_ttl
    keys:
      created_at: asc          # or appropriate date field
    expire_after: 30d          # desired retention period
    background: true           # recommended for production
```

### Step 2: Ensure Date Field Exists

Verify your document model has the appropriate date field:

```csharp
public class YourDocument
{
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    // OR
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddDays(30);
}
```

### Step 3: Deploy and Verify

1. **Deploy**: The index will be created automatically on application startup
2. **Verify**: Check application logs for successful index creation
3. **Monitor**: Observe document counts to ensure TTL is working

## Best Practices

### TTL Configuration

1. **Background Creation**: Always use `background: true` for production environments
2. **Appropriate Retention**: Balance storage costs with data retention requirements
3. **Date Field Selection**: Use creation timestamps rather than update timestamps
4. **Testing**: Test TTL behavior in development environment first

### Collection Considerations

**Good Candidates for TTL**:
- Log entries and audit trails
- Temporary session data
- Cache entries
- Expired tokens or invitations
- Old conversation messages
- Activity history and metrics

**Avoid TTL for**:
- Critical business data
- Financial transactions
- User account information
- Configuration data

### Performance Considerations

1. **Index Performance**: TTL indexes also serve as regular indexes for queries
2. **Deletion Impact**: MongoDB's background deletion is efficient but can impact performance during heavy deletion periods
3. **Storage Planning**: Factor TTL into storage capacity planning

## Troubleshooting

### Common Issues

1. **TTL Not Working**
   - Verify date field contains valid `Date` objects, not strings
   - Check index was created successfully in logs
   - Wait for MongoDB's background task (runs every ~60 seconds)

2. **Index Creation Failed**
   - Check application logs for specific error messages
   - Verify YAML syntax and format validation
   - For Cosmos DB, check for unique index limitations

3. **Cosmos DB Compatibility**
   - System automatically detects Cosmos DB
   - Unused index dropping is skipped for Cosmos DB
   - Unique index errors are handled gracefully

### Monitoring TTL

```csharp
// Example: Check TTL index status
db.collection.getIndexes().filter(idx => idx.expireAfterSeconds)

// Example: Monitor document expiration
db.collection.find({
  created_at: { $lt: new Date(Date.now() - 30*24*60*60*1000) }
}).count()
```

### Log Messages

**Successful Index Creation**:
```
[INFO] Creating 1 indexes for collection activity_history
[INFO] Index synchronization completed
```

**Cosmos DB Handling**:
```
[WARN] Detected Cosmos DB - skipping index drops for collection logs
[WARN] Cosmos DB unique index restriction encountered...
```

## Security Considerations

1. **Data Retention Compliance**: Ensure TTL settings comply with data retention policies
2. **Audit Trail**: Consider longer retention for audit-related collections
3. **Backup Strategy**: Factor TTL into backup and recovery planning
4. **Legal Requirements**: Some data may have legal retention requirements

## Configuration Examples

### Short-term Cache (1 hour)

```yaml
cache_entries:
  - name: cache_ttl
    keys:
      expires_at: asc
    expire_after: 1h
    background: true
```

### Session Data (24 hours)

```yaml
user_sessions:
  - name: session_ttl
    keys:
      created_at: asc
    expire_after: 24h
    background: true
```

### Audit Logs (1 year)

```yaml
audit_logs:
  - name: audit_ttl
    keys:
      timestamp: asc
    expire_after: 52w
    background: true
```

### Temporary Tokens (30 minutes)

```yaml
temp_tokens:
  - name: token_ttl
    keys:
      issued_at: asc
    expire_after: 30m
    background: true
```

## Related Documentation

- [DB Schemas](./DB_SCHEMAS.md) - Database schema design guidelines
- [MongoDB Connection Fix](./MONGODB_CONNECTION_FIX.md) - Connection troubleshooting
- [Data Seeding](./DATA_SEEDING.md) - Initial data setup

## Implementation Files

- **Configuration**: `XiansAi.Server.Src/mongodb-indexes.yaml`
- **Schema Validation**: `XiansAi.Server.Src/mongodb-indexes.schema.json`
- **Index Synchronizer**: `XiansAi.Server.Src/Shared/Data/MongoIndexSynchronizer.cs`
- **Time Converter**: `XiansAi.Server.Src/Shared/Utils/Serialization/TimeSpanTypeConverter.cs`
- **Service Registration**: `XiansAi.Server.Src/Shared/Configuration/SharedServices.cs`
- **Startup Integration**: `XiansAi.Server.Src/Program.cs` 