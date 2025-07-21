using Shared.Repositories;
using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Providers.Auth.Auth0;
using Shared.Providers.Auth.AzureB2C;
using Shared.Providers.Auth.Keycloak;
using Shared.Providers.Auth;
using Shared.Auth;
using Shared.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Hosting;

namespace Features.Shared.Configuration;

public static class SharedConfiguration
{
    public static WebApplicationBuilder AddSharedServices(this WebApplicationBuilder builder)
    {
        // Register memory cache if not already registered
        builder.Services.AddMemoryCache();

        // Register HttpClient for token services
        builder.Services.AddHttpClient();

        // Register token validation cache
        builder.Services.AddScoped<ITokenValidationCache, NoOpTokenValidationCache>();

        // Register token services
        builder.Services.AddScoped<Auth0TokenService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<Auth0TokenService>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            return new Auth0TokenService(logger, configuration, httpClient);
        });
        builder.Services.AddScoped<AzureB2CTokenService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<AzureB2CTokenService>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            return new AzureB2CTokenService(logger, configuration, httpClient);
        });
        builder.Services.AddScoped<KeycloakTokenService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<KeycloakTokenService>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            
            // In test environment, check if Keycloak configuration exists
            var env = serviceProvider.GetRequiredService<IWebHostEnvironment>();
            if (env.EnvironmentName == "Tests")
            {
                var keycloakSection = configuration.GetSection("Keycloak");
                if (!keycloakSection.Exists() || keycloakSection.Get<KeycloakConfig>() == null)
                {
                    // Return a mock KeycloakTokenService for tests
                    return new KeycloakTokenService(logger, GetTestKeycloakConfiguration(), httpClient);
                }
            }
            
            return new KeycloakTokenService(logger, configuration, httpClient);
        });
        builder.Services.AddScoped<ITokenServiceFactory, TokenServiceFactory>();

        // Register auth providers
        builder.Services.AddScoped<Auth0Provider>();
        builder.Services.AddScoped<AzureB2CProvider>();
        builder.Services.AddScoped<KeycloakProvider>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<KeycloakProvider>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var tokenService = serviceProvider.GetRequiredService<KeycloakTokenService>();
            
            // In test environment, check if Keycloak configuration exists
            var env = serviceProvider.GetRequiredService<IWebHostEnvironment>();
            if (env.EnvironmentName == "Tests")
            {
                var keycloakSection = configuration.GetSection("Keycloak");
                if (!keycloakSection.Exists() || keycloakSection.Get<KeycloakConfig>() == null)
                {
                    // Return a mock KeycloakProvider for tests
                    return new KeycloakProvider(logger, tokenService, GetTestKeycloakConfiguration());
                }
            }
            
            return new KeycloakProvider(logger, tokenService, configuration);
        });
        builder.Services.AddScoped<IAuthProviderFactory, AuthProviderFactory>();
        builder.Services.AddScoped<IAuthMgtConnect, AuthMgtConnect>();

        // Add services using specialized configuration classes
        builder = builder
            .AddCorsConfiguration();
            
        // Add infrastructure services (clients, data access, etc.)
        builder.Services.AddInfrastructureServices(builder.Configuration);
        
        // Add common api-related services
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();
        builder.Services.AddServerOpenApi();
        
        // Configure controllers with model validation
        builder.Services.AddControllers(options =>
        {
            options.ModelValidatorProviders.Clear();
        })
        .ConfigureApiBehaviorOptions(options =>
        {
            // Automatically return 400 Bad Request for model validation errors
            options.InvalidModelStateResponseFactory = context =>
            {
                var errors = context.ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .SelectMany(x => x.Value?.Errors.Select(e => e.ErrorMessage) ?? Array.Empty<string>())
                    .ToList();
                
                return new BadRequestObjectResult(new
                {
                    error = "Validation failed",
                    errors = errors
                });
            };
        });
        
        // Enable model validation for minimal APIs
        builder.Services.Configure<ApiBehaviorOptions>(options =>
        {
            options.SuppressModelStateInvalidFilter = false;
        });
        
        // Add health checks
        builder.Services.AddHealthChecks();
        
        // Register TimeProvider for DI
        builder.Services.AddSingleton(TimeProvider.System);
        
        // Add HttpContextAccessor for access to the current HttpContext
        builder.Services.AddHttpContextAccessor();

        // Register repositories
        builder.Services.AddScoped<IConversationThreadRepository, ConversationThreadRepository>();
        builder.Services.AddScoped<IConversationMessageRepository, ConversationMessageRepository>();
        builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IAgentRepository, AgentRepository>();
        builder.Services.AddScoped<IAgentPermissionRepository, AgentPermissionRepository>();
        builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();

        // Register Utility service
        builder.Services.AddScoped<IMarkdownService, MarkdownService>();

        // Register services
        builder.Services.AddScoped<IWorkflowSignalService, WorkflowSignalService>();
        builder.Services.AddScoped<IMessageService, MessageService>();
        builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
        builder.Services.AddScoped<IPermissionsService, PermissionsService>();
        builder.Services.AddHttpClient();              
        builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
        builder.Services.AddScoped<IWebhookService, WebhookService>();
        builder.Services.AddScoped<IRoleCacheService, RoleCacheService>();
        builder.Services.AddScoped<IUserTenantService, UserTenantService>();
        builder.Services.AddScoped<IUserManagementService, UserManagementService>();

        return builder;
    }
    
    public static WebApplication UseSharedMiddleware(this WebApplication app)
    {
        // Configure environment-specific middleware
        if (app.Environment.IsDevelopment())
        {
            // Development-only middleware here (if any)
        }
        
        // Configure middleware using specialized configuration classes
        app = app
            .UseSwaggerConfiguration()
            .UseExceptionHandlingConfiguration();
        
        // Get policy name from configuration
        var corsSettings = app.Configuration.GetSection("Cors").Get<CorsSettings>() ?? new CorsSettings();
        
        // Configure standard middleware
        app.UseCors(corsSettings.PolicyName);
        app.UseAuthentication();
        app.UseAuthorization();

        // Map health checks
        app.MapHealthChecks("/health");

        return app;
    }
    
    private static IConfiguration GetTestKeycloakConfiguration()
    {
        var testConfig = new Dictionary<string, string>
        {
            ["Keycloak:AuthServerUrl"] = "https://test.keycloak.com",
            ["Keycloak:Realm"] = "test-realm",
            ["Keycloak:ValidIssuer"] = "https://test.keycloak.com/realms/test-realm"
        };
        
        return new ConfigurationBuilder()
            .AddInMemoryCollection(testConfig)
            .Build();
    }
}