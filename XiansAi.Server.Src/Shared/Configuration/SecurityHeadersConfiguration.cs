using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Features.Shared.Configuration;

/// <summary>
/// Configuration for security headers middleware to protect against XSS, clickjacking, MIME-sniffing, and other attacks.
/// </summary>
public static class SecurityHeadersConfiguration
{
    /// <summary>
    /// Adds security headers middleware to the application pipeline.
    /// Configures CSP, X-Frame-Options, X-Content-Type-Options, and other security headers.
    /// </summary>
    public static WebApplication UseSecurityHeaders(this WebApplication app)
    {
        var isDevelopment = app.Environment.IsDevelopment();
        
        app.Use(async (context, next) =>
        {
            // Content Security Policy (CSP) - Mitigates XSS attacks
            // More permissive in development, strict in production
            var cspPolicy = isDevelopment 
                ? BuildDevelopmentCspPolicy() 
                : BuildProductionCspPolicy();
            
            context.Response.Headers.Append("Content-Security-Policy", cspPolicy);
            
            // Prevent MIME type sniffing - Prevents browsers from MIME-sniffing responses
            context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
            
            // Prevent clickjacking - Prevents the page from being embedded in iframes
            context.Response.Headers.Append("X-Frame-Options", "DENY");
            
            // XSS Protection for legacy browsers (deprecated but still useful for older browsers)
            context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
            
            // Referrer Policy - Controls how much referrer information is shared
            context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
            
            // Permissions Policy (formerly Feature-Policy) - Controls browser features
            context.Response.Headers.Append("Permissions-Policy", 
                "geolocation=(), microphone=(), camera=(), payment=(), usb=(), magnetometer=(), gyroscope=(), accelerometer=()");
            
            // Cross-Domain Policies - Controls cross-domain access for Adobe products
            context.Response.Headers.Append("X-Permitted-Cross-Domain-Policies", "none");
            
            // HSTS (HTTP Strict Transport Security) - Force HTTPS (production only)
            if (!isDevelopment)
            {
                context.Response.Headers.Append("Strict-Transport-Security", 
                    "max-age=31536000; includeSubDomains; preload");
            }
            
            await next();
        });
        
        return app;
    }
    
    /// <summary>
    /// Builds a development-friendly Content Security Policy.
    /// More permissive to allow hot-reload, debugging tools, etc.
    /// </summary>
    private static string BuildDevelopmentCspPolicy()
    {
        return string.Join("; ", new[]
        {
            "default-src 'self'",
            "script-src 'self' 'unsafe-inline' 'unsafe-eval'", // Allow inline scripts for dev tools
            "style-src 'self' 'unsafe-inline'", // Allow inline styles for dev tools
            "img-src 'self' data: https:", // Allow images from self, data URIs, and HTTPS sources
            "font-src 'self' data:", // Allow fonts from self and data URIs
            "connect-src 'self' ws: wss: http: https:", // Allow WebSocket and HTTP connections
            "frame-ancestors 'none'", // Prevent embedding in iframes
            "base-uri 'self'", // Restrict base tag URLs
            "form-action 'self'", // Restrict form submissions
            "object-src 'none'", // Block plugins like Flash
            "upgrade-insecure-requests" // Upgrade HTTP to HTTPS when possible
        });
    }
    
    /// <summary>
    /// Builds a strict production Content Security Policy.
    /// Restricts resource loading to prevent XSS attacks.
    /// </summary>
    private static string BuildProductionCspPolicy()
    {
        return string.Join("; ", new[]
        {
            "default-src 'self'",
            "script-src 'self'", // Only allow scripts from same origin
            "style-src 'self'", // Only allow styles from same origin
            "img-src 'self' data: https:", // Allow images from self, data URIs, and HTTPS sources
            "font-src 'self'", // Only allow fonts from same origin
            "connect-src 'self' wss: https:", // Allow secure WebSocket and HTTPS connections
            "frame-ancestors 'none'", // Prevent embedding in iframes
            "base-uri 'self'", // Restrict base tag URLs
            "form-action 'self'", // Restrict form submissions to same origin
            "object-src 'none'", // Block plugins like Flash
            "upgrade-insecure-requests" // Upgrade HTTP to HTTPS
        });
    }
}

