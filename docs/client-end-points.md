# Creating New Client Endpoints

This guide explains how to implement new client endpoints in the XiansAi.Server project following the established patterns.

## Implementation Steps

### 1. Create the Endpoint Class

Create a new endpoint class in `EndpointExt/WebClient/` directory:

```csharp
namespace XiansAi.Server.EndpointExt.WebClient;

// Define request DTOs if needed
public class MyEntityRequest 
{
    public required string Name { get; set; }
    // Add other properties
}

public class MyEntityEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<MyEntityEndpoint> _logger;

    public MyEntityEndpoint(
        IDatabaseService databaseService,
        ILogger<MyEntityEndpoint> logger
    )
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    // Implement endpoint methods
    public async Task<IResult> GetEntity(string id)
    {
        var repository = new MyEntityRepository(await _databaseService.GetDatabase());
        var entity = await repository.GetByIdAsync(id);
        return Results.Ok(entity);
    }
}
```

### 2. Register the Endpoint Service

Add the endpoint registration in `AppExt/ServiceCollectionExtensions.cs` under the `AddEndpoints()` method:

```csharp
private static WebApplicationBuilder AddEndpoints(this WebApplicationBuilder builder)
{
    // ... existing registrations ...
    builder.Services.AddScoped<MyEntityEndpoint>();
    return builder;
}
```

### 3. Map the Endpoints

Create a new mapping method in `EndpointExt/WebClientEndpointExtensions.cs`:

```csharp
public static class WebClientEndpointExtensions
{
    public static void MapClientEndpoints(this WebApplication app)
    {
        // ... existing mappings ...
        MapMyEntityEndpoints(app);
    }

    private static void MapMyEntityEndpoints(this WebApplication app)
    {
        app.MapGet("/api/client/myentities/{id}", async (
            string id,
            [FromServices] MyEntityEndpoint endpoint) =>
        {
            return await endpoint.GetEntity(id);
        })
        .WithName("Get MyEntity")
        .RequireAuthorization("RequireTenantAuth")
        .WithOpenApi();

        // Add other endpoint mappings
    }
}
```

## Best Practices

### 1. Naming Conventions
- Endpoint classes should end with `Endpoint` suffix (e.g., `MyEntityEndpoint`)
- Place endpoint classes in `EndpointExt/WebClient/` directory
- Use consistent route naming: `/api/client/{resource}/{action}`
- Request/response DTOs should be descriptive (e.g., `CreateMyEntityRequest`)

### 2. Security
- Always add `.RequireAuthorization("RequireTenantAuth")` to protect endpoints
- Use appropriate HTTP methods:
  - GET for retrieving data
  - POST for creating new resources
  - PUT for updating existing resources
  - DELETE for removing resources

### 3. Documentation
- Add `.WithOpenApi()` to enable Swagger documentation
- Use `.WithName()` to set operation IDs
- Add operation descriptions using `.WithOpenApi(operation => {...})`
- Document expected responses and error cases

### 4. Error Handling
- Return appropriate HTTP status codes:
  - 200 OK for successful operations
  - 201 Created for successful resource creation
  - 400 Bad Request for invalid input
  - 404 Not Found for missing resources
  - 500 Internal Server Error for unexpected errors
- Use consistent error response format

## Common Implementation Patterns

### 1. Database Operations
```csharp
// Repository initialization
var repository = new MyEntityRepository(await _databaseService.GetDatabase());

// Common operations
public async Task<IResult> GetEntities()
{
    var entities = await repository.GetAllAsync();
    _logger.LogInformation("Retrieved {Count} entities", entities.Count);
    return Results.Ok(entities);
}

public async Task<IResult> GetEntity(string id)
{
    var entity = await repository.GetByIdAsync(id);
    if (entity == null)
        return Results.NotFound();
    return Results.Ok(entity);
}
```

### 2. Request Validation
```csharp
public async Task<IResult> CreateEntity(MyEntityRequest request)
{
    // Validate request
    if (string.IsNullOrEmpty(request.Name))
    {
        return Results.BadRequest("Name is required");
    }

    // Create entity
    var entity = new MyEntity
    {
        Id = ObjectId.GenerateNewId().ToString(),
        Name = request.Name,
        CreatedAt = DateTime.UtcNow
    };

    await repository.CreateAsync(entity);
    return Results.Ok(entity);
}
```

