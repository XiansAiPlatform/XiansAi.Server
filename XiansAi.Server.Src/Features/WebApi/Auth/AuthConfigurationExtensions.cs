using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Caching.Memory;
using Features.WebApi.Auth.Providers.Auth0;
using Features.WebApi.Auth.Providers.AzureB2C;
using Features.WebApi.Auth.Providers;
using Features.WebApi.Auth.Providers.Tokens;

namespace Features.WebApi.Auth;

public static class AuthConfigurationExtensions
{
    public static WebApplicationBuilder AddWebApiAuth(this WebApplicationBuilder builder)
    {
        // Register memory cache if not already registered
        builder.Services.AddMemoryCache();
        
        // Register token validation cache - using no-op implementation to disable caching
        builder.Services.AddScoped<ITokenValidationCache, NoOpTokenValidationCache>();
        
        // Register token services
        builder.Services.AddScoped<Auth0TokenService>();
        builder.Services.AddScoped<AzureB2CTokenService>();
        builder.Services.AddScoped<ITokenServiceFactory, TokenServiceFactory>();
        
        // Register auth providers
        builder.Services.AddScoped<Auth0Provider>();
        builder.Services.AddScoped<AzureB2CProvider>();
        builder.Services.AddScoped<IAuthProviderFactory, AuthProviderFactory>();
        builder.Services.AddScoped<IAuthMgtConnect, AuthMgtConnect>();

        // Get the configured provider
        var providerConfig = builder.Configuration.GetSection("AuthProvider").Get<AuthProviderConfig>() ?? 
            new AuthProviderConfig();

        // Add Authentication - don't set defaults to avoid conflicts with other authentication schemes
        builder.Services.AddAuthentication()
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
                policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation));
                policy.RequireRole(SystemRoles.SysAdmin);
            });

            options.AddPolicy("RequireTenantAdmin", policy =>
            {
                policy.AuthenticationSchemes.Add("JWT");
                policy.Requirements.Add(new AuthRequirement(AuthRequirementOptions.FullTenantValidation));
                policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin);
            });
        });

        // Register the unified authorization handler
        builder.Services.AddScoped<IAuthorizationHandler, AuthRequirementHandler>();
        
        // For backward compatibility during transition, keep the old handlers registered
        builder.Services.AddScoped<IAuthorizationHandler, ValidTenantHandler>();
        builder.Services.AddScoped<IAuthorizationHandler, TokenClientHandler>();

        return builder;
    }
} 