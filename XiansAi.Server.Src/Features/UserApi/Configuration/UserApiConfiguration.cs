using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Features.UserApi.Auth;
using Features.UserApi.Websocket;
using Features.UserApi.Services;
using Features.UserApi.Endpoints;

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
            .AddScheme<AuthenticationSchemeOptions, EndpointAuthenticationHandler>(
                "EndpointApiKeyScheme", options => { });

            // Register both authorization handlers
            builder.Services.AddScoped<IAuthorizationHandler, ValidWebsocketAccessHandler>();
            builder.Services.AddScoped<IAuthorizationHandler, ValidEndpointAccessHandler>();

            // Add both authorization policies
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy("WebsocketAuthPolicy", policy =>
                {
                    policy.AddAuthenticationSchemes("WebSocketApiKeyScheme");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new ValidWebsocketAccessRequirement());
                });
                options.AddPolicy("EndpointAuthPolicy", policy =>
                {
                    policy.AddAuthenticationSchemes("EndpointApiKeyScheme");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new ValidEndpointAccessRequirement());
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

            MessagingEndpoints.MapMessagingEndpoints(app);
            // Configure Websocket
            app.MapHub<ChatHub>("/ws/chat");
            app.MapHub<TenantChatHub>("/ws/tenant/chat");

            return app;
        }
    }
}
