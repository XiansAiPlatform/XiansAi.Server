using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Features.WebApi.Endpoints;
using Features.WebApi.Auth;
using Features.WebApi.Repositories;
using Features.WebApi.Services;
using XiansAi.Server.Features.WebApi.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Features.WebApi.Configuration;

public static class WebApiConfiguration
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
                policy.Requirements.Add(new TenantClientRequirement(builder.Configuration));
            });
            options.AddPolicy("RequireTokenAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new Auth0ClientRequirement(builder.Configuration));
            });
        });

        return builder;
    }
    public static WebApplicationBuilder AddWebApiServices(this WebApplicationBuilder builder)
    {
        // Register authorization handlers
        builder.Services.AddScoped<IAuthorizationHandler, TenantClientHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, Auth0ClientHandler>();

        // Register Web API specific services
        builder.Services.AddSingleton<IAuth0MgtAPIConnect, Auth0MgtAPIConnect>();
        builder.Services.AddScoped<WorkflowStarterService>();
        builder.Services.AddScoped<WorkflowEventsService>();
        builder.Services.AddScoped<IWorkflowFinderService, WorkflowFinderService>();
        builder.Services.AddScoped<WorkflowCancelService>();
        builder.Services.AddScoped<CertificateService>();
        builder.Services.AddScoped<InstructionsService>();
        builder.Services.AddScoped<DefinitionsService>();
        builder.Services.AddScoped<TenantService>();
        builder.Services.AddScoped<WebhookService>();
        builder.Services.AddScoped<ActivitiesService>();
        builder.Services.AddScoped<IMessagingService, MessagingService>();
        
        // Register repositories
        builder.Services.AddScoped<IInstructionRepository, InstructionRepository>();
        builder.Services.AddScoped<IFlowDefinitionRepository, FlowDefinitionRepository>();
        builder.Services.AddScoped<IActivityRepository, ActivityRepository>();
        builder.Services.AddScoped<IWebhookRepository, WebhookRepository>();
        builder.Services.AddScoped<ITenantRepository, TenantRepository>();
        
        return builder;
    }
    
    public static WebApplication UseWebApiEndpoints(this WebApplication app)
    {
        // Map Web API endpoints
        WorkflowEndpointExtensions.MapWorkflowEndpoints(app);
        InstructionEndpointExtensions.MapInstructionEndpoints(app);
        ActivityEndpointExtensions.MapActivityEndpoints(app);
        SettingsEndpointExtensions.MapSettingsEndpoints(app);
        DefinitionsEndpointExtensions.MapDefinitionsEndpoints(app);
        TenantEndpointExtensions.MapTenantEndpoints(app);
        WebhookEndpointExtensions.MapWebhookEndpoints(app);
        PublicEndpointExtensions.MapPublicEndpoints(app);
        MessagingEndpointExtensions.MapMessagingEndpoints(app);
        
        return app;
    }
} 