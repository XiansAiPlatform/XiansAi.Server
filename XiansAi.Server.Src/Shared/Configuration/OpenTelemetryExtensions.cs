using System.Reflection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Auth;

namespace Features.Shared.Configuration;

/// <summary>
/// OpenTelemetry configuration for XiansAi.Server.
/// Enablement is driven by <c>OpenTelemetry:OtlpEndpoint</c> (if missing/empty, telemetry is disabled).
/// </summary>
public static class OpenTelemetryExtensions
{
    public static WebApplicationBuilder AddOpenTelemetry(this WebApplicationBuilder builder)
    {
        var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint");
        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            Console.WriteLine("[OpenTelemetry] Telemetry is disabled because OpenTelemetry:OtlpEndpoint is not set. Set it to enable traces/metrics export.");
            return builder;
        }

        var serviceName =
            builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName")
            ?? Assembly.GetEntryAssembly()?.GetName().Name
            ?? "XiansAi.Server";

        var serviceVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "1.0.0";
        var logsOtlpEndpoint =
            builder.Configuration.GetValue<string>("OpenTelemetry:LogsOtlpEndpoint")
            ?? otlpEndpoint;

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
                        options.RecordException = true;
                        options.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/health");

                        options.EnrichWithHttpRequest = (activity, httpRequest) =>
                        {
                            activity.SetTag("http.method", httpRequest.Method);
                            activity.SetTag("http.scheme", httpRequest.Scheme);
                            activity.SetTag("http.host", httpRequest.Host.Value);

                            var tenantId = httpRequest.Headers["X-Tenant-Id"].FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(tenantId))
                            {
                                activity.SetTag("tenant.id", tenantId);
                            }
                        };

                        options.EnrichWithHttpResponse = (activity, httpResponse) =>
                        {
                            activity.SetTag("http.status_code", httpResponse.StatusCode);

                            var tenantContext = httpResponse.HttpContext.RequestServices.GetService<ITenantContext>();
                            if (tenantContext != null && !string.IsNullOrWhiteSpace(tenantContext.TenantId))
                            {
                                activity.SetTag("tenant.id", tenantContext.TenantId);
                                activity.SetTag("tenant.user", tenantContext.LoggedInUser);
                                activity.SetTag("tenant.user_type", tenantContext.UserType.ToString());
                                if (tenantContext.UserRoles?.Length > 0)
                                {
                                    activity.SetTag("tenant.user_roles", string.Join(",", tenantContext.UserRoles));
                                }
                            }
                        };
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = (activity, req) =>
                        {
                            activity.SetTag("http.request.method", req.Method.ToString());
                        };
                        options.EnrichWithHttpResponseMessage = (activity, res) =>
                        {
                            activity.SetTag("http.response.status_code", (int)res.StatusCode);
                        };
                    })
                    .AddSource("XiansAi.*")
                    .AddSource("XiansAi.Server.Temporal")
                    .AddSource("MongoDB.Driver.Core")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    }))
                .WithMetrics(metrics => metrics
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddMeter("XiansAi.*")
                    .AddOtlpExporter(options =>
                    {
                        options.Endpoint = new Uri(otlpEndpoint);
                        options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    }));

            // Optional logs export (endpoint-driven). Defaults to OpenTelemetry:OtlpEndpoint if LogsOtlpEndpoint isn't set.
            if (!string.IsNullOrWhiteSpace(logsOtlpEndpoint))
            {
                builder.Logging.AddOpenTelemetry(options =>
                {
                    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(
                        serviceName: serviceName,
                        serviceVersion: serviceVersion,
                        serviceInstanceId: Environment.MachineName));

                    options.IncludeFormattedMessage = true;
                    options.ParseStateValues = true;
                    options.IncludeScopes = true;

                    options.AddOtlpExporter(otlp =>
                    {
                        otlp.Endpoint = new Uri(logsOtlpEndpoint);
                        otlp.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                    });
                });

                Console.WriteLine($"[OpenTelemetry] Logs OTLP Endpoint: {logsOtlpEndpoint}");
            }

            Console.WriteLine($"[OpenTelemetry] ✓ OpenTelemetry fully enabled for {serviceName}");
            Console.WriteLine($"[OpenTelemetry]   - Service: {serviceName} v{serviceVersion}");
            Console.WriteLine($"[OpenTelemetry]   - OTLP Endpoint: {otlpEndpoint}");
            Console.WriteLine("[OpenTelemetry]   - Note: If collector is unreachable, traces/metrics will be buffered or dropped (non-blocking)");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenTelemetry] ⚠ WARNING: Failed to initialize OpenTelemetry: {ex.Message}");
            Console.WriteLine("[OpenTelemetry] ⚠ Application will continue without telemetry export");
        }

        return builder;
    }
}