### 3. Logging
```csharp
public async Task<IResult> UpdateEntity(string id, MyEntityRequest request)
{
    _logger.LogInformation("Updating entity {Id}", id);
    try
    {
        // Update logic
        return Results.Ok(updatedEntity);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error updating entity {Id}", id);
        return Results.StatusCode(500);
    }
}
```

## Complete Endpoint Example

Here's a complete example showing common endpoint patterns:

```csharp
public class MyEntityEndpoint
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<MyEntityEndpoint> _logger;

    public MyEntityEndpoint(
        IDatabaseService databaseService,
        ILogger<MyEntityEndpoint> logger
    )
    {
        _databaseService = databaseService;
        _logger = logger;
    }

    public async Task<IResult> GetEntities()
    {
        var repository = new MyEntityRepository(await _databaseService.GetDatabase());
        var entities = await repository.GetAllAsync();
        _logger.LogInformation("Retrieved {Count} entities", entities.Count);
        return Results.Ok(entities);
    }

    public async Task<IResult> GetEntity(string id)
    {
        var repository = new MyEntityRepository(await _databaseService.GetDatabase());
        var entity = await repository.GetByIdAsync(id);
        if (entity == null)
            return Results.NotFound();
        return Results.Ok(entity);
    }

    public async Task<IResult> CreateEntity(MyEntityRequest request)
    {
        if (string.IsNullOrEmpty(request.Name))
            return Results.BadRequest("Name is required");

        var repository = new MyEntityRepository(await _databaseService.GetDatabase());
        var entity = new MyEntity
        {
            Id = ObjectId.GenerateNewId().ToString(),
            Name = request.Name,
            CreatedAt = DateTime.UtcNow
        };

        await repository.CreateAsync(entity);
        return Results.Ok(entity);
    }

    public async Task<IResult> DeleteEntity(string id)
    {
        var repository = new MyEntityRepository(await _databaseService.GetDatabase());
        await repository.DeleteAsync(id);
        return Results.Ok();
    }
}
```

## Endpoint Registration

Map all endpoints in `WebClientEndpointExtensions.cs`:

```csharp
private static void MapMyEntityEndpoints(this WebApplication app)
{
    app.MapGet("/api/client/myentities", async (
        [FromServices] MyEntityEndpoint endpoint) =>
    {
        return await endpoint.GetEntities();
    })
    .WithName("Get Entities")
    .RequireAuthorization("RequireTenantAuth")
    .WithOpenApi();

    app.MapGet("/api/client/myentities/{id}", async (
        string id,
        [FromServices] MyEntityEndpoint endpoint) =>
    {
        return await endpoint.GetEntity(id);
    })
    .WithName("Get Entity")
    .RequireAuthorization("RequireTenantAuth")
    .WithOpenApi();

    app.MapPost("/api/client/myentities", async (
        [FromBody] MyEntityRequest request,
        [FromServices] MyEntityEndpoint endpoint) =>
    {
        return await endpoint.CreateEntity(request);
    })
    .WithName("Create Entity")
    .RequireAuthorization("RequireTenantAuth")
    .WithOpenApi();

    app.MapDelete("/api/client/myentities/{id}", async (
        string id,
        [FromServices] MyEntityEndpoint endpoint) =>
    {
        return await endpoint.DeleteEntity(id);
    })
    .WithName("Delete Entity")
    .RequireAuthorization("RequireTenantAuth")
    .WithOpenApi();
}
```

## Testing

1. Use Swagger UI to test endpoints at `/swagger`
2. Ensure proper authorization headers are set
3. Test all success and error scenarios
4. Verify proper error handling and status codes
5. Check logging output for debugging information

## Common Issues and Solutions

1. **Authorization Issues**
   - Verify "RequireTenantAuth" policy is applied
   - Check JWT token is valid and contains required claims

2. **Database Connection**
   - Ensure proper injection of IDatabaseService
   - Verify MongoDB connection string in configuration

3. **Request Validation**
   - Implement comprehensive input validation
   - Return clear error messages for invalid requests

4. **Error Handling**
   - Use try-catch blocks for database operations
   - Log exceptions with appropriate detail level
   - Return user-friendly error messages