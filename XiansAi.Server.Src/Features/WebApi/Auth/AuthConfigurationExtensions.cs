using Microsoft.AspNetCore.Authorization;
using Shared.Providers.Auth;

namespace Features.WebApi.Auth;

public static class AuthConfigurationExtensions
{
    public static WebApplicationBuilder AddWebApiAuth(this WebApplicationBuilder builder)
    {
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
                policy.RequireRole(SystemRoles.SysAdmin, SystemRoles.TenantAdmin, SystemRoles.TenantUser);
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