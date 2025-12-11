using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Shared.Auth;

namespace Features.Shared.Configuration;

/// <summary>
/// Extension methods for configuring OpenTelemetry observability
/// Supports two modes:
/// - Development: Direct export to Aspire Dashboard (minimal config)
/// - Production: Export to OTEL Collector for flexible backend routing
/// </summary>
public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Adds OpenTelemetry instrumentation with automatic tracing and metrics
    /// </summary>
    public static WebApplicationBuilder AddOpenTelemetry(this WebApplicationBuilder builder)
    {
        var enabled = builder.Configuration.GetValue<bool>("OpenTelemetry:Enabled", false);
        if (!enabled)
        {
            return builder;
        }

        var serviceName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName") ?? "XiansAi.Server";
        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint") ?? "http://localhost:4317";

        Console.WriteLine($"[OpenTelemetry] Initializing OpenTelemetry for service: {serviceName}");
        Console.WriteLine($"[OpenTelemetry] OTLP Endpoint: {otlpEndpoint}");

        try
        {
            builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceVersion: serviceVersion,
                    serviceInstanceId: Environment.MachineName)
                .AddAttributes(new Dictionary<string, object>
                {
                    ["deployment.environment"] = builder.Environment.EnvironmentName,
                    ["host.name"] = Environment.MachineName,
                }))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation(options =>
                {
                    // Record exception details for better debugging
                    options.RecordException = true;
                    
                    // Filter out health check endpoints to reduce noise
                    options.Filter = context =>
                    {
                        return !context.Request.Path.StartsWithSegments("/health");
                    };
                    
                    // Enrich spans with additional HTTP request information
                    options.EnrichWithHttpRequest = (activity, httpRequest) =>
                    {
                        activity.SetTag("http.method", httpRequest.Method);
                        activity.SetTag("http.scheme", httpRequest.Scheme);
                        activity.SetTag("http.host", httpRequest.Host.Value);
                        
                        // Add tenant context from HTTP headers
                        var tenantId = httpRequest.Headers["X-Tenant-Id"].FirstOrDefault();
                        if (!string.IsNullOrEmpty(tenantId))
                        {
                            activity.SetTag("tenant.id", tenantId);
                        }
                        
                        // Add user context if available
                        var userId = httpRequest.HttpContext?.User?.Identity?.Name;
                        if (!string.IsNullOrEmpty(userId))
                        {
                            activity.SetTag("user.id", userId);
                        }
                    };
                    
                    // Enrich spans with HTTP response information
                    options.EnrichWithHttpResponse = (activity, httpResponse) =>
                    {
                        activity.SetTag("http.status_code", httpResponse.StatusCode);
                        
                        // Add tenant context from DI if available (for requests that have been authenticated)
                        var tenantContext = httpResponse.HttpContext.RequestServices.GetService<ITenantContext>();
                        if (tenantContext != null && !string.IsNullOrEmpty(tenantContext.TenantId))
                        {
                            activity.SetTag("tenant.id", tenantContext.TenantId);
                            activity.SetTag("tenant.user", tenantContext.LoggedInUser);
                            activity.SetTag("tenant.user_type", tenantContext.UserType.ToString());
                            
                            // Add user roles if available
                            if (tenantContext.UserRoles != null && tenantContext.UserRoles.Length > 0)
                            {
                                activity.SetTag("tenant.user_roles", string.Join(",", tenantContext.UserRoles));
                            }
                        }
                    };
                })
                .AddHttpClientInstrumentation(options =>
                {
                    // Record exceptions in outgoing HTTP calls
                    options.RecordException = true;
                    
                    // Enrich with request details
                    options.EnrichWithHttpRequestMessage = (activity, httpRequestMessage) =>
                    {
                        activity.SetTag("http.request.method", httpRequestMessage.Method.ToString());
                        
                        // Propagate tenant context to outgoing requests
                        if (httpRequestMessage.Headers.Contains("X-Tenant-Id"))
                        {
                            var tenantId = httpRequestMessage.Headers.GetValues("X-Tenant-Id").FirstOrDefault();
                            if (!string.IsNullOrEmpty(tenantId))
                            {
                                activity.SetTag("tenant.id", tenantId);
                            }
                        }
                    };
                    
                    // Enrich with response details
                    options.EnrichWithHttpResponseMessage = (activity, httpResponseMessage) =>
                    {
                        activity.SetTag("http.response.status_code", (int)httpResponseMessage.StatusCode);
                    };
                })
                .AddSource("XiansAi.*") // Custom activity sources including XiansAi.Server.Temporal
                .AddSource("XiansAi.Server.Temporal") // Explicitly add server Temporal ActivitySource
                .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources") // MongoDB operations
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                }))
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()    // Request count, duration, etc.
                .AddHttpClientInstrumentation()    // Outgoing HTTP metrics
                .AddRuntimeInstrumentation()       // .NET runtime metrics (GC, memory, threads)
                .AddMeter("XiansAi.*")            // Custom meters
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    // Note: Exporter failures won't break execution - spans/metrics will be buffered or dropped silently
                }));

            Console.WriteLine($"[OpenTelemetry] ✓ OpenTelemetry fully enabled for {serviceName}");
            Console.WriteLine($"[OpenTelemetry]   - Service: {serviceName} v{serviceVersion}");
            Console.WriteLine($"[OpenTelemetry]   - OTLP Endpoint: {otlpEndpoint}");
            Console.WriteLine($"[OpenTelemetry]   - Note: If collector is unreachable, traces/metrics will be buffered or dropped (non-blocking)");
        }
        catch (Exception ex)
        {
            // OpenTelemetry initialization failures should NOT break the application
            // Log warning and continue - application will work without telemetry
            Console.WriteLine($"[OpenTelemetry] ⚠ WARNING: Failed to initialize OpenTelemetry: {ex.Message}");
            Console.WriteLine($"[OpenTelemetry] ⚠ Application will continue without telemetry export");
            // Don't rethrow - let application continue
        }

        return builder;
    }
}

