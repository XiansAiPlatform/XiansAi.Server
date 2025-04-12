# Designing API Endpoints

This guide explains how to implement new API endpoints in the XiansAi.Server project using Minimal APIs with static extension methods.

## Core Pattern: Static Extension Methods

Endpoints are defined within `static` classes as extension methods for `IEndpointRouteBuilder`. This approach promotes organization and leverages Minimal API features.

### File Locations and Naming

-   **Agent API Endpoints:** Place files in `XiansAi.Server.Src/Features/AgentApi/Endpoints/`. Name files like `*Endpoints.cs` (e.g., `CacheEndpoints.cs`).
-   **Web API (Client) Endpoints:** Place files in `XiansAi.Server.Src/Features/WebApi/Endpoints/`. Name files like `*EndpointExtensions.cs` (e.g., `WorkflowEndpointExtensions.cs`).

### Basic Structure

Each file defines a static class containing one or more `Map*` extension methods.

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc; // For [FromServices], [FromBody] etc.
using Microsoft.AspNetCore.Routing; 
// Add other necessary using statements (MediatR, Services, Models, etc.)

namespace XiansAi.Server.Features.WebApi.Endpoints; // Or AgentApi

public static class MyEntityEndpointExtensions // Or MyEntityEndpoints
{
    public static IEndpointRouteBuilder MapMyEntityEndpoints(this IEndpointRouteBuilder builder)
    {
        // Group endpoints logically for OpenAPI documentation
        var group = builder.MapGroup("/api/client/myentities") // Base path for the group
                           .WithTags("MyEntities"); // Tag for Swagger UI grouping

        // Define individual endpoints within the group
        group.MapGet("/{id}", GetEntityByIdAsync)
            .WithName("Get MyEntity By Id")
            .RequireAuthorization("RequireTenantAuth") // Apply authorization policy
            .WithOpenApi(); 
            
        group.MapPost("/", CreateEntityAsync)
            .WithName("Create MyEntity")
            .RequireAuthorization("RequireTenantAuth")
            .WithOpenApi();
            
        // Add other endpoint mappings (PUT, DELETE, other GETs)

        return builder; // Return builder for chaining
    }

    // Endpoint handler method (can be static local function or separate static method)
    private static async Task<IResult> GetEntityByIdAsync(
        string id,
        [FromServices] IMediator mediator, // Example: Injecting MediatR
        [FromServices] ILogger<MyEntityEndpointExtensions> logger) 
    {
        // Example using MediatR (common in WebApi)
        // var query = new GetMyEntityQuery(id);
        // var result = await mediator.Send(query);
        // return Results.Ok(result);
        
        // Or implement logic directly / call another service (common in AgentApi)
        logger.LogInformation("Fetching entity with ID: {EntityId}", id);
        // ... implementation ...
        await Task.CompletedTask; // Placeholder
        var entity = new { Id = id, Name = "Sample Entity" }; // Placeholder
        if (entity == null) {
            return Results.NotFound();
        }
        return Results.Ok(entity); 
    }

    private static async Task<IResult> CreateEntityAsync(
        [FromBody] MyEntityRequest request, // Define MyEntityRequest DTO elsewhere
        [FromServices] IMediator mediator,
        [FromServices] ILogger<MyEntityEndpointExtensions> logger)
    {
        // Validate request
        if (string.IsNullOrEmpty(request.Name))
        {
            return Results.BadRequest("Name is required");
        }
        
        logger.LogInformation("Creating entity with Name: {EntityName}", request.Name);
        // Example using MediatR
        // var command = new CreateMyEntityCommand(request);
        // var result = await mediator.Send(command);
        // return Results.Created($"/api/client/myentities/{result.Id}", result); 

        await Task.CompletedTask; // Placeholder
        var createdEntity = new { Id = Guid.NewGuid().ToString(), request.Name }; // Placeholder
        return Results.Created($"/api/client/myentities/{createdEntity.Id}", createdEntity);
    }
    
