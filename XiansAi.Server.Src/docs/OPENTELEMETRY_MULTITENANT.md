# Multi-Tenant OpenTelemetry Configuration

XiansAi Server is a multi-tenant platform, and the OpenTelemetry implementation has been designed to provide **tenant-aware observability**. This means all telemetry data (traces, metrics, logs) includes tenant context, allowing you to monitor and troubleshoot each tenant independently.

## Tenant Context in Telemetry

### Automatic Tenant Tagging

Every trace and metric automatically includes the following tenant-related tags:

| Tag | Description | Example |
|-----|-------------|---------|
| `tenant.id` | The tenant identifier | `acme-corp` |
| `tenant.user` | The authenticated user making the request | `john.doe@acme.com` |
| `tenant.user_type` | Type of authentication used | `UserToken`, `AgentApiKey`, `DevToken` |
| `tenant.user_roles` | Comma-separated list of roles | `TenantAdmin,User` |

### How Tenant Context is Captured

The OpenTelemetry instrumentation captures tenant context at multiple stages:

1. **From HTTP Headers** (`X-Tenant-Id`):
   - Captured early in the request pipeline
   - Available even before authentication completes

2. **From TenantContext** (After Authentication):
   - Captured after authentication middleware runs
   - Includes full user information and roles
   - Most reliable and complete tenant data

3. **Propagated to Outgoing Requests**:
   - Tenant context flows to downstream services
   - Maintains trace continuity across tenant-specific workflows

## Filtering Telemetry by Tenant

### In Aspire Dashboard

When viewing telemetry in Aspire Dashboard, you can filter by tenant:

1. **Traces View**:
   - Filter by `tenant.id` tag
   - Group by tenant to see per-tenant performance
   - Click on a trace to see full tenant context

2. **Metrics View**:
   - All HTTP metrics include tenant dimensions
   - Create tenant-specific dashboards
   - Compare performance across tenants

### Example Queries

**View all traces for a specific tenant:**
```
tenant.id = "acme-corp"
```

**View errors for a tenant:**
```
tenant.id = "acme-corp" AND http.status_code >= 400
```

**View traces by user:**
```
tenant.id = "acme-corp" AND tenant.user = "john.doe@acme.com"
```

**View agent activity:**
```
tenant.user_type = "AgentApiKey"
```

## Multi-Tenant Scenarios

### Scenario 1: Tenant-Specific Performance Issues

**Problem**: Tenant "acme-corp" reports slow response times.

**Solution**:
1. Filter traces by `tenant.id = "acme-corp"`
2. Sort by duration to find slowest operations
3. Drill into specific traces to identify bottlenecks
4. Check if issue is tenant-specific or platform-wide

### Scenario 2: Agent Debugging

**Problem**: An agent for tenant "widget-co" is failing.

**Solution**:
1. Filter by `tenant.id = "widget-co"` AND `tenant.user_type = "AgentApiKey"`
2. Look for traces with exceptions
3. Identify which agent (via certificate CN in logs)
4. Trace the full workflow execution

### Scenario 3: User Behavior Analysis

**Problem**: Need to understand how a specific user interacts with the platform.

**Solution**:
1. Filter by `tenant.user = "jane.smith@widget.com"`
2. View all their API calls and workflows
3. Analyze access patterns and feature usage
4. Identify performance issues specific to their usage

### Scenario 4: Cross-Tenant Comparison

**Problem**: Want to compare performance across all tenants.

**Solution**:
1. Group metrics by `tenant.id`
2. Compare request rates, error rates, latencies
3. Identify tenants with unusual patterns
4. Proactively address issues before they escalate

## Tenant Isolation in Production

### OTEL Collector Configuration

For production deployments with an OTEL Collector, you can route tenant data to separate backends:

```yaml
# otel-collector-config.yaml
processors:
  # Route traces by tenant
  routing:
    from_attribute: tenant.id
    table:
      - value: premium-tenant-1
        exporters: [otlp/premium]
      - value: premium-tenant-2
        exporters: [otlp/premium]
    default_exporters: [otlp/standard]

exporters:
  # Premium tenants get better retention
  otlp/premium:
    endpoint: tempo-premium:4317
    
  # Standard tenants
  otlp/standard:
    endpoint: tempo-standard:4317

service:
  pipelines:
    traces:
      receivers: [otlp]
      processors: [routing, batch]
      exporters: [otlp/premium, otlp/standard]
```

### Tenant-Specific Sampling

Sample different tenants at different rates:

```yaml
processors:
  # Sample based on tenant
  tail_sampling:
    policies:
      # Keep all errors
      - name: errors
        type: status_code
        status_code: {status_codes: [ERROR]}
      
      # Premium tenants: 100% sampling
      - name: premium-tenants
        type: string_attribute
        string_attribute: 
          key: tenant.id
          values: [premium-tenant-1, premium-tenant-2]
          enabled_regex_matching: true
          cache_max_size: 100
        
      # Standard tenants: 10% sampling
      - name: standard-tenants
        type: probabilistic
        probabilistic: {sampling_percentage: 10}
```

