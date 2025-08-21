using Shared.Repositories;
using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Shared.Providers.Auth.Auth0;
using Shared.Providers.Auth.AzureB2C;
using Shared.Providers.Auth.Keycloak;
using Shared.Providers.Auth;
using Shared.Auth;
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
            return new KeycloakTokenService(logger, configuration, httpClient);
        });
        builder.Services.AddScoped<ITokenServiceFactory, TokenServiceFactory>();

        // Register auth providers
        builder.Services.AddScoped<Auth0Provider>();
        builder.Services.AddScoped<AzureB2CProvider>();
        builder.Services.AddScoped<KeycloakProvider>();
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
        builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
        builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IAgentRepository, AgentRepository>();
        builder.Services.AddScoped<IAgentPermissionRepository, AgentPermissionRepository>();
        builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        builder.Services.AddScoped<ITenantOidcConfigRepository, TenantOidcConfigRepository>();

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
        builder.Services.AddScoped<ITenantOidcConfigService, TenantOidcConfigService>();
        builder.Services.AddScoped<ISecureEncryptionService, SecureEncryptionService>();

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
}