# API Documentation

XiansAI Server provides comprehensive API documentation using OpenAPI (Swagger) specification.

## Accessing the Documentation

The API documentation is accessible via a web interface at:

```
https://<your-server-url>/api-docs
```

Or in local development:

```
http://localhost:<port>/api-docs
```

## Features of the API Documentation

- **Interactive Documentation**: Test API endpoints directly from the browser
- **Authentication Support**: UI includes support for JWT authentication
- **Request/Response Examples**: Provides example request and response payloads
- **Clear Organization**: Endpoints are organized by controller/functionality
- **Health Check**: A simple health check endpoint is available at `/api/health`

## How to Use

1. Navigate to the API documentation URL
2. Explore the available endpoints
3. Click on any endpoint to expand its details
4. For authenticated endpoints:
   - Click the "Authorize" button at the top
   - Enter your JWT token in the format: `Bearer <your-token>`
   - Click "Authorize" and close the dialog
5. Use the "Try it out" button to test endpoints
6. Fill in required parameters and click "Execute"
7. View the response

## Documentation for Developers

### Adding XML Documentation

Detailed API documentation is generated from XML comments in code. To enhance the documentation, add XML comments to controllers, actions, and models:

```csharp
/// <summary>
/// Gets a specific resource by ID
/// </summary>
/// <param name="id">The unique identifier for the resource</param>
/// <returns>The requested resource</returns>
/// <response code="200">Returns the requested resource</response>
/// <response code="404">If the resource was not found</response>
public async Task<IResult> GetResource(string id)
{
    // Implementation...
}
```

### Adding OpenAPI Annotations

For minimal routes, use the `WithOpenApi` extension method to add documentation:

```csharp
app.MapGet("/api/resources/{id}", GetResource)
   .WithName("Get Resource")
   .RequireAuthorization("RequireTenantAuth")
   .WithOpenApi(operation => {
       operation.Summary = "Get a specific resource";
       operation.Description = "Retrieves a resource by its unique ID";
       return operation;
   });
```

## Security

The API documentation is available in all environments and does not expose sensitive information. Authentication is still required to test protected endpoints.

For additional security in production environments, consider:

1. Setting up a reverse proxy with authentication for the `/api-docs` path
2. Configuring network rules to restrict access to trusted IPs/networks 