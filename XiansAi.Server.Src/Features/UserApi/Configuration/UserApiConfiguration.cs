using Features.AgentApi.Auth;
using Features.Shared.Configuration;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Repositories;
using Shared.Services;
using Features.UserApi.Auth;
using XiansAi.Server.Shared.Repositories;
using XiansAi.Server.Shared.Services;
using Features.UserApi.Webhooks;
using Features.UserApi.Websocket;
using Features.UserApi.Services;

namespace Features.UserApi.Configuration
{
    public static class UserApiConfiguration
    {
        public static WebApplicationBuilder AddUserApiServices(this WebApplicationBuilder builder)
        {
            builder.Services.AddSingleton<MongoChangeStreamService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<MongoChangeStreamService>());
            // Add SignalR services
            builder.Services.AddSignalR();

            return builder;
        }

        public static WebApplicationBuilder AddUserApiAuth(this WebApplicationBuilder builder)
        {
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

            return builder;
        }

        public static WebApplication UseUserApiEndpoints(this WebApplication app)
        {
            // Configure environment-specific middleware
            if (app.Environment.IsDevelopment())
            {
                // Development-only middleware here (if any)
            }

            WebhookTriggerEndpoints.MapWebhookTriggerEndpoints(app);
            // Configure Websocket
            app.MapHub<ChatHub>("/ws/chat");

            return app;
        }
    }
}
