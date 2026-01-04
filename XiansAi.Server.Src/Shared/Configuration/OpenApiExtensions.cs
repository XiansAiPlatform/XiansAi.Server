
using Microsoft.OpenApi.Models;
using System.Reflection;
using System.Text;
using System.Text.Json;
using YamlDotNet.Serialization;

namespace Features.Shared.Configuration;

public static class OpenApiExtensions
{
    public static IServiceCollection AddServerOpenApi(this IServiceCollection services)
    {
        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "XiansAI API",
                Version = "v1",
                Description = "XiansAI Server API Documentation",
                Contact = new OpenApiContact
                {
                    Name = "xians.ai",
                    Url = new Uri("https://xians.ai")
                },
                License = new OpenApiLicense
                {
                    Name = "MIT License",
                }
            });

            // External OpenAPI specifications (like agent-management-admin-api.yaml) are served
            // via dedicated endpoints and referenced in SwaggerUI. The YAML file in wwwroot/api-docs/
            // is automatically included as static content by the .NET SDK.

            // Add JWT Authentication support in Swagger UI
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

            // Add API Key Authentication support
            options.AddSecurityDefinition("apiKey", new OpenApiSecurityScheme
            {
                Description = @"API Key authentication. Use the X-API-Key header with your API key.",
                Name = "X-API-Key",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey
            });

            options.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                },
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "apiKey"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            // Include XML comments if available
            var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
            var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
            if (File.Exists(xmlPath))
            {
                options.IncludeXmlComments(xmlPath);
            }
        });

        return services;
    }

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
            
            // Add Agent Management Admin API document if it was loaded
            // Check if the document exists by trying to access the JSON endpoint
            c.SwaggerEndpoint("/api-docs/agent-management-admin/swagger.json", "Agent Management API (Admin)");
            
            c.RoutePrefix = "api-docs";
            c.DocumentTitle = "XiansAI API Documentation";
            c.DefaultModelsExpandDepth(-1); // Hide models section by default
            c.DisplayRequestDuration(); // Show request duration
            c.EnableDeepLinking(); // Enable deep linking for operations and tags
            c.EnableFilter(); // Enable filtering by tag
            c.EnableTryItOutByDefault(); // Enable TryItOut by default
        });
        
        // Add endpoint to serve the OpenAPI YAML file as JSON (for Swagger UI)
        // Standard approach: External OpenAPI specs are served via endpoints
        // Using YamlDotNet (already in project) to convert YAML to JSON
        app.MapGet("/api-docs/agent-management-admin/swagger.json", async (HttpContext context) =>
        {
            // Use standard ASP.NET Core WebRootPath to locate the YAML file
            var webRootPath = app.Environment.WebRootPath ?? Path.Combine(AppContext.BaseDirectory, "wwwroot");
            var yamlPath = Path.Combine(webRootPath, "api-docs", "agent-management-admin-api.yaml");

            if (!File.Exists(yamlPath))
            {
                context.Response.StatusCode = 404;
                await context.Response.WriteAsync("Agent Management Admin API specification not found");
                return;
            }

            try
            {
                var yamlContent = await File.ReadAllTextAsync(yamlPath);
                
                // Use YamlDotNet to deserialize YAML to object, then serialize to JSON
                var deserializer = new DeserializerBuilder().Build();
                var yamlObject = deserializer.Deserialize<object>(yamlContent);
                
                // Convert to JSON using System.Text.Json
                var jsonContent = JsonSerializer.Serialize(yamlObject, new JsonSerializerOptions
                {
                    WriteIndented = true
                });

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(jsonContent);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Error loading OpenAPI specification: {ex.Message}");
            }
        })
        .ExcludeFromDescription(); // Exclude from main Swagger doc
        
        return app;
    }
}
