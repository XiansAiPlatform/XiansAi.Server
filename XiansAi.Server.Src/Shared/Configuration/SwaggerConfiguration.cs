using Microsoft.OpenApi.Models;
using System.Collections.Generic;

namespace Features.Shared.Configuration;

public static class SwaggerConfiguration
{
    public static WebApplication UseSwaggerConfiguration(this WebApplication app)
    {
        app.UseSwagger(c =>
        {
            // Change the path for Swagger JSON Endpoints
            c.RouteTemplate = "api-docs/{documentName}/swagger.json";
            
            // Modify Swagger with Request Context to set server URL
            c.PreSerializeFilters.Add((swagger, httpReq) =>
            {
                swagger.Servers = new List<OpenApiServer> { 
                    new OpenApiServer { Url = $"{httpReq.Scheme}://{httpReq.Host.Value}" } 
                };
            });
        });
        
        app.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/api-docs/v1/swagger.json", "XiansAI API v1");
            c.RoutePrefix = "api-docs";
            c.DocumentTitle = "XiansAI API Documentation";
            c.DefaultModelsExpandDepth(-1); // Hide models section by default
            c.DisplayRequestDuration(); // Show request duration
            c.EnableDeepLinking(); // Enable deep linking for operations and tags
            c.EnableFilter(); // Enable filtering by tag
            c.EnableTryItOutByDefault(); // Enable TryItOut by default
        });
        
        return app;
    }
} 