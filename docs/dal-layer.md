# Data Access Layer (DAL) Implementation Guide

This guide outlines the standard patterns for implementing a Data Access Layer in our application, using MongoDB as the database.

## Architecture Overview

The DAL consists of four main components:

1. Model Classes
2. Repository Classes
3. Database Service
4. Endpoint Controllers

### 1. Model Classes

Models represent the database entities and should follow these patterns:

```csharp
public class EntityName
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public required string Id { get; set; }

    [BsonElement("field_name")]
    public required Type FieldName { get; set; }

    // Always include creation timestamp
    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; set; }
}
```

Key points:

- Use `required` for non-nullable properties
- Use snake_case for MongoDB field names in `BsonElement` attributes
- Include `Id` and `CreatedAt` fields in all entities
- Use appropriate BsonRepresentation for ID fields

### 2. Repository Classes

Repositories handle database operations for specific entities:

```csharp
public class EntityRepository
{
    private readonly IMongoCollection<Entity> _collection;

    public EntityRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<Entity>("collection_name");
    }

    // Standard CRUD Operations
    public async Task<Entity> GetByIdAsync(string id)
    public async Task<List<Entity>> GetAllAsync()
    public async Task CreateAsync(Entity entity)
    public async Task<bool> UpdateAsync(string id, Entity entity)
    public async Task<bool> DeleteAsync(string id)

    // Common Query Patterns
    public async Task<Entity> GetLatestByFieldAsync(string fieldValue)
    public async Task<List<Entity>> GetByFieldAsync(string fieldValue)
}
```

Key points:

- Use async/await for all database operations
- Include standard CRUD operations
- Add specific query methods as needed
- Return `Task<bool>` for operations that need success confirmation
- Implement sorting and filtering methods when required

### 3. Database Service

The database service provides database connection management:

```csharp
public interface IDatabaseService
{
    Task<IMongoDatabase> GetDatabase();
}

public class DatabaseService : IDatabaseService
{
    private readonly IMongoDbClientService _mongoDbClientService;

    public DatabaseService(IMongoDbClientService mongoDbClientService)
    {
        _mongoDbClientService = mongoDbClientService;
    }

    public async Task<IMongoDatabase> GetDatabase()
    {
        return await Task.FromResult(_mongoDbClientService.GetDatabase());
    }
}
```

Key points:

- Use dependency injection
- Always define an interface
- Keep the service lightweight and focused
- Maintain single responsibility principle

### 4. Endpoint Implementation

Endpoints should follow this pattern:

```csharp
public class EntityEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<EntityEndpoint> _logger;

    public EntityEndpoint(IDatabaseService databaseService, ILogger<EntityEndpoint> logger)
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<IResult> OperationName(Parameters parameters)
    {
        var repository = new EntityRepository(await _databaseService.GetDatabase());
        var result = await repository.OperationAsync(parameters);
        return Results.Ok(result);
    }
}
```

Key points:

- Use dependency injection for database service and logger
- Create repository instances per operation
- Return `IResult` with appropriate HTTP results
- Include logging for important operations

## Best Practices

### 1. Error Handling

- Use try-catch blocks for database operations
- Return appropriate HTTP status codes
- Log errors with sufficient context
- Implement custom exception types when needed
- Handle MongoDB-specific exceptions appropriately

### 2. Validation

- Validate input before database operations
- Use request models for input validation
- Include required fields and data types
- Implement business rule validation
- Use data annotations for basic validation

### 3. Performance

- Use indexes for frequently queried fields
- Implement pagination for large result sets
- Use projection to limit returned fields when appropriate
- Implement caching where beneficial
- Use bulk operations for multiple document updates

### 4. Versioning

- Include version fields when entity versioning is needed
- Implement methods to retrieve latest versions
- Maintain creation timestamps for all records
- Consider soft deletes for historical tracking
- Implement version control strategies

### 5. Security

- Implement proper access control
- Sanitize input data
- Use parameterized queries
- Avoid exposing internal database details
- Follow principle of least privilege

## Advanced Patterns

### 1. Aggregation Queries

```csharp
public async Task<List<Entity>> GetAggregatedData()
{
    return await _collection.Aggregate()
        .Match(filter)
        .Group(key => key.Field, group => new { Count = group.Count() })
        .ToListAsync();
}
```

### 2. Bulk Operations

```csharp
public async Task BulkWriteAsync(List<Entity> entities)
{
    var writes = entities.Select(entity =>
        new InsertOneModel<Entity>(entity));
    await _collection.BulkWriteAsync(writes);
}
```

### 3. Search Implementation

```csharp
public async Task<List<Entity>> SearchAsync(string searchTerm)
{
    var filter = Builders<Entity>.Filter.Or(
        Builders<Entity>.Filter.Regex(x => x.Field1, new BsonRegularExpression(searchTerm, "i")),
        Builders<Entity>.Filter.Regex(x => x.Field2, new BsonRegularExpression(searchTerm, "i"))
    );
    return await _collection.Find(filter).ToListAsync();
}
```

## Testing

### 1. Repository Testing

- Use in-memory MongoDB for unit tests
- Test all CRUD operations
- Verify edge cases and error conditions
- Test complex queries and aggregations
- Implement integration tests

### 2. Endpoint Testing

- Test HTTP responses
- Verify error handling
- Test input validation
- Implement end-to-end tests
- Test authentication and authorization

## Example Implementation

See the following files for reference:

- `MongoDB/Models/Instruction.cs` for model implementation
- `MongoDB/Repositories/InstructionRepository.cs` for repository pattern
- `MongoDB/DatabaseService.cs` for database service
- `EndpointExt/WebClient/InstructionsEndpoint.cs` for endpoint implementation

## Creating New DAL Components

### 1. Create Model

1. Define the model class with required properties
2. Add appropriate BsonElement attributes
3. Include standard fields (Id, CreatedAt)

### 2. Create Repository

1. Implement standard CRUD operations
2. Add specific query methods
3. Include appropriate indexes
4. Implement error handling

### 3. Create Endpoint

1. Define request/response models
2. Implement endpoint methods
3. Add proper validation
4. Include logging

### 4. Register Services

1. Add to dependency injection container
2. Configure database connection
3. Set up appropriate middleware

## Maintenance and Updates

### 1. Schema Updates

- Plan for backward compatibility
- Implement migration strategies
- Version database schemas
- Document changes

### 2. Performance Monitoring

- Monitor query performance
- Review and update indexes
- Implement performance logging
- Regular maintenance checks

### 3. Security Updates

- Regular security audits
- Update access controls
- Monitor for vulnerabilities
- Keep dependencies updated
