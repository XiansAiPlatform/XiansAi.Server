using Features.WebApi.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils.GenAi;
using XiansAi.Server.Shared.Auth;
using XiansAi.Server.Shared.Repositories;
using XiansAi.Server.Shared.Services;
using XiansAi.Server.Shared.Websocket;
using Microsoft.AspNetCore.Mvc;
using XiansAi.Server.Shared.Webhooks;
namespace Features.Shared.Configuration;

public static class SharedConfiguration
{
    public static WebApplicationBuilder AddSharedServices(this WebApplicationBuilder builder)
    {
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

        // Configure authentication with both schemes in a single call
        builder.Services.AddAuthentication(options =>
        {
            // Optionally set a default scheme if desired
            // options.DefaultScheme = "WebhookApiKeyScheme";
        })
        .AddScheme<AuthenticationSchemeOptions, WebsocketAuthenticationHandler>(
            "WebSocketApiKeyScheme", options => { })
        .AddScheme<AuthenticationSchemeOptions, WebhookAuthenticationHandler>(
            "WebhookApiKeyScheme", options => { });

        // Register both authorization handlers
        builder.Services.AddScoped<IAuthorizationHandler, ValidWebsocketAccessHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, ValidWebhookAccessHandler>();

        // Add both authorization policies
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("WebsocketAuthPolicy", policy =>
            {
                policy.AddAuthenticationSchemes("WebSocketApiKeyScheme");
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ValidWebsocketAccessRequirement());
            });
            options.AddPolicy("WebhookAuthPolicy", policy =>
            {
                policy.AddAuthenticationSchemes("WebhookApiKeyScheme");
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ValidWebhookAccessRequirement());
            });
        });

        // Add SignalR services
        builder.Services.AddSignalR();

        builder.Services.AddSingleton<MongoChangeStreamService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<MongoChangeStreamService>());

        // Register repositories
        builder.Services.AddScoped<IConversationThreadRepository, ConversationThreadRepository>();
        builder.Services.AddScoped<IConversationMessageRepository, ConversationMessageRepository>();
        builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IAgentRepository, AgentRepository>();
        builder.Services.AddScoped<IAgentPermissionRepository, AgentPermissionRepository>();
        builder.Services.AddScoped<IApiKeyRepository, ApiKeyRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();

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

        WebhookTriggerEndpoints.MapWebhookTriggerEndpoints(app);
        // Configure Websocket
        app.MapHub<ChatHub>("/ws/chat");

        return app;
    }
}