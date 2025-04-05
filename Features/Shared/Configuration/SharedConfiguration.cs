using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using System.Threading.Tasks;
using XiansAi.Server.Auth;
using XiansAi.Server.Services.Lib;
using XiansAi.Server.Services.Web;

namespace XiansAi.Server.Features.Shared.Configuration;

public static class SharedConfiguration
{
    public static WebApplicationBuilder AddSharedServices(this WebApplicationBuilder builder)
    {
        // Add infrastructure services (clients, data access, etc.)
        builder.Services.AddInfrastructureServices(builder.Configuration);
        
        // Add common api-related services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddOpenApi();
        builder.Services.AddControllers();
        
        return builder;
    }
    
    public static WebApplicationBuilder AddSharedConfiguration(this WebApplicationBuilder builder)
    {
        // Add CORS
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                builder.WithOrigins(allowedOrigins ?? Array.Empty<string>())
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials();
            });
        });
        
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
        
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireTenantAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new TenantClientRequirement(builder.Configuration));
            });
            options.AddPolicy("RequireAuth0Auth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new Auth0ClientRequirement(builder.Configuration));
            });
        });
        
        // Register authorization handlers
        builder.Services.AddScoped<IAuthorizationHandler, TenantClientHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, Auth0ClientHandler>();
        
        builder.Services.AddHttpContextAccessor();
        
        return builder;
    }
    
    public static WebApplication UseSharedMiddleware(this WebApplication app)
    {
        // Configure common middleware for both services
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.MapOpenApi();
        }
        
        app.UseCors("AllowAll");
        app.UseExceptionHandler("/error");
        app.UseAuthentication();
        app.UseAuthorization();
        
        return app;
    }
} 