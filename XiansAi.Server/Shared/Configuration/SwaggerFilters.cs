using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Collections.Generic;

namespace Features.Shared.Configuration
{
    /// <summary>
    /// Custom schema filter to add additional metadata to schemas
    /// </summary>
    public class ExampleSchemaFilter : ISchemaFilter
    {
        public void Apply(OpenApiSchema schema, SchemaFilterContext context)
        {
            // You can customize schemas based on their type here
            // For example, adding examples or format information
            
            if (context.Type == typeof(DateTime) || context.Type == typeof(DateTime?))
            {
                schema.Example = new Microsoft.OpenApi.Any.OpenApiString(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
                schema.Description = schema.Description ?? "UTC datetime in ISO 8601 format";
            }
            
            if (context.Type == typeof(Guid) || context.Type == typeof(Guid?))
            {
                schema.Example = new Microsoft.OpenApi.Any.OpenApiString(Guid.NewGuid().ToString());
                schema.Description = schema.Description ?? "Unique identifier in UUID format";
            }
        }
    }

    /// <summary>
    /// Custom operation filter to add additional metadata to operations
    /// </summary>
    public class AuthorizationOperationFilter : IOperationFilter
    {
        public void Apply(OpenApiOperation operation, OperationFilterContext context)
        {
            // Add authorization responses to operations that require authorization
            var authAttributes = context.MethodInfo?.DeclaringType?.GetCustomAttributes(true)
                .Union(context.MethodInfo?.GetCustomAttributes(true) ?? Array.Empty<object>())
                .OfType<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>();
            
            if (authAttributes?.Any() == true)
            {
                operation.Responses.TryAdd("401", new OpenApiResponse { Description = "Unauthorized - Authentication required" });
                operation.Responses.TryAdd("403", new OpenApiResponse { Description = "Forbidden - Insufficient permissions" });
            }
        }
    }
} 