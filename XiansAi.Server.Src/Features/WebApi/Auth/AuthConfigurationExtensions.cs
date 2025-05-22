using Microsoft.AspNetCore.Authorization;
using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.AzureB2C;
using Features.WebApi.Auth.Providers;

namespace Features.WebApi.Auth;

public static class AuthConfigurationExtensions
{
    public static WebApplicationBuilder AddWebApiAuth(this WebApplicationBuilder builder)
    {
        // Register auth providers
        builder.Services.AddScoped<Auth0Provider>();
        builder.Services.AddScoped<AzureB2CProvider>();
        builder.Services.AddScoped<IAuthProviderFactory, AuthProviderFactory>();
        builder.Services.AddScoped<IAuthMgtConnect, AuthMgtConnect>();

        // Get the configured provider
        var providerConfig = builder.Configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ?? 
            new AuthProviderConfig();

        // Add Authentication
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "JWT";
            options.DefaultChallengeScheme = "JWT";
        })
        .AddJwtBearer("JWT", options =>
        {
            // Create the provider instance to configure JWT
            var serviceProvider = builder.Services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            IAuthProvider authProvider;
            
            // Create the appropriate provider based on configuration
            switch (providerConfig.Provider)
            {
                case AuthProviderType.Auth0:
                    authProvider = new Auth0Provider(loggerFactory.CreateLogger<Auth0Provider>());
                    break;
                case AuthProviderType.AzureB2C:
                    authProvider = new AzureB2CProvider(loggerFactory.CreateLogger<AzureB2CProvider>());
                    break;
                default:
                    throw new ArgumentException($"Unsupported authentication provider: {providerConfig.Provider}");
            }
            
            // Configure JWT options via the provider
            authProvider.ConfigureJwtBearer(options, builder.Configuration);
        });
        
        // Add Authorization
        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("RequireTenantAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new ValidTenantRequirement(builder.Configuration));
            });
            options.AddPolicy("RequireTokenAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new ValidTokenRequirement(builder.Configuration));
            });
        });

        // Register authorization handlers
        builder.Services.AddScoped<IAuthorizationHandler, ValidTenantHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, TokenClientHandler>();

        return builder;
    }
} 