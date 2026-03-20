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
    /// <summary>
    /// Resolves the span/log attribute name used for tenant identity.
    /// Checks <c>OpenTelemetry:TenantTagName</c> in configuration first, then the
    /// <c>OPENTELEMETRY_TENANT_TAG_NAME</c> environment variable; falls back to <c>tenant.id</c>.
    /// </summary>
    public static string ResolveTenantTagName(IConfiguration configuration) =>
        (configuration.GetValue<string>("OpenTelemetry:TenantTagName")
         ?? Environment.GetEnvironmentVariable("OPENTELEMETRY_TENANT_TAG_NAME"))?.Trim()
        is { Length: > 0 } t ? t : "tenant.id";

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
        var tenantTagName = ResolveTenantTagName(builder.Configuration);

        // Off by default — user identity fields (user, type, roles) are PII and must be
        // explicitly opted in. Set OpenTelemetry:IncludeUserIdentity=true only in trusted,
        // internal-only observability environments.
        var includeUserIdentity = builder.Configuration.GetValue<bool>("OpenTelemetry:IncludeUserIdentity", false);

        // Bootstrap logger — uses ILogger so messages are structured records (not raw Console output),
        // appear correctly in containerised log collectors, and respect the JSON console formatter.
        using var startupLoggerFactory = LoggerFactory.Create(lb =>
            lb.SetMinimumLevel(LogLevel.Information).AddSimpleConsole());
        var logger = startupLoggerFactory.CreateLogger(nameof(OpenTelemetryExtensions));

        if (string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            logger.LogWarning("OpenTelemetry:OtlpEndpoint is not set — telemetry export disabled.");
            return builder;
        }

        logger.LogInformation("Initializing OpenTelemetry for service: {ServiceName}", serviceName);
        logger.LogInformation("OTLP Endpoint: {OtlpEndpoint}", otlpEndpoint);
        if (includeUserIdentity)
        {
            logger.LogWarning("OpenTelemetry:IncludeUserIdentity is enabled — user identity PII (user, type, roles) will be emitted as span attributes.");
        }

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

                                // PII guard — user identity fields are opt-in only.
                                // Enable via OpenTelemetry:IncludeUserIdentity=true in trusted environments.
                                if (includeUserIdentity)
                                {
                                    activity.SetTag("tenant.user", tenantContext.LoggedInUser);
                                    activity.SetTag("tenant.user_type", tenantContext.UserType.ToString());

                                    if (tenantContext.UserRoles?.Length > 0)
                                    {
                                        activity.SetTag("tenant.user_roles", string.Join(",", tenantContext.UserRoles));
                                    }
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

            logger.LogInformation(
                "OpenTelemetry enabled for {ServiceName} v{ServiceVersion} → {OtlpEndpoint}. " +
                "If the collector is unreachable, traces/metrics are buffered or dropped (non-blocking).",
                serviceName, serviceVersion, otlpEndpoint);
        }
        catch (Exception ex)
        {
            // OTel init failures must never crash the server
            logger.LogError(ex, "Failed to initialize OpenTelemetry — server will continue without telemetry export.");
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
