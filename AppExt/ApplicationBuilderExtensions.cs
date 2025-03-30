using XiansAi.Server.Auth;
using XiansAi.Server.Auth.Repositories;
using XiansAi.Server.Endpoints;

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

        // Initialize database indexes for permissions and users
        using (var scope = app.Services.CreateScope())
        {
            var indexInitializer = scope.ServiceProvider.GetRequiredService<DatabaseIndexInitializer>();
            Task.Run(async () => 
            {
                try
                {
                    await indexInitializer.InitializeIndexesAsync();
                    app.Logger.LogInformation("Database indexes initialized successfully");
                }
                catch (Exception ex)
                {
                    app.Logger.LogError(ex, "Failed to initialize database indexes");
                }
            }).Wait();
        }

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
        WebEndpointExtensions.MapWebEndpoints(app);
        LibEndpointExtensions.MapLibEndpoints(app);
        PublicEndpointExtensions.MapPublicEndpoints(app);
        AgentEndpointExtensions.MapAgentEndpoints(app);
        app.MapControllers();
    }
}
