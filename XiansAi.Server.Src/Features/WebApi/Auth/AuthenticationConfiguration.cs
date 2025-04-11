using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using Features.WebApi.Auth;

namespace Features.WebApi.Configuration;

public static class AuthenticationConfiguration
{
    public static WebApplicationBuilder AddAuthenticationServices(this WebApplicationBuilder builder)
    {
        // Add Auth
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "JWT";
            options.DefaultChallengeScheme = "JWT";
        })
        .AddJwtBearer("JWT", options =>
        {
            options.RequireHttpsMetadata = false;
            options.Authority = builder.Configuration["Auth0:Domain"];
            options.Audience = builder.Configuration["Auth0:Audience"];
            options.Events = new JwtBearerEvents
            {
                OnTokenValidated = context =>
                {
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                    {
                        // Set the User property of HttpContext
                        context.HttpContext.User = context.Principal;
                    }
                    return Task.CompletedTask;
                }
            };
        });
        
        return builder;
    }
    
    public static WebApplicationBuilder AddAuthorizationServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireTenantAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new TenantClientRequirement(builder.Configuration));
            });
            options.AddPolicy("RequireTokenAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new Auth0ClientRequirement(builder.Configuration));
            });
        });
        
        // Register authorization handlers
        builder.Services.AddScoped<IAuthorizationHandler, TenantClientHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, Auth0ClientHandler>();
        
        return builder;
    }
} 