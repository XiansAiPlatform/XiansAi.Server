using System.Security.Claims;
using Azure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using XiansAi.Server.Auth;
using XiansAi.Server.Services.Lib;
using XiansAi.Server.Services.Web;
using XiansAi.Server.GenAi;
using XiansAi.Server.Database;
using XiansAi.Server.Temporal;
using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Security.KeyVault.Secrets;

namespace XiansAi.Server;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.AddCorsConfiguration()
              //.AddKeyVaultConfiguration()
              .AddAuthConfiguration()
              .AddServerServices()
              .AddClientServices()
              .AddEndpoints()
              .AddApiConfiguration();

        return builder.Services;
    }

    private static WebApplicationBuilder AddKeyVaultConfiguration(this WebApplicationBuilder builder)
    {
        if (builder.Environment.IsProduction())
        {
            try
            {
                var keyVaultUri = builder.Configuration["KeyVaultUri"];
                if (string.IsNullOrEmpty(keyVaultUri))
                {
                    throw new InvalidOperationException("KeyVaultUri configuration is missing");
                }

                Console.WriteLine("Registering key vault configuration");
                Console.WriteLine($"Adding Key Vault Configuration with url: {keyVaultUri}");

                var credential = new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    ExcludeSharedTokenCacheCredential = true,
                    ExcludeVisualStudioCredential = true,
                    ExcludeVisualStudioCodeCredential = true
                });

                builder.Configuration.AddAzureKeyVault(
                    new Uri(keyVaultUri),
                    credential,
                    new KeyVaultSecretManager());
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error configuring Azure Key Vault: {ex.Message}");
                throw;
            }
        }
        return builder;
    }

    private static WebApplicationBuilder AddServerServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<IKeyVaultService, KeyVaultService>();
        builder.Services.AddSingleton<IAuth0MgtAPIConnect, Auth0MgtAPIConnect>();
        builder.Services.AddStackExchangeRedisCache(options =>
        {
            options.Configuration = builder.Configuration.GetRequiredSection("RedisCache:ConnectionString").Value;
            options.InstanceName = "VerificationCodes_";
        });

        builder.Services.AddSingleton<CertificateGenerator>();
        return builder;
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
            new TemporalClientService(
                sp.GetRequiredService<IKeyVaultService>(),
                sp.GetRequiredService<ILogger<TemporalClientService>>(),
                sp.GetRequiredService<ITenantContext>()));

        builder.Services.AddScoped<IOpenAIClientService>(sp =>
            new OpenAIClientService(
                sp.GetRequiredService<ITenantContext>().GetOpenAIConfig(),
                sp.GetRequiredService<IKeyVaultService>(),
                sp.GetRequiredService<ILogger<OpenAIClientService>>(),
                sp.GetRequiredService<ITenantContext>()));

        builder.Services.AddScoped<IMongoDbClientService>(sp =>
            new MongoDbClientService(
                sp.GetRequiredService<ITenantContext>().GetMongoDBConfig(),
                sp.GetRequiredService<IKeyVaultService>(),
                sp.GetRequiredService<ITenantContext>()));

        builder.Services.AddScoped<IEmailService, EmailService>();
        builder.Services.AddScoped<IDatabaseService, DatabaseService>();
        return builder;
    }

    private static WebApplicationBuilder AddEndpoints(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<WorkflowStarterEndpoint>();
        builder.Services.AddScoped<WorkflowSignalEndpoint>();
        builder.Services.AddScoped<WorkflowEventsEndpoint>();
        builder.Services.AddScoped<WorkflowFinderEndpoint>();
        builder.Services.AddScoped<WorkflowCancelEndpoint>();
        builder.Services.AddScoped<CertificateEndpoint>();
        builder.Services.AddScoped<InstructionsEndpoint>();
        builder.Services.AddScoped<InstructionsServerEndpoint>();
        builder.Services.AddScoped<ActivitiesServerEndpoint>();
        builder.Services.AddScoped<ActivitiesEndpoint>();
        builder.Services.AddScoped<DefinitionsServerEndpoint>();
        builder.Services.AddScoped<DefinitionsEndpoint>();
        builder.Services.AddScoped<RegistrationEndpoint>();
        return builder;
    }

    private static WebApplicationBuilder AddApiConfiguration(this WebApplicationBuilder builder)
    {
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Xians.ai API", Version = "v1" });
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
                //policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new TenantClientRequirement(builder.Configuration));
            });
            options.AddPolicy("RequireAuth0Auth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new Auth0ClientRequirement(builder.Configuration));
            });
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<IAuthorizationHandler, TenantClientHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, Auth0ClientHandler>();
        return builder;
    }
}

public class PrefixKeyVaultSecretManager : KeyVaultSecretManager
{
    public override string GetKey(KeyVaultSecret secret)
    {
        return secret.Name.Replace("--", ":");
    }
}