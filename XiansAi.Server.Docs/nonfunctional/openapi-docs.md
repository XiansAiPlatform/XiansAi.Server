# Swagger/OpenAPI Documentation

## Overview

This project uses Swashbuckle.AspNetCore to provide OpenAPI documentation for the APIs. The documentation is accessible at `/api-docs` when the application is running.

## Configuration

The OpenAPI documentation is configured in the following files:

- `Shared/Configuration/OpenApi/OpenApiExtensions.cs`: Contains the OpenAPI Configuration
- `Shared/Configuration/SharedConfiguration.cs`: Configures Swagger middleware in the HTTP pipeline
- `Shared/Configuration/OpenApi/SwaggerConfiguration.cs`: Contains custom filters for enhancing the documentation

## Accessing the Documentation

The API documentation is available at:

```url
https://your-server/api-docs
```

## Custom Paths

We've customized the Swagger JSON endpoint paths to:

```url
/api-docs/{documentName}/swagger.json
```

## Features

Our Swagger configuration includes:

- JWT Authentication support
- Certificate Authentication support
- Auto-generated operation IDs based on method names
- XML documentation integration
- Request examples for common types
- Server URL detection based on the current request
- Custom filters for authorization and schema enhancement
- Support for inheritance using `allOf`

## Documenting Endpoints

### Minimal API Endpoints

For minimal API endpoints, use the `WithOpenApi` extension method:

```csharp
app.MapGet("/api/resource/{id}", GetResource)
    .WithOpenApi(operation => {
        operation.Summary = "Get resource by ID";
        operation.Description = "Retrieves a specific resource by its unique identifier";
        return operation;
    });
```

### Controllers

For controller actions, use XML documentation:

```csharp
/// <summary>
/// Retrieves a specific resource
/// </summary>
/// <param name="id">The resource ID</param>
/// <returns>The requested resource</returns>
/// <response code="200">Resource found and returned</response>
/// <response code="404">Resource not found</response>
[HttpGet("{id}")]
[ProducesResponseType(typeof(Resource), 200)]
[ProducesResponseType(404)]
public IActionResult GetResource(string id)
{
    // Implementation
}
```

## Custom Schema Documentation

To provide examples and additional documentation for DTOs:

```csharp
/// <summary>
/// Represents a workflow request
/// </summary>
public class WorkflowRequest
{
    /// <summary>
    /// The name of the workflow
    /// </summary>
    /// <example>ProcessOrder</example>
    public string Name { get; set; }
    
    /// <summary>
    /// The input data for the workflow
    /// </summary>
    /// <example>{"orderId": "12345"}</example>
    public object Input { get; set; }
}
```

## Adding Authorization Requirements

The `AuthorizationOperationFilter` automatically adds 401/403 responses to operations that require authorization.

## Custom Filters

We've implemented the following custom filters:

- `ExampleSchemaFilter`: Adds examples for common types like DateTime and Guid
- `AuthorizationOperationFilter`: Adds authorization-related responses to secured endpoints

## Best Practices

1. Always include operation summaries and descriptions
2. Document all possible response codes
3. Provide examples for complex request/response models
4. Use XML comments for detailed documentation
5. Group related endpoints using tags 