using Features.WebApi.Auth.Providers;
using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.AzureB2C;
using Features.WebApi.Auth.Providers.Keycloak;
using Features.WebApi.Auth.Providers.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace Features.WebApi.Auth;

public static class AuthConfigurationExtensions
{
    public static WebApplicationBuilder AddWebApiAuth(this WebApplicationBuilder builder)
    {
        // Register memory cache if not already registered
        builder.Services.AddMemoryCache();

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
        builder.Services.AddScoped<ITokenServiceFactory, TokenServiceFactory>();
        
        // Register auth providers
        builder.Services.AddScoped<Auth0Provider>();
        builder.Services.AddScoped<AzureB2CProvider>();
        builder.Services.AddScoped<KeycloakProvider>();
        builder.Services.AddScoped<IAuthProviderFactory, AuthProviderFactory>();
        builder.Services.AddScoped<IAuthMgtConnect, AuthMgtConnect>();

        // Get the configured provider
        // var providerConfig = builder.Configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ?? 
        //     new AuthProviderConfig();

        // Add Authentication with JWT as the default scheme
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = "JWT";
            options.DefaultChallengeScheme = "JWT";
            options.DefaultForbidScheme = "JWT";
        })
        .AddJwtBearer("JWT", options =>
        {
            // Use the service provider to get the appropriate provider
            var serviceProvider = builder.Services.BuildServiceProvider();
            var authProviderFactory = serviceProvider.GetRequiredService<IAuthProviderFactory>();
            var authProvider = authProviderFactory.GetProvider();
            
            // Configure JWT options via the provider
            authProvider.ConfigureJwtBearer(options, builder.Configuration);
        });
        
        // Add Authorization with the unified auth requirement
        builder.Services.AddAuthorization(options =>
        {
            // Policy that only validates the token
            options.AddPolicy("RequireTokenAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.TokenOnly));
            });
            
            // Policy that validates token and tenant ID, but not tenant configuration
            options.AddPolicy("RequireTenantAuth", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation));
            });
            
            // Optional: Add a policy that validates token and tenant ID but not configuration
            options.AddPolicy("RequireTenantAuthWithoutConfig", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.TenantWithoutConfig));
            });

            //role based policies
            options.AddPolicy("RequireSysAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation, SystemRoles.SysAdmin));
            });

            options.AddPolicy("RequireTenantAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation, SystemRoles.SysAdmin, SystemRoles.TenantAdmin));
            });
        });

        // Register the unified authorization handler
        builder.Services.AddScoped<IAuthorizationHandler, AuthRequirementHandler>();

        return builder;
    }
} 