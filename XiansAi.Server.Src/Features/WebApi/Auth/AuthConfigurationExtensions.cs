using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;

namespace Features.WebApi.Auth;

public static class AuthConfigurationExtensions
{
    public static WebApplicationBuilder AddWebApiAuth(this WebApplicationBuilder builder)
    {
        // Add Authentication
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
        // Add Authorization
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireTenantAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new ValidTenantRequirement(builder.Configuration));
            });
            options.AddPolicy("RequireTokenAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new Auth0ClientRequirement(builder.Configuration));
            });
        });

        return builder;
    }
} 