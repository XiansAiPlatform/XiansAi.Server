# Swagger/OpenAPI Documentation

## Overview

This project uses Swashbuckle.AspNetCore to provide OpenAPI documentation for the APIs. The documentation is accessible at `/api-docs` when the application is running.

## Configuration

The OpenAPI documentation is configured in the following files:

- `Shared/Configuration/OpenApiExtensions.cs`: Contains the main OpenAPI configuration
- `Features/WebApi/Endpoints`: Contains WebAPI endpoint definitions
- `Features/AgentApi/Endpoints`: Contains AgentAPI endpoint definitions

## Accessing the Documentation

The API documentation is available at:

```url
https://api.xians.ai/api-docs
http://localhost:5000/api-docs
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
- XML documentation integration
- Server URL detection based on the current request
- Request duration display
- Deep linking for operations and tags
- Filtering by tag
- TryItOut enabled by default
- Models section hidden by default

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

### Example from WebAPI Endpoints

```csharp
workflowsGroup.MapGet("/", async (
    [FromQuery] DateTime? startTime,
    [FromQuery] DateTime? endTime,
    [FromQuery] string? owner,
    [FromQuery] string? status,
    [FromServices] IWorkflowFinderService endpoint) =>
{
    return await endpoint.GetWorkflows(startTime, endTime, owner, status);
})
.WithName("Get Workflows")
.WithOpenApi(operation => {
    operation.Summary = "Get workflows with optional filters";
    operation.Description = "Retrieves workflows with optional filtering by date range and owner";
    return operation;
});
```

### Example from AgentAPI Endpoints

```csharp
signalGroup.MapPost("", async (
    [FromBody] WorkflowSignalRequest request,
    [FromServices] IWorkflowSignalService endpoint) =>
{
    return await endpoint.HandleSignalWorkflow(request);
})
.WithOpenApi(operation => {
    operation.Summary = "Signal workflow";
    operation.Description = "Sends a signal to a running workflow instance";
    return operation;
});
```

## Endpoint Groups and Authorization

Endpoints are organized into groups with common attributes:

### WebAPI Endpoints

```csharp
var workflowsGroup = app.MapGroup("/api/client/workflows")
    .WithTags("WebAPI - Workflows")
    .RequiresValidTenant();
```

### AgentAPI Endpoints

```csharp
var signalGroup = app.MapGroup("/api/agent/signal")
    .WithTags("AgentAPI - Signal")
    .RequiresCertificate();
```

## Authentication Methods

The API supports two authentication methods:

1. JWT Bearer Authentication (for WebAPI clients)
2. Certificate Authentication (for AgentAPI clients)

The Swagger UI includes instructions for using JWT authentication:

```csharp
options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
{
    Description = @"Authorization header using the Bearer scheme. Example: ""Authorization: Bearer {token}. ""
        If you are calling AgentAPI functions, use the App Server Token downloaded from the portal settings. 
        If you are calling WebAPI functions, use the JWT token obtained from the user login in browser.",
    Name = "Authorization",
    In = ParameterLocation.Header,
    Type = SecuritySchemeType.ApiKey,
    Scheme = "Bearer"
});
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

## Best Practices

1. Always include operation summaries and descriptions
2. Document all possible response codes
3. Provide examples for complex request/response models
4. Use XML comments for detailed documentation
5. Group related endpoints using tags
6. Follow naming conventions for endpoints with `WithName()`
7. Use consistent tag naming for logical grouping in the documentation 