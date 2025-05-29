using Shared.Repositories;
using Shared.Services;
using Shared.Utils.GenAi;
using XiansAi.Server.Shared.Repositories;
using XiansAi.Server.Shared.Services;
using Microsoft.AspNetCore.Mvc;

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

        // Register repositories
        builder.Services.AddScoped<IConversationThreadRepository, ConversationThreadRepository>();
        builder.Services.AddScoped<IConversationMessageRepository, ConversationMessageRepository>();
        builder.Services.AddScoped<IKnowledgeRepository, KnowledgeRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IAgentRepository, AgentRepository>();
        builder.Services.AddScoped<IAgentPermissionRepository, AgentPermissionRepository>();


        // Register Utility service
        builder.Services.AddScoped<IMarkdownService, MarkdownService>();

        // Register services
        builder.Services.AddScoped<IWorkflowSignalService, WorkflowSignalService>();
        builder.Services.AddScoped<IMessageService, MessageService>();
        builder.Services.AddScoped<IKnowledgeService, KnowledgeService>();
        builder.Services.AddScoped<IPermissionsService, PermissionsService>();

        
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
        
        return app;
    }
} 