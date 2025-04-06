using Microsoft.OpenApi.Models;
using System.Collections.Generic;

namespace Features.Shared.Configuration;

public static class HealthConfiguration
{
    public static WebApplication UseHealthChecks(this WebApplication app)
    {
        app.MapHealthChecks("/api/health").WithOpenApi(operation => {
            operation.Summary = "API Health Check";
            operation.Description = "Returns 200 OK when the API is running";
            operation.Tags = new List<OpenApiTag> { new() { Name = "System" }};
            return operation;
        });
        
        return app;
    }
} 