namespace Features.Shared.Configuration;

public static class CorsConfiguration
{
    public static WebApplicationBuilder AddCorsConfiguration(this WebApplicationBuilder builder)
    {
        var corsConfig = builder.Configuration.GetSection("Cors").Get<CorsSettings>() ?? new CorsSettings();
        
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(corsConfig.PolicyName, policyBuilder =>
            {
                var corsBuilder = policyBuilder.WithOrigins(corsConfig.AllowedOrigins ?? Array.Empty<string>());
                
                if (corsConfig.AllowAnyMethod)
                {
                    corsBuilder.AllowAnyMethod();
                }
                else if (corsConfig.AllowedMethods?.Length > 0)
                {
                    corsBuilder.WithMethods(corsConfig.AllowedMethods);
                }
                
                // Only allow specific, necessary headers to prevent CORS-based attacks
                var allowedHeaders = new[] 
                { 
                    "Authorization", 
                    "Content-Type",
                    "Accept",
                    "X-Tenant-Id",
                    "X-API-Key"
                };
                
                if (corsConfig.AllowedHeaders?.Length > 0)
                {
                    // Use configured headers if provided
                    corsBuilder.WithHeaders(corsConfig.AllowedHeaders);
                }
                else
                {
                    // Use secure defaults
                    corsBuilder.WithHeaders(allowedHeaders);
                }
                
                if (corsConfig.AllowCredentials)
                {
                    corsBuilder.AllowCredentials();
                }
                
                if (corsConfig.ExposedHeaders?.Length > 0)
                {
                    corsBuilder.WithExposedHeaders(corsConfig.ExposedHeaders);
                }
            });
        });
        
        return builder;
    }
}

public class CorsSettings
{
    public string PolicyName { get; set; } = "AllowAll";
    public string[] AllowedOrigins { get; set; } = Array.Empty<string>();
    public bool AllowAnyMethod { get; set; } = true;
    public string[] AllowedMethods { get; set; } = Array.Empty<string>();
    public bool AllowAnyHeader { get; set; } = false; // Deprecated: Use AllowedHeaders for security
    public string[] AllowedHeaders { get; set; } = Array.Empty<string>();
    public bool AllowCredentials { get; set; } = true;
    public string[] ExposedHeaders { get; set; } = Array.Empty<string>();
} 