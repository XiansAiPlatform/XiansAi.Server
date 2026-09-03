using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Shared.Providers.Auth;

namespace Features.WebApi.Auth;

public static class AuthConfigurationExtensions
{
    public static WebApplicationBuilder AddWebApiAuth(this WebApplicationBuilder builder)
    {
        // Add Authentication without setting global defaults - let each endpoint specify its own scheme
        builder.Services.AddAuthentication()
            .AddJwtBearer("JWT", _ => { });

        // Never call BuildServiceProvider() during service registration — it creates a second
        // root container whose hosted services can deadlock the real host at RunAsync().
        builder.Services.AddOptions<JwtBearerOptions>("JWT")
            .Configure<IAuthProviderFactory, IConfiguration>((options, authProviderFactory, configuration) =>
            {
                var authProvider = authProviderFactory.GetProvider();
                authProvider.ConfigureJwtBearer(options, configuration);
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