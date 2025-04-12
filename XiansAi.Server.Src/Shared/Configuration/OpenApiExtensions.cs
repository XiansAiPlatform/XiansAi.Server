using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Reflection;

namespace Features.Shared.Configuration
{
    public static class OpenApiExtensions
    {
        public static IServiceCollection AddOpenApi(this IServiceCollection services)
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
                        Name = "Proprietary",
                    }
                });

                // Add JWT Authentication support in Swagger UI
                options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
                {
                    Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
                    Name = "Authorization",
                    In = ParameterLocation.Header,
                    Type = SecuritySchemeType.ApiKey,
                    Scheme = "Bearer"
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

        public static WebApplication MapOpenApi(this WebApplication app)
        {
            app.UseSwagger(options =>
            {
                options.RouteTemplate = "api-docs/{documentName}/swagger.json";
            });
            
            app.UseSwaggerUI(options =>
            {
                options.SwaggerEndpoint("/api-docs/v1/swagger.json", "XiansAI API v1");
                options.RoutePrefix = "api-docs";
                options.DocumentTitle = "XiansAI API Documentation";
                options.DefaultModelsExpandDepth(-1); // Hide models section by default
                options.DisplayRequestDuration(); // Show request duration
                options.EnableDeepLinking(); // Enable deep linking for operations and tags
                options.EnableFilter(); // Enable filtering by tag
                options.EnableTryItOutByDefault(); // Enable TryItOut by default
            });
            
            return app;
        }
    }
} 