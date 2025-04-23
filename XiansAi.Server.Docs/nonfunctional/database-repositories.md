# Repository Pattern Implementation Guide

This guide outlines the standard patterns for implementing Repositories in our application, using MongoDB as the database.

## Architecture Overview

The Repository layer consists of three main components:

1. Model Classes
2. Repository Interfaces and Implementations
3. Shared Database Service

### 1. Model Classes

Models represent the database entities and should follow these patterns:

```csharp
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace XiansAi.Server.Shared.Data.Models;

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
    
    // Include tenant ID for multi-tenant data
    [BsonElement("tenant_id")]
    public string? TenantId { get; set; }
}
```

Key points:

- Use `required` for non-nullable properties
- Use snake_case for MongoDB field names in `BsonElement` attributes
- Include `Id` and `CreatedAt` fields in all entities
- Include `TenantId` for multi-tenant data
- Use appropriate BsonRepresentation for ID fields

### 2. Repository Classes

Repositories handle database operations for specific entities. They are organized in feature-specific directories:

- `XiansAi.Server.Src/Features/AgentApi/Repositories` for agent-related repositories
- `XiansAi.Server.Src/Features/WebApi/Repositories` for web API related repositories

Each repository follows this pattern:

```csharp
using MongoDB.Driver;
using XiansAi.Server.Shared.Data;
using XiansAi.Server.Shared.Data.Models;

namespace Features.FeatureName.Repositories;

public interface IEntityRepository
{
    Task<Entity> GetByIdAsync(string id);
    Task<List<Entity>> GetAllAsync();
    // Additional query methods...
}

public class EntityRepository : IEntityRepository
{
    private readonly IMongoCollection<Entity> _collection;

    public EntityRepository(IDatabaseService databaseService)
    {
        var database = databaseService.GetDatabase().GetAwaiter().GetResult();
        _collection = database.GetCollection<Entity>("collection_name");
        
        // Create indexes if needed
        var indexKeys = Builders<Entity>.IndexKeys.Ascending(x => x.SomeField);
        var indexOptions = new CreateIndexOptions { Background = true };
        var indexModel = new CreateIndexModel<Entity>(indexKeys, indexOptions);
        _collection.Indexes.CreateOne(indexModel);
    }

    // Standard CRUD Operations
    public async Task<Entity> GetByIdAsync(string id)
    {
        return await _collection.Find(x => x.Id == id).FirstOrDefaultAsync();
    }

    // Additional query implementations...
}
```

Key points:

- Always define an interface for each repository
- Inject the `IDatabaseService` in the constructor
- Create indexes in the constructor if needed
- Implement async methods for all database operations
- Include standard CRUD operations and custom queries as needed

## Dependency Registration

Repositories must be registered for dependency injection in the appropriate configuration class:

### For AgentApi features:

```csharp
// In Features/AgentApi/Configuration/AgentApiConfiguration.cs
public static WebApplicationBuilder AddAgentApiServices(this WebApplicationBuilder builder)
{
    // Register repositories
    builder.Services.AddScoped<IEntityRepository, EntityRepository>();
    
    // Other service registrations...
    return builder;
}
```

### For WebApi features:

```csharp
// In Features/WebApi/Configuration/WebApiConfiguration.cs
public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
{
    // Register repositories
    builder.Services.AddScoped<IEntityRepository, EntityRepository>();
    
    // Other service registrations...
    return builder;
}
```

## Best Practices

### 1. Error Handling

- Use try-catch blocks for database operations
- Log errors with sufficient context
- Return appropriate results to indicate success/failure
- Handle MongoDB-specific exceptions appropriately

### 2. Multi-tenant Data

- Always include tenant filtering in queries for multi-tenant data
- Add tenant ID indexes for performance
- Validate tenant access in service layer

### 3. Performance

- Create indexes for frequently queried fields
- Implement pagination for large result sets
- Use projection to limit returned fields when appropriate
- Consider using async enumeration for large datasets

### 4. Security

- Implement proper access control at the service layer
- Sanitize input data
- Avoid exposing internal database details
- Follow principle of least privilege

## Advanced Patterns

### 1. Complex Queries

```csharp
public async Task<List<EntityDto>> GetFilteredEntitiesAsync(string tenantId, string searchTerm)
{
    var builder = Builders<Entity>.Filter;
    var filter = builder.Eq(x => x.TenantId, tenantId);
    
    if (!string.IsNullOrEmpty(searchTerm))
    {
        var searchFilter = builder.Regex(x => x.Name, new MongoDB.Bson.BsonRegularExpression(searchTerm, "i"));
        filter = builder.And(filter, searchFilter);
    }
    
    return await _collection.Find(filter)
        .Sort(Builders<Entity>.Sort.Descending(x => x.CreatedAt))
        .Limit(100)
        .Project(x => new EntityDto
        {
            Id = x.Id,
            Name = x.Name,
            CreatedAt = x.CreatedAt
        })
        .ToListAsync();
}
```

### 2. Index Creation

Always create appropriate indexes in the repository constructor:

```csharp
// Single field index
var indexKeys = Builders<Entity>.IndexKeys.Ascending(x => x.Name);
var indexModel = new CreateIndexModel<Entity>(indexKeys, new CreateIndexOptions { Background = true });
_collection.Indexes.CreateOne(indexModel);

// Compound index
var compoundIndex = Builders<Entity>.IndexKeys
    .Ascending(x => x.TenantId)
    .Ascending(x => x.WorkflowId);
var compoundModel = new CreateIndexModel<Entity>(compoundIndex, new CreateIndexOptions { Background = true });
_collection.Indexes.CreateOne(compoundModel);
```

## Creating New Repository Components

### 1. Create Models

1. Define the model class in `Shared.Data.Models` namespace
2. Add appropriate BsonElement attributes
3. Include standard fields (Id, CreatedAt)
4. Add TenantId for multi-tenant data

### 2. Create Repository

1. Create a new file in the appropriate feature repository folder
2. Define an interface with required operations
3. Implement the repository class with the interface
4. Add constructor that accepts IDatabaseService
5. Set up collection and indexes

### 3. Register the Repository

1. Add registration to the appropriate configuration class
2. Use AddScoped for the lifetime scope

## Example Implementation

See the following files for reference:

- `Features/WebApi/Repositories/WebhookRepository.cs`
- `Features/AgentApi/Repositories/KnowledgeRepository.cs`
- `Features/WebApi/Configuration/WebApiConfiguration.cs`
- `Features/AgentApi/Configuration/AgentApiConfiguration.cs`
- `Shared/Data/DatabaseService.cs`

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
