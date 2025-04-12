using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi.Models;
using System;
using System.IO;
using System.Reflection;

namespace Features.Shared.Configuration.OpenApi;

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

}
