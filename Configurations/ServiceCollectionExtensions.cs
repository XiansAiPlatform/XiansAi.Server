using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Certificate;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.AddCorsConfiguration()
              .AddAuthConfiguration()
              .AddClientServices()
              .AddEndpoints()
              .AddApiConfiguration();

        return builder.Services;
    }

    private static WebApplicationBuilder AddCorsConfiguration(this WebApplicationBuilder builder)
    {
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
        return builder;
    }

    private static WebApplicationBuilder AddClientServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ITenantContext, TenantContext>();

        builder.Services.AddScoped<ITemporalClientService>(sp =>
            new TemporalClientService(sp.GetRequiredService<ITenantContext>().GetTemporalConfig()));

        builder.Services.AddScoped<IOpenAIClientService>(sp =>
            new OpenAIClientService(sp.GetRequiredService<ITenantContext>().GetOpenAIConfig()));

        builder.Services.AddScoped<IMongoDbClientService>(sp =>
            new MongoDbClientService(sp.GetRequiredService<ITenantContext>().GetMongoDBConfig()));

        builder.Services.AddSingleton<CertificateGenerator>();
        return builder;
    }

    private static WebApplicationBuilder AddEndpoints(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<WorkflowStarterEndpoint>();
        builder.Services.AddScoped<WorkflowEventsEndpoint>();
        builder.Services.AddScoped<WorkflowDefinitionEndpoint>();
        builder.Services.AddScoped<WorkflowFinderEndpoint>();
        builder.Services.AddScoped<WorkflowCancelEndpoint>();
        builder.Services.AddScoped<CertificateEndpoint>();
        builder.Services.AddScoped<InstructionsEndpoint>();
        builder.Services.AddScoped<InstructionsServerEndpoint>();
        return builder;
    }

    private static WebApplicationBuilder AddApiConfiguration(this WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Flowmaxer API", Version = "v1" });
        });
        builder.Services.AddOpenApi();
        builder.Services.AddControllers();
        return builder;
    }

    private static WebApplicationBuilder AddAuthConfiguration(this WebApplicationBuilder builder)
    {
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "JWT";
            options.DefaultChallengeScheme = "JWT";
        })
        .AddJwtBearer("JWT", options =>
        {
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
            options.AddPolicy("RequireJwtAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new JwtClientRequirement(builder.Configuration));
            });
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAuthorizationHandler, JwtClientHandler>();
        return builder;
    }
}