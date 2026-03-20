using System.Diagnostics;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Shared.Auth;

namespace Features.Shared.Configuration;

/// <summary>
/// Configures OpenTelemetry tracing and metrics for XiansAi.Server.
/// Enabled via OpenTelemetry:Enabled=true; exports via OTLP gRPC to OpenTelemetry:OtlpEndpoint.
/// </summary>
public static class OpenTelemetryExtensions
{
    public static WebApplicationBuilder AddOpenTelemetry(this WebApplicationBuilder builder)
    {
        var enabled = builder.Configuration.GetValue<bool>("OpenTelemetry:Enabled", false);
        if (!enabled)
        {
            return builder;
        }

        var serviceName = builder.Configuration.GetValue<string>("OpenTelemetry:ServiceName") ?? "XiansAi.Server";
        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        var otlpEndpoint = builder.Configuration.GetValue<string>("OpenTelemetry:OtlpEndpoint");

        // Configurable tenant tag name — OPENTELEMETRY_TENANT_TAG_NAME env var, default: tenant.id
        var tenantTagName =
            (builder.Configuration.GetValue<string>("OpenTelemetry:TenantTagName")
             ?? Environment.GetEnvironmentVariable("OPENTELEMETRY_TENANT_TAG_NAME"))?.Trim()
            is { Length: > 0 } t ? t : "tenant.id";

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            Console.WriteLine("[OpenTelemetry] WARNING: OpenTelemetry:OtlpEndpoint is not set — telemetry export disabled.");
            return builder;
        }

        Console.WriteLine($"[OpenTelemetry] Initializing for service: {serviceName}");
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

                        // Exclude health check endpoints to reduce noise
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health");

                        // Tag incoming requests with tenant context from HTTP headers
                        options.EnrichWithHttpRequest = (activity, httpRequest) =>
                        {
                            activity.SetTag("http.method", httpRequest.Method);
                            activity.SetTag("http.scheme", httpRequest.Scheme);
                            activity.SetTag("http.host", httpRequest.Host.Value);

                            var tenantId = httpRequest.Headers["X-Tenant-Id"].FirstOrDefault();
                            if (!string.IsNullOrEmpty(tenantId))
                            {
                                activity.SetTag(tenantTagName, tenantId);
                            }
                        };

                        // Enrich with authenticated tenant context after auth middleware runs
                        options.EnrichWithHttpResponse = (activity, httpResponse) =>
                        {
                            activity.SetTag("http.status_code", httpResponse.StatusCode);

                            var tenantContext = httpResponse.HttpContext.RequestServices.GetService<ITenantContext>();
                            if (tenantContext != null && !string.IsNullOrEmpty(tenantContext.TenantId))
                            {
                                activity.SetTag(tenantTagName, tenantContext.TenantId);
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

                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            activity.SetTag("http.request.method", request.Method.ToString());

                            var tenantId = request.Headers.TryGetValues("X-Tenant-Id", out var values)
                                ? values.FirstOrDefault()
                                : null;
                            if (!string.IsNullOrEmpty(tenantId))
                            {
                                activity.SetTag(tenantTagName, tenantId);
                            }
                        };

                        options.EnrichWithHttpResponseMessage = (activity, response) =>
                            activity.SetTag("http.response.status_code", (int)response.StatusCode);
                    })
                    .AddSource("XiansAi.*")
                    .AddSource("MongoDB.Driver.Core.Extensions.DiagnosticSources")
                    .AddProcessor(new MongoDbTenantPropagationProcessor(tenantTagName))
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

            // Logs use builder.Logging.AddOpenTelemetry() because IncludeFormattedMessage / ParseStateValues /
            // IncludeScopes are on OpenTelemetryLoggerOptions, not on LoggerProviderBuilder (WithLogging parameter).
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.ParseStateValues = true;
                logging.IncludeScopes = true;
                logging.AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                });
            });

            Console.WriteLine($"[OpenTelemetry] ✓ Enabled for {serviceName} v{serviceVersion} → {otlpEndpoint}");
            Console.WriteLine("[OpenTelemetry]   Note: If collector is unreachable, traces/metrics are buffered or dropped (non-blocking)");
        }
        catch (Exception ex)
        {
            // OTel init failures must never crash the server
            Console.WriteLine($"[OpenTelemetry] ⚠ Failed to initialize: {ex.Message}");
            Console.WriteLine("[OpenTelemetry] ⚠ Server will continue without telemetry export");
        }

        return builder;
    }

    /// <summary>
    /// Copies <c>tenant.id</c> from the nearest ancestor span onto MongoDB spans.
    /// MongoDB operations run inside an HTTP request that already has <c>tenant.id</c>
    /// set by <see cref="AddOpenTelemetry"/> enrichment, but the MongoDB driver creates
    /// child activities without inheriting custom tags.
    /// </summary>
    private sealed class MongoDbTenantPropagationProcessor : BaseProcessor<Activity>
    {
        private const string MongoDbSourceName = "MongoDB.Driver.Core.Extensions.DiagnosticSources";
        private readonly string _tagName;

        public MongoDbTenantPropagationProcessor(string tagName)
        {
            _tagName = tagName;
        }

        public override void OnStart(Activity activity)
        {
            if (activity.Source.Name != MongoDbSourceName)
            {
                return;
            }

            // Walk up the parent chain to find a span that already has the tenant tag.
            var parent = activity.Parent;
            while (parent != null)
            {
                var tenantId = parent.GetTagItem(_tagName) as string;
                if (!string.IsNullOrWhiteSpace(tenantId))
                {
                    activity.SetTag(_tagName, tenantId);
                    return;
                }

                parent = parent.Parent;
            }
        }
    }
}
