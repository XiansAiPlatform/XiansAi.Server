# OpenTelemetry Observability Guide

XiansAi.Server includes built-in OpenTelemetry support for monitoring application performance, tracing requests, and collecting metrics.

## Architecture

### Development Mode
```
XiansAi.Server → OTLP (port 18889) → Aspire Dashboard
                                       └─ UI: localhost:18888
```

### Production Mode
```
XiansAi.Server → OTLP (port 4317) → OTEL Collector
                                      ├─→ Prometheus
                                      ├─→ Jaeger
                                      └─→ Grafana/Custom
```

## Quick Start - Development

### 1. Start with Observability

```bash
cd community-edition

# Start all services including Aspire Dashboard
docker-compose --profile dev up

# Or start without Aspire Dashboard (observability disabled)
docker-compose up
```

### 2. Access Aspire Dashboard

Open your browser to: **http://localhost:18888**

You'll see:
- **Traces**: Request flows through your application
- **Metrics**: HTTP requests, response times, .NET runtime stats
- **Logs**: Application logs (if enabled)

### 3. Generate Some Traffic

```bash
# Make requests to your server
curl http://localhost:5001/health
curl http://localhost:5001/api/...

# Watch the telemetry appear in real-time in Aspire Dashboard!
```

## Configuration

### Enable/Disable Observability

In `server/.env.local`:

```bash
# Enable observability
OpenTelemetry__Enabled=true

# Disable observability
OpenTelemetry__Enabled=false
```

### Development Configuration

```bash
# Service name (appears in telemetry)
OpenTelemetry__ServiceName=XiansAi.Server

# OTLP endpoint (Aspire Dashboard in dev)
OpenTelemetry__OtlpEndpoint=http://aspire-dashboard:18889
```

### Production Configuration

For production, point to an OTEL Collector:

```bash
# Enable observability
OpenTelemetry__Enabled=true

# Service name
OpenTelemetry__ServiceName=XiansAi.Server

# OTLP endpoint (Collector in production)
OpenTelemetry__OtlpEndpoint=http://otel-collector:4317
```

## What Gets Collected

### Traces (Distributed Tracing)
- ✅ HTTP requests (ASP.NET Core)
- ✅ Outgoing HTTP calls (HttpClient)
- ✅ Custom spans (via `XiansAi.*` sources)
- ✅ Exception details
- ✅ Request/response details

### Metrics
- ✅ HTTP request count
- ✅ HTTP request duration
- ✅ HTTP response status codes
- ✅ .NET runtime metrics (GC, memory, threads)
- ✅ HttpClient metrics
- ✅ Custom metrics (via `XiansAi.*` meters)

### Automatic Filtering
- Health check endpoints (`/health`) are excluded from traces

## Docker Compose Profiles

### With Aspire Dashboard (Development)
```bash
docker-compose --profile dev up
```

Starts:
- MongoDB
- XiansAi Server (with OpenTelemetry)
- XiansAi UI
- **Aspire Dashboard** (observability UI)

### Without Aspire Dashboard (Minimal)
```bash
docker-compose up
```

Starts:
- MongoDB
- XiansAi Server (OpenTelemetry disabled or pointing to external collector)
- XiansAi UI

## Production Setup

### Option 1: Azure Application Insights

```bash
OpenTelemetry__Enabled=true
OpenTelemetry__ServiceName=XiansAi.Server
OpenTelemetry__OtlpEndpoint=https://dc.services.visualstudio.com/v2/track
```

### Option 2: OTEL Collector

Create `docker-compose.production.yml`:

```yaml
services:
  otel-collector:
    image: otel/opentelemetry-collector-contrib:latest
    container_name: xians-otel-collector
    command: ["--config=/etc/otel/config.yaml"]
    ports:
      - "4317:4317"   # OTLP gRPC
      - "8889:8889"   # Prometheus metrics
    volumes:
      - ./otel-collector-config.yaml:/etc/otel/config.yaml
    networks:
      - xians-production

  xiansai-server:
    environment:
      - OpenTelemetry__Enabled=true
      - OpenTelemetry__OtlpEndpoint=http://otel-collector:4317
```

Then configure the collector to export to Prometheus, Jaeger, Grafana Cloud, etc.

## Troubleshooting

### No telemetry showing up

1. **Check if OpenTelemetry is enabled**
   ```bash
   # In server/.env.local
   OpenTelemetry__Enabled=true
   ```

2. **Check if Aspire Dashboard is running**
   ```bash
   docker ps | grep aspire-dashboard
   # Should show: xians-aspire-dashboard
   ```

3. **Check server logs**
   ```bash
   docker logs xians-server | grep OpenTelemetry
   # Should see: [OpenTelemetry] OpenTelemetry enabled - exporting to http://aspire-dashboard:18889
   ```

4. **Verify endpoint**
   ```bash
   # Inside server container
   curl http://aspire-dashboard:18889
   ```

### Dashboard not accessible

```bash
# Check if port 18888 is available
curl http://localhost:18888

# Check Aspire Dashboard logs
docker logs xians-aspire-dashboard
```

### Disable observability

```bash
# In server/.env.local
OpenTelemetry__Enabled=false

# Restart server
docker-compose restart xiansai-server
```

## Performance Impact

OpenTelemetry has minimal performance impact:
- **CPU**: < 2% overhead
- **Memory**: ~10-20 MB additional memory
- **Latency**: < 1ms per request
- **Network**: Efficient batching and compression

The benefits of observability far outweigh the small performance cost!

## Advanced Usage

### Custom Spans

```csharp
using System.Diagnostics;

// Create custom activity source
private static readonly ActivitySource MyActivitySource = new("XiansAi.MyFeature");

// Create custom span
using var activity = MyActivitySource.StartActivity("ProcessingData");
activity?.SetTag("user.id", userId);
activity?.SetTag("data.size", dataSize);

// Your code here
ProcessData();

// Span automatically ends when disposed
```

### Custom Metrics

```csharp
using System.Diagnostics.Metrics;

// Create custom meter
private static readonly Meter MyMeter = new("XiansAi.MyFeature");
private static readonly Counter<int> ProcessedItems = MyMeter.CreateCounter<int>("items.processed");

// Record metric
ProcessedItems.Add(1, new KeyValuePair<string, object?>("user.id", userId));
```

## Resources

- [OpenTelemetry Documentation](https://opentelemetry.io/docs/)
- [Aspire Dashboard](https://learn.microsoft.com/en-us/dotnet/aspire/fundamentals/dashboard)
- [.NET OpenTelemetry](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/observability-with-otel)