    // Define request/response DTOs if necessary
    public class MyEntityRequest 
    {
        public required string Name { get; set; }
        // Add other properties
    }
}
```
*Refer to existing files in `Features/WebApi/Endpoints` and `Features/AgentApi/Endpoints` for concrete examples.*

## Mapping Endpoints

-   Use `builder.MapGet()`, `MapPost()`, `MapPut()`, `MapDelete()` to define endpoints.
-   Pass handler logic directly as a lambda or reference a static local function/method.
-   Inject dependencies directly into the handler method parameters using `[FromServices]`, `[FromBody]`, `[FromRoute]`, `[FromQuery]`.
-   For `WebApi`, prefer using MediatR (`IMediator`) to dispatch requests to dedicated handlers, keeping the endpoint definition clean. See `WorkflowEndpointExtensions.cs` for examples.
-   For `AgentApi`, logic might be simpler and handled directly or by calling specific services. See `CacheEndpoints.cs` for examples.

## API Metadata and Grouping

-   **`.MapGroup(prefix)`:** Groups related endpoints under a common route prefix.
-   **`.WithTags(tagName)`:** Organizes endpoints in the Swagger UI. Use PascalCase for tag names (e.g., "Workflows", "CacheManagement").
-   **`.WithName(operationId)`:** Provides a unique ID for the operation, crucial for client generation and linking. Use PascalCase (e.g., "GetWorkflowById").
-   **`.WithOpenApi()`:** Enables and configures OpenAPI documentation for the endpoint. Can be customized further (e.g., adding summaries, descriptions).
-   **`.WithGroupName(groupName)`:** (Optional) Can be used for broader API categorization if needed.

## Authorization

-   Apply authorization policies using **`.RequireAuthorization(policyName)`**.
-   Common policies include:
    -   `"RequireTenantAuth"`: For standard authenticated client access (WebApi).
    -   Others may be defined for specific AgentApi or internal requirements.

## Request/Response Handling

-   Define Data Transfer Objects (DTOs) for request bodies (`[FromBody]`) and complex responses. Place DTOs in relevant `Contracts` or `Models` folders within the feature area.
-   Use standard `Microsoft.AspNetCore.Http.Results` static methods (e.g., `Results.Ok()`, `Results.NotFound()`, `Results.BadRequest()`, `Results.Created()`, `Results.NoContent()`) to return appropriate HTTP responses.
-   Implement input validation within the handler method or, preferably, within the MediatR handler/service layer.

## Registration

The `Map*Endpoints` or `Map*EndpointExtensions` methods need to be called during application startup to register the routes with the application pipeline. This is typically done in `Program.cs` or via aggregator extension methods called from `Program.cs`.

Example (Conceptual - check `Program.cs` for actual implementation):

```csharp
// In Program.cs or a setup extension method
var app = builder.Build();

// ... other middleware ...

app.UseRouting();
app.UseAuthentication(); // Must be before Authorization
app.UseAuthorization();

// Map endpoints
app.MapMyEntityEndpoints(); 
app.MapWorkflowEndpoints(); 
app.MapCacheEndpoints();
// ... map other endpoints ...

app.Run();
```

## Common Issues and Solutions

1.  **Authorization Issues:**
    -   Verify the correct `[Authorize]` attribute or `.RequireAuthorization(policyName)` is applied.
    -   Ensure Authentication middleware runs *before* Authorization middleware in `Program.cs`.
    -   Check JWT token validity and required claims for the policy.
2.  **Dependency Injection:**
    -   Ensure required services (like `IMediator`, Repositories, Loggers) are registered in `Program.cs` or `ServiceCollectionExtensions`.
    -   Use `[FromServices]` correctly in handler parameters.
3.  **Routing Conflicts:**
    -   Ensure route templates are unique. Use `WithName` to help diagnose issues.
    -   Check `.MapGroup()` prefixes.
4.  **Error Handling:**
    -   Use try-catch blocks judiciously, especially around external calls (database, other APIs).
    -   Log exceptions using injected `ILogger`.
    -   Return appropriate `Results` types for errors (e.g., `Results.BadRequest`, `Results.NotFound`, `Results.Problem` for 500 errors). Consider implementing global exception handling middleware.
