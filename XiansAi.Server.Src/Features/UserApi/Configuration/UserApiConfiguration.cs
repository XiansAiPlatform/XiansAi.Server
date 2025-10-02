using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Features.UserApi.Auth;
using Features.UserApi.Services;
using Features.UserApi.Endpoints;

using Shared.Services;
using Shared.Repositories;
using Shared.Auth;

namespace Features.UserApi.Configuration
{
    public static class UserApiConfiguration
    {
        public static WebApplicationBuilder AddUserApiServices(this WebApplicationBuilder builder)
        {
            builder.Services.AddLogging();
            
            // Add Message Event Publisher for SSE support
            builder.Services.AddSingleton<IMessageEventPublisher, MessageEventPublisher>();
            builder.Services.AddSingleton<MongoChangeStreamService>();
            builder.Services.AddHostedService(sp => sp.GetRequiredService<MongoChangeStreamService>());
            
            // Add PendingRequestService for sync messaging support
            builder.Services.AddSingleton<IPendingRequestService, PendingRequestService>();
            
            // Add SignalR services
            AddSignalRServices(builder.Services);

            // OIDC per-tenant config storage and encryption
            builder.Services.AddScoped<ITenantOidcConfigRepository, TenantOidcConfigRepository>();
            builder.Services.AddScoped<ITenantOidcConfigService, TenantOidcConfigService>();
            // ISecureEncryptionService is registered as Singleton in SharedConfiguration
            builder.Services.AddScoped<IDynamicOidcValidator, DynamicOidcValidator>();

            // Add Webhook service
            builder.Services.AddScoped<IWebhookReceiverService, WebhookReceiverService>();

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
                    // Important: Only use the EndpointApiKeyScheme to prevent JWT authentication from running first
                    // This ensures API key authentication is properly handled without JWT interference
                    policy.AuthenticationSchemes.Clear();
                    policy.AddAuthenticationSchemes("EndpointApiKeyScheme");
                    policy.RequireAuthenticatedUser();
                    policy.Requirements.Add(new ValidEndpointAccessRequirement());
                });
            });

            return builder;
        }

        public static void AddSignalRServices(this IServiceCollection services)
        {
            // Register SignalR with optimized settings
            services.AddSignalR(options =>
            {
                // Optimize for bot performance
                options.EnableDetailedErrors = false; // Reduce overhead in production
                options.KeepAliveInterval = TimeSpan.FromSeconds(15); // Balanced keep-alive
                options.ClientTimeoutInterval = TimeSpan.FromSeconds(30); // Quick timeout for stale connections
                options.HandshakeTimeout = TimeSpan.FromSeconds(15); // Fast handshake
                options.MaximumReceiveMessageSize = 32_768; // 32KB limit for bot messages
                options.StreamBufferCapacity = 10; // Optimized buffer size
            })
            .AddJsonProtocol(options =>
            {
                // Optimize JSON serialization for bot messages
                options.PayloadSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
                options.PayloadSerializerOptions.WriteIndented = false;
                options.PayloadSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });
        }

        public static WebApplication UseUserApiEndpoints(this WebApplication app)
        {
            RestEndpoints.MapRestEndpoints(app);
            SseEndpoints.MapSseEndpoints(app);
            SocketEndpoints.MapSocketEndpoints(app);
            WebhookEndpoints.MapWebhookEndpoints(app);

            return app;
        }
    }
}
