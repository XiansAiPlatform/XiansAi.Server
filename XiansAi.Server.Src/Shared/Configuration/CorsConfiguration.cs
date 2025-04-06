namespace Features.Shared.Configuration;

public static class CorsConfiguration
{
    public static WebApplicationBuilder AddCorsConfiguration(this WebApplicationBuilder builder)
    {
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>();
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", builder =>
            {
                builder.WithOrigins(allowedOrigins ?? Array.Empty<string>())
                       .AllowAnyMethod()
                       .AllowAnyHeader()
                       .AllowCredentials();
            });
        });
        
        return builder;
    }
} 