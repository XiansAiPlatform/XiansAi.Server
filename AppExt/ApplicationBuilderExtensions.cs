using XiansAi.Server.Auth;
using XiansAi.Server.EndpointExt;

namespace XiansAi.Server;
public static class ApplicationBuilderExtensions
{
    public static WebApplication ConfigureMiddleware(this WebApplication app)
    {
        // Development specific middleware
        if (app.Environment.IsDevelopment())
        {
            ConfigureDevelopmentMiddleware(app);
        }

        // Global middleware
        ConfigureGlobalMiddleware(app);

        // Authentication & Authorization
        ConfigureAuthMiddleware(app);

        // Endpoint mapping
        ConfigureEndpoints(app);

        return app;
    }

    private static void ConfigureDevelopmentMiddleware(WebApplication app)
    {
        app.UseSwagger();
        app.UseSwaggerUI();
        app.MapOpenApi();
    }

    private static void ConfigureGlobalMiddleware(WebApplication app)
    {
        app.UseCors("AllowAll");   
        app.UseExceptionHandler("/error");
    }

    private static void ConfigureAuthMiddleware(WebApplication app)
    {
        app.UseMiddleware<CertificateValidationMiddleware>();
        app.UseCertificateForwarding();
        app.UseAuthentication();
        app.UseAuthorization();
    }

    private static void ConfigureEndpoints(WebApplication app)
    {
        WebClientEndpointExtensions.MapClientEndpoints(app);
        FlowServerEndpointExtensions.MapFlowServerEndpoints(app);
        app.MapControllers();
    }
}