## Tenant Context in Custom Code

### Adding Tenant Tags to Custom Activities

```csharp
using System.Diagnostics;
using Shared.Auth;

public class MyService
{
    private static readonly ActivitySource ActivitySource = new("XiansAi.MyService");
    private readonly ITenantContext _tenantContext;
    
    public MyService(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }
    
    public async Task ProcessDataAsync()
    {
        using var activity = ActivitySource.StartActivity("ProcessData");
        
        // Tenant context is automatically added by instrumentation,
        // but you can add custom tenant-specific tags
        activity?.SetTag("tenant.id", _tenantContext.TenantId);
        activity?.SetTag("tenant.operation", "data-processing");
        activity?.SetTag("tenant.data_size", dataSize);
        
        // Your processing logic
        await DoWorkAsync();
    }
}
```

### Adding Tenant Dimensions to Custom Metrics

```csharp
using System.Diagnostics.Metrics;
using Shared.Auth;

public class MyService
{
    private static readonly Meter Meter = new("XiansAi.MyService");
    private static readonly Counter<long> ProcessedItems = 
        Meter.CreateCounter<long>("items.processed", "items", "Number of items processed");
    
    private readonly ITenantContext _tenantContext;
    
    public void RecordProcessedItem()
    {
        // Metrics automatically include tenant tags
        ProcessedItems.Add(1, 
            new KeyValuePair<string, object?>("tenant.id", _tenantContext.TenantId),
            new KeyValuePair<string, object?>("item.type", "document"));
    }
}
```

## Best Practices

### 1. Always Use TenantContext

```csharp
// ✅ Good: Use injected TenantContext
public class MyService
{
    private readonly ITenantContext _tenantContext;
    
    public MyService(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }
    
    public void DoWork()
    {
        var tenantId = _tenantContext.TenantId;
        // Automatically captured in telemetry
    }
}

// ❌ Bad: Parsing tenant ID from other sources
public void DoWork(HttpRequest request)
{
    var tenantId = request.Headers["X-Tenant-Id"]; // Don't do this
}
```

### 2. Include Tenant Context in Logs

```csharp
// Structured logging with tenant context
_logger.LogInformation(
    "Processing request for tenant {TenantId} by user {UserId}",
    _tenantContext.TenantId,
    _tenantContext.LoggedInUser);
```

### 3. Tag Custom Operations

For long-running or background operations, explicitly tag with tenant:

```csharp
using var activity = ActivitySource.StartActivity("BackgroundJob");
activity?.SetTag("tenant.id", tenantId);
activity?.SetTag("job.type", "data-sync");
```

### 4. Monitor Cross-Tenant Operations

If an operation touches multiple tenants (e.g., SysAdmin actions):

```csharp
activity?.SetTag("tenant.id", _tenantContext.TenantId); // Current tenant
activity?.SetTag("tenant.target_ids", string.Join(",", targetTenantIds)); // Affected tenants
activity?.SetTag("tenant.is_cross_tenant", "true");
```

## Security Considerations

### Tenant Data Isolation

- Telemetry data includes tenant IDs but **not sensitive tenant data**
- User IDs are included but **not passwords or tokens**
- Ensure your observability backend has proper access controls

### PII in Telemetry

Be careful not to log PII in custom tags:

```csharp
// ❌ Bad: Don't include PII
activity?.SetTag("user.email", email);
activity?.SetTag("user.phone", phoneNumber);

// ✅ Good: Use IDs only
activity?.SetTag("user.id", userId);
activity?.SetTag("tenant.id", tenantId);
```

### Access Control

In production, ensure:
- Tenant admins can only view their tenant's telemetry
- Use separate Aspire Dashboard instances per tenant (for strict isolation)
- Or use Grafana with tenant-based access control

## Troubleshooting

### Tenant ID Not Appearing in Traces

**Symptoms**: Traces don't have `tenant.id` tag

**Causes**:
1. Request not authenticated (no TenantContext set)
2. Request to public endpoint (no tenant required)
3. Background job without tenant context

**Solutions**:
1. Check if endpoint requires authentication
2. Verify `X-Tenant-Id` header is present
3. For background jobs, explicitly set tenant context

### Inconsistent Tenant Tags

**Symptoms**: Some spans have tenant tags, others don't

**Cause**: Tenant context set late in request pipeline

**Solution**: The enrichment happens in `EnrichWithHttpResponse`, which runs after authentication. This is expected and correct.

### Missing Tenant in Outgoing HTTP Calls

**Symptoms**: Downstream service calls don't have tenant context

**Cause**: `X-Tenant-Id` header not propagated

**Solution**: Ensure your HTTP client includes the tenant header:

```csharp
var client = _httpClientFactory.CreateClient();
client.DefaultRequestHeaders.Add("X-Tenant-Id", _tenantContext.TenantId);
```

## Resources

- [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/)
- [Aspire Dashboard Filtering](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard/explore)
- [Multi-Tenant Observability Patterns](https://opentelemetry.io/docs/patterns/multi-tenancy/)





