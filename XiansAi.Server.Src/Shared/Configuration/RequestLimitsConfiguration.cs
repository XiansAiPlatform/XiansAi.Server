using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Features.Shared.Configuration;

/// <summary>
/// Configuration settings for request size limits to prevent DoS attacks.
/// </summary>
public class RequestLimitsSettings
{
    /// <summary>
    /// Maximum allowed size for the request body in bytes.
    /// Default: 30MB (31,457,280 bytes)
    /// </summary>
    public long MaxRequestBodySize { get; set; } = 31_457_280; // 30MB

    /// <summary>
    /// Maximum allowed size for multipart body sections (file uploads) in bytes.
    /// Default: 128MB (134,217,728 bytes)
    /// </summary>
    public long MultipartBodyLengthLimit { get; set; } = 134_217_728; // 128MB

    /// <summary>
    /// Maximum size of the request buffer in bytes.
    /// Default: 1MB (1,048,576 bytes)
    /// </summary>
    public int MaxRequestBufferSize { get; set; } = 1_048_576; // 1MB
}

/// <summary>
/// Extension methods for configuring request size limits.
/// </summary>
public static class RequestLimitsConfiguration
{
    /// <summary>
    /// Configures request size limits for Kestrel and form options to prevent DoS attacks.
    /// </summary>
    /// <param name="builder">The WebApplicationBuilder instance.</param>
    /// <returns>The WebApplicationBuilder for method chaining.</returns>
    public static WebApplicationBuilder AddRequestLimits(this WebApplicationBuilder builder)
    {
        // Load configuration from appsettings
        var requestLimitsSettings = builder.Configuration
            .GetSection("RequestLimits")
            .Get<RequestLimitsSettings>() ?? new RequestLimitsSettings();

        // Configure Kestrel server limits
        builder.WebHost.ConfigureKestrel(options =>
        {
            // Set global maximum request body size
            options.Limits.MaxRequestBodySize = requestLimitsSettings.MaxRequestBodySize;
            
            // Set maximum request buffer size
            options.Limits.MaxRequestBufferSize = requestLimitsSettings.MaxRequestBufferSize;
            
            // Set maximum request header count to prevent header-based DoS attacks
            options.Limits.MaxRequestHeaderCount = 100;
            
            // Set maximum request header size (default 32KB is reasonable)
            options.Limits.MaxRequestHeadersTotalSize = 32_768; // 32KB
            
            // Set maximum request line size (for URL and method)
            options.Limits.MaxRequestLineSize = 8_192; // 8KB
        });

        // Configure form options for multipart/form-data requests
        builder.Services.Configure<FormOptions>(options =>
        {
            // Set maximum size for multipart body length (file uploads)
            options.MultipartBodyLengthLimit = requestLimitsSettings.MultipartBodyLengthLimit;
            
            // Set maximum size for buffering form values
            options.ValueLengthLimit = int.MaxValue;
            
            // Set maximum count for form values
            options.ValueCountLimit = 1024;
            
            // Set buffer threshold for multipart form data
            options.BufferBodyLengthLimit = requestLimitsSettings.MaxRequestBodySize;
            
            // Set memory buffer threshold (64KB default is reasonable)
            options.MemoryBufferThreshold = 65_536;
        });

        var logger = builder.Services.BuildServiceProvider().GetRequiredService<ILogger<Program>>();
        logger.LogInformation(
            "Request limits configured - MaxRequestBodySize: {MaxRequestBodySize}MB, " +
            "MultipartBodyLengthLimit: {MultipartBodyLengthLimit}MB, " +
            "MaxRequestBufferSize: {MaxRequestBufferSize}KB",
            requestLimitsSettings.MaxRequestBodySize / 1_048_576,
            requestLimitsSettings.MultipartBodyLengthLimit / 1_048_576,
            requestLimitsSettings.MaxRequestBufferSize / 1024);

        return builder;
    }
}

