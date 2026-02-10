using Shared.Repositories;
using Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Shared.Providers.Auth.Auth0;
using Shared.Providers.Auth.AzureB2C;
using Shared.Providers.Auth.GitHub;
using Shared.Providers.Auth.Keycloak;
using Shared.Providers.Auth.Oidc;
using Shared.Providers.Auth;
using Shared.Providers.Secret;
using Shared.Auth;
using Shared.Utils;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;

namespace Features.Shared.Configuration;

public static class SharedConfiguration
{
    public static WebApplicationBuilder AddSharedServices(this WebApplicationBuilder builder)
    {
        // Configure forwarded headers for production deployments behind reverse proxy/load balancer
        // This is critical for Azure Container Apps, App Service, and other cloud platforms
        // where SSL/TLS termination happens at the load balancer level
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                // Forward the X-Forwarded-For and X-Forwarded-Proto headers
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
                
                // Trust all proxies (safe in Azure Container Apps / App Service)
                // The platform-managed load balancer is trusted
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
                
                // Limit to 1 proxy hop (Azure load balancer)
                options.ForwardLimit = 1;
            });
        }
        
        // Configure HSTS for production (must be before other middleware registration)
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddHsts(options =>
            {
                options.Preload = true;
                options.IncludeSubDomains = true;
                options.MaxAge = TimeSpan.FromDays(365);
            });
        }
        
        // Configure cookie policy for secure cookies in production
        builder.Services.Configure<CookiePolicyOptions>(options =>
        {
            // In production, require HTTPS for all cookies
            // In development, allow HTTP for local testing
            options.Secure = builder.Environment.IsProduction() 
                ? CookieSecurePolicy.Always 
                : CookieSecurePolicy.None;
            
            // Use Lax for better compatibility with modern auth flows
            // Strict would block OAuth redirects
            options.MinimumSameSitePolicy = SameSiteMode.Lax;
            
            // Only check consent in production
            options.CheckConsentNeeded = context => builder.Environment.IsProduction();
        });
        
        // Register memory cache with size limit to prevent DoS attacks via cache exhaustion
        // Default size limit: 10,000 entries (configurable via Auth:TokenValidationCacheSizeLimit)
        var cacheSizeLimit = builder.Configuration.GetValue<long>("Auth:TokenValidationCacheSizeLimit", 10000);
        builder.Services.AddMemoryCache(options =>
        {
            options.SizeLimit = cacheSizeLimit;
        });

        // Register HttpClient for token services
        builder.Services.AddHttpClient();

        // Register token validation cache
        builder.Services.AddScoped<ITokenValidationCache, MemoryTokenValidationCache>();

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
        builder.Services.AddScoped<OidcTokenService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<OidcTokenService>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var cache = serviceProvider.GetRequiredService<IMemoryCache>();
            return new OidcTokenService(logger, configuration, cache);
        });
        builder.Services.AddScoped<GitHubTokenService>(serviceProvider =>
        {
            var logger = serviceProvider.GetRequiredService<ILogger<GitHubTokenService>>();
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            var httpClient = serviceProvider.GetRequiredService<HttpClient>();
            return new GitHubTokenService(logger, configuration, httpClient);
        });
        builder.Services.AddScoped<ITokenServiceFactory, TokenServiceFactory>();

        // Register auth providers
        builder.Services.AddScoped<Auth0Provider>();
        builder.Services.AddScoped<AzureB2CProvider>();
        builder.Services.AddScoped<KeycloakProvider>();
        builder.Services.AddScoped<OidcProvider>();
        builder.Services.AddScoped<GitHubProvider>();
        builder.Services.AddScoped<IAuthProviderFactory, AuthProviderFactory>();
        builder.Services.AddScoped<IAuthMgtConnect, AuthMgtConnect>();

        // Add services using specialized configuration classes
        builder = builder
            .AddCorsConfiguration()
            .AddRateLimiting()
            .AddRequestLimits();
            
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

        // Register custom authorization middleware result handler
        builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationMiddlewareResultHandler>();

        // Register repositories
        builder.Services.AddScoped<IConversationRepository, ConversationRepository>();
        builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IAgentRepository, AgentRepository>();
        builder.Services.AddScoped<IAgentPermissionRepository, AgentPermissionRepository>();
        builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        builder.Services.AddScoped<IUserRepository, UserRepository>();
        builder.Services.AddScoped<IInvitationRepository, InvitationRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        builder.Services.AddScoped<ITenantOidcConfigRepository, TenantOidcConfigRepository>();
        builder.Services.AddScoped<IUsageEventRepository, UsageEventRepository>();
        builder.Services.AddScoped<IActivationRepository, ActivationRepository>();

        // Register Utility services
        builder.Services.AddScoped<IMarkdownService, MarkdownService>();
        builder.Services.AddScoped<IJwtClaimsExtractor, JwtClaimsExtractor>();

        // Register services
        builder.Services.AddScoped<IWorkflowSignalService, WorkflowSignalService>();
        builder.Services.AddScoped<IMessageService, MessageService>();
        builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
        builder.Services.AddScoped<IPermissionsService, PermissionsService>();
        builder.Services.AddScoped<IUsageEventService, UsageEventService>();
        builder.Services.AddHttpClient();              
        builder.Services.AddScoped<IApiKeyService, ApiKeyService>();
        builder.Services.AddScoped<IRoleCacheService, RoleCacheService>();
        builder.Services.AddScoped<IUserTenantService, UserTenantService>();
        builder.Services.AddScoped<IUserManagementService, UserManagementService>();
        builder.Services.AddScoped<ITenantOidcConfigService, TenantOidcConfigService>();
        builder.Services.AddSingleton<ISecureEncryptionService, SecureEncryptionService>();
        
        // Register secret vault repository and providers
        builder.Services.AddScoped<ISecretRepository, SecretRepository>();
        builder.Services.AddScoped<DatabaseSecretVaultProvider>();
        builder.Services.AddScoped<AzureKeyVaultProvider>();
        builder.Services.AddScoped<AwsSecretsManagerProvider>();
        builder.Services.AddScoped<HashiCorpVaultProvider>();
        
        // Register secret vault service with provider factory
        builder.Services.AddScoped<ISecretVaultProvider>(serviceProvider =>
        {
            var configuration = serviceProvider.GetRequiredService<IConfiguration>();
            return SecretVaultProviderFactory.CreateProvider(configuration, serviceProvider);
        });
        builder.Services.AddScoped<ISecretVaultService, SecretVaultService>();

        // Configure JSON serialization options for minimal APIs
        // This ensures enums are serialized as strings instead of numeric values
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        });

        return builder;
    }
    
    public static WebApplication UseSharedMiddleware(this WebApplication app)
    {
        // Configure environment-specific middleware
        if (app.Environment.IsDevelopment())
        {
            // Development-only middleware here (if any)
        }
        else if (app.Environment.IsProduction())
        {
            // Use forwarded headers FIRST - must be before HSTS and HTTPS redirection
            // This allows the app to know the original protocol (HTTPS) even though
            // it receives HTTP from the Azure load balancer
            app.UseForwardedHeaders();
            
            // Production-only security middleware
            // Enable HSTS (HTTP Strict Transport Security)
            app.UseHsts();
            
            // Redirect HTTP to HTTPS
            // This will work correctly because UseForwardedHeaders sets the correct scheme
            app.UseHttpsRedirection();
        }
        
        // Apply comprehensive security headers (XSS, clickjacking, MIME-sniffing protection)
        // This includes CSP, X-Frame-Options, X-Content-Type-Options, Referrer-Policy, etc.
        app.UseSecurityHeaders();
        
        // Configure middleware using specialized configuration classes
        app = app
            .UseSwaggerConfiguration()
            .UseExceptionHandlingConfiguration();
        
        // Get policy name from configuration
        var corsSettings = app.Configuration.GetSection("Cors").Get<CorsSettings>() ?? new CorsSettings();
        
        // Configure standard middleware
        app.UseCors(corsSettings.PolicyName);
        
        // Add rate limiting middleware (must be after routing and before authentication)
        app.UseRateLimitingMiddleware();
        
        app.UseAuthentication();
        app.UseAuthorization();

        // Map health checks
        app.MapHealthChecks("/health");

        return app;
    }
}