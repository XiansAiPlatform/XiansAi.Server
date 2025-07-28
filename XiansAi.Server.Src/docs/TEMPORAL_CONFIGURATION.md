# Temporal Configuration Guide

This document provides a comprehensive guide for configuring Temporal workflow connections in the XiansAi Server. The system supports both default and tenant-specific configurations, with proper connection management and security best practices.

## Architecture Overview

The Temporal integration uses a sophisticated connection management system:

- **Singleton Service Pattern** - Efficient connection pooling and reuse
- **Factory Pattern** - Tenant-aware client resolution
- **Multi-Tenant Support** - Isolated workflow execution per tenant
- **Connection Caching** - Per-tenant client caching with proper disposal
- **Async Operations** - Non-blocking workflow operations

The system implements `ITemporalClientService` as a singleton with `ITemporalClientFactory` providing scoped tenant context resolution.

## Core Configuration

### Default Temporal Configuration

The base configuration that applies to all tenants unless overridden:

```bash
# Default Temporal server configuration
Temporal__FlowServerUrl=localhost:7233
Temporal__FlowServerNamespace=default

# Optional: Default certificates for mTLS (if required)
Temporal__CertificateBase64=LS0tLS1CRUdJTi...
Temporal__PrivateKeyBase64=LS0tLS1CRUdJTi...

# Cloud API Key for namespace management (optional)
Temporal__CloudApiKey=your-temporal-cloud-api-key
```

### Configuration Priority

The system follows a fallback hierarchy:

1. **Tenant-specific configuration** (highest priority)
2. **Default configuration** (fallback)
3. **Error if neither found**

```bash
# Tenant-specific configuration overrides default
Tenants__your-tenant-id__Temporal__FlowServerUrl=custom-tenant-server:7233
Tenants__your-tenant-id__Temporal__FlowServerNamespace=custom-namespace
```

## Environment-Specific Configurations

### Development Configuration

For local development and testing:

```bash
# Simple local Temporal server
Temporal__FlowServerUrl=localhost:7233
Temporal__FlowServerNamespace=default

# No certificates required for local development
# Temporal__CertificateBase64=  # Optional
# Temporal__PrivateKeyBase64=  # Optional
```

### Production Configuration

For production deployment with Temporal Cloud:

```bash
# Temporal Cloud configuration
Temporal__FlowServerUrl=your-namespace.xyz.tmprl.cloud:7233
Temporal__FlowServerNamespace=your-namespace.xyz

# Required mTLS certificates for Temporal Cloud
Temporal__CertificateBase64=LS0tLS1CRUdJTiBDRVJUSUZJQ0FURS0tLS0tCk1JSUJ5akNDQVZDZ0F3SUJBZ0lSQVNhSlYyS25CQklnSXJRRTlkeFFndnd3Q2dZSUtvWkl6ajBFQXdNd0V6RVIKTUE4R0ExVUVDaE1JZEdWdGNHOXlZV3d3SGhjTk1qVXdOVEkxTURnek1EQTJXaGNOTWpZd05USTFNRGd6TVRBMgpXakFUTVJFd0R3WURWUVFLRXdoMFpXMXdiM0poYkRCMk1CQUdCeXFHU000OUFnRUdCU3VCQkFBaUEySUFCRGJmCkJ3R0tsN0RFV3ZTVXh5bkowd25ISG9qQzFxWHMrVmtGWkkwV3MzRnd1UFhXL3ZBZmE5RHhlT2JyOGc4bThxNncKcXdsZFNFZnQwNFI4VnJmK0RCckxGT1A0TFpOMG5wRm9xNkE5ejBQRGYyeUpIQmZIWHRxeFpxMnBEZWFLTUtObwpNR1l3RGdZRFZSMFBBUUgvQkFRREFnR0dNQThHQTFVZEV3RUIvd1FGTUFNQkFmOHdIUVlEVlIwT0JCWUVGRlJGCnUvblR2bkgrakJwUmZTOThhZzBJSHowZk1DUUdBMVVkRVFRZE1CdUNHV05zYVdWdWRDNXliMjkwTG5SbGJYQnYKY21Gc0xtZGhOMVV3Q2dZSUtvWkl6ajBFQXdNRGFBQXdaUUl4QUt2K3luaFJaUFlsZU5HSlVZR2krZGszaW50cAo3SlJhMjJZR3pzdldGTHpWZ1FWd2lwMUxTMmhkUXVJaytnajlCZ0l3WVNRcUFUZlBrUjZxTXluaEZZN0VzWnF2ClJUK1BqZUZSNC9WcGJuTGg2bGpId20vTFNiZUk2RHByUmNmNkZHMDEKLS0tLS1FTkQgQ0VSVElGSUNBVEUtLS0tLQo=
Temporal__PrivateKeyBase64=LS0tLS1CRUdJTiBQUklWQVRFIEtFWS0tLS0tCk1JRzJBZ0VBTUJBR0J5cUdTTTQ5QWdFR0JTdUJCQUFpQklHZU1JR2JBZ0VCQkREdUlCMzNSVzNMUGFDeWlEWjIKRXR5TEwreUZxSnhsT2lsNFJSUU45N2ZLWDR3L2orT3dhMUFoS3o3ZnJFM3JiRE9oWkFOaUFBUTIzd2NCaXBldwp4RnIwbE1jcHlkTUp4eDZJd3RhbDdQbFpCV1NORnJOeGNMajExdjd3SDJ2UThYam02L0lQSnZLdXNLc0pYVWhICjdkT0VmRmEzL2d3YXl4VGorQzJUZEo2UmFLdWdQYzlEdzM5c2lSd1h4MTdhc1dhdHFRM21pakE9Ci0tLS0tRU5EIFBSSVZBVEUgS0VZLS0tLS0K

# Cloud API key for namespace management
Temporal__CloudApiKey=your-temporal-cloud-api-key
```

## Multi-Tenant Configuration

### Tenant-Specific Overrides

Each tenant can have its own Temporal configuration:

```bash
# Tenant A configuration
Tenants__tenant-a__Temporal__FlowServerUrl=tenant-a.xyz.tmprl.cloud:7233
Tenants__tenant-a__Temporal__FlowServerNamespace=tenant-a.xyz
Tenants__tenant-a__Temporal__CertificateBase64=LS0tLS1CRUdJTi...
Tenants__tenant-a__Temporal__PrivateKeyBase64=LS0tLS1CRUdJTi...

# Tenant B configuration  
Tenants__tenant-b__Temporal__FlowServerUrl=tenant-b.xyz.tmprl.cloud:7233
Tenants__tenant-b__Temporal__FlowServerNamespace=tenant-b.xyz
Tenants__tenant-b__Temporal__CertificateBase64=LS0tLS1CRUdJTi...
Tenants__tenant-b__Temporal__PrivateKeyBase64=LS0tLS1CRUdJTi...
```

### Configuration Fallback Logic

```csharp
// 1. Try tenant-specific configuration first
Tenants:{tenantId}:Temporal

// 2. Fallback to default configuration
Temporal

// 3. Throw error if neither found
throw new InvalidOperationException($"Temporal configuration for tenant {tenantId} not found");
```

## Connection Management

### Singleton Service Pattern

The system uses a singleton `TemporalClientService` for optimal connection management:

```csharp
// Registered as singleton for connection reuse
services.AddSingleton<ITemporalClientService, TemporalClientService>();

// Factory provides tenant context
services.AddScoped<ITemporalClientFactory, TemporalClientFactory>();
```

### Connection Caching

- **Per-tenant client caching** - Each tenant gets its own cached connection
- **Thread-safe creation** - Semaphore-protected double-check pattern
- **Proper disposal** - Automatic cleanup of cached connections
- **Resource management** - Implements IDisposable for clean shutdown

### Async Operations

All Temporal operations support async patterns:

```csharp
// Preferred async usage
var client = await _clientFactory.GetClientAsync();

// Backward compatible sync usage
var client = _clientFactory.GetClient();
```

## Security Configuration

### mTLS Authentication

For Temporal Cloud, mTLS certificates are required:

```bash
# Base64-encoded certificate and private key
Temporal__CertificateBase64=LS0tLS1CRUdJTi...
Temporal__PrivateKeyBase64=LS0tLS1CRUdJTi...
```

**Certificate Management:**

1. Generate certificate through Temporal Cloud UI
2. Download certificate and private key files
3. Base64 encode both files
4. Store in secure configuration (Azure Key Vault, etc.)

### API Key Management

For namespace management operations:

```bash
# Cloud API key for administrative operations
Temporal__CloudApiKey=your-temporal-cloud-api-key
```

**Security Best Practices:**

1. **Rotate certificates regularly** - Follow your organization's certificate lifecycle policy
2. **Use secure storage** - Store certificates in Azure Key Vault or similar
3. **Limit API key permissions** - Use least-privilege principle
4. **Monitor access** - Log and monitor Temporal connections
5. **Environment separation** - Use different namespaces for dev/staging/prod

## Advanced Configuration

### Connection Pool Settings

While Temporal clients are cached per tenant, you can configure underlying connection behavior:

```bash
# Connection timeout settings (if supported by Temporal client)
Temporal__ConnectionTimeout=30000  # 30 seconds
Temporal__OperationTimeout=60000   # 60 seconds
```

### Workflow Options

Configure default workflow behaviors:

```bash
# Default workflow execution timeout
Temporal__DefaultWorkflowTimeout=3600  # 1 hour

# Default task queue configuration
Temporal__DefaultTaskQueue=default
```

### Monitoring and Observability

```bash
# Enable detailed logging for Temporal operations
Logging__LogLevel__Temporalio=Information

# Application Insights correlation
ApplicationInsights__EnableTemporalTracking=true
```

## Configuration Validation

The system validates configuration at startup:

### Required Fields

- **FlowServerUrl** - Always required
- **FlowServerNamespace** - Required for proper workflow isolation
- **Certificates** - Required for Temporal Cloud (mTLS)

### Validation Errors

Common configuration errors and solutions:

```bash
# Error: Temporal configuration for tenant {tenantId} not found
# Solution: Add either tenant-specific or default configuration

# Error: FlowServerUrl is required for tenant {tenantId}
# Solution: Ensure FlowServerUrl is set in configuration

# Error: Certificate is not set in the configuration
# Solution: Add CertificateBase64 and PrivateKeyBase64 for Temporal Cloud
```

## Troubleshooting

### Common Issues

1. **Connection Refused**
   ```
   Problem: Cannot connect to Temporal server
   Solution: Verify FlowServerUrl and network connectivity
   ```

2. **Certificate Authentication Failed**
   ```
   Problem: mTLS authentication failed
   Solution: Verify certificate format and expiration
   ```

3. **Namespace Not Found**
   ```
   Problem: Workflow execution fails with namespace error
   Solution: Verify FlowServerNamespace configuration
   ```

4. **Tenant Configuration Missing**
   ```
   Problem: No configuration found for specific tenant
   Solution: Add tenant-specific config or ensure default config exists
   ```

### Diagnostic Commands

```bash
# Test Temporal connectivity
tctl --namespace your-namespace workflow list

# Verify certificate
openssl x509 -in certificate.pem -text -noout

# Check namespace access
tctl --namespace your-namespace namespace describe
```

### Logging Configuration

Enable detailed logging for troubleshooting:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Temporalio": "Debug",
      "Shared.Utils.Temporal": "Debug"
    }
  }
}
```

## Migration and Maintenance

### Updating Certificates

1. Generate new certificates in Temporal Cloud
2. Update configuration with new Base64-encoded values
3. Deploy configuration update
4. Verify connections are working
5. Remove old certificates from Temporal Cloud

### Namespace Migration

To migrate workflows to a new namespace:

1. Create new namespace in Temporal Cloud
2. Update configuration with new namespace
3. Deploy application update
4. Migrate workflow state if necessary
5. Decommission old namespace

### Multi-Region Setup

For high availability across regions:

```bash
# Primary region
Temporal__FlowServerUrl=primary.xyz.tmprl.cloud:7233

# Failover configuration (application-level)
Temporal__Failover__Enabled=true
Temporal__Failover__SecondaryUrl=secondary.xyz.tmprl.cloud:7233
```

## Performance Optimization

### Connection Reuse

The singleton pattern ensures optimal connection reuse:

- **Per-tenant caching** - Connections cached by tenant ID
- **Thread-safe access** - Concurrent request support
- **Automatic cleanup** - Proper resource disposal

### Best Practices

1. **Use async methods** - Prefer `GetClientAsync()` over `GetClient()`
2. **Minimize client creation** - Let the caching handle connection management
3. **Monitor connection health** - Implement health checks for Temporal connectivity
4. **Configure timeouts appropriately** - Balance responsiveness vs. reliability

## Configuration Examples

### Complete Development Setup

```bash
# .env.development
ASPNETCORE_ENVIRONMENT=Development
Temporal__FlowServerUrl=localhost:7233
Temporal__FlowServerNamespace=development
# No certificates needed for local Temporal server
```

### Complete Production Setup

```bash
# .env.production
ASPNETCORE_ENVIRONMENT=Production
Temporal__FlowServerUrl=xiansai-prod.ozqzb.tmprl.cloud:7233
Temporal__FlowServerNamespace=xiansai-prod.ozqzb
Temporal__CertificateBase64=LS0tLS1CRUdJTi...
Temporal__PrivateKeyBase64=LS0tLS1CRUdJTi...
Temporal__CloudApiKey=your-cloud-api-key

# Tenant-specific overrides if needed
Tenants__special-tenant__Temporal__FlowServerNamespace=special-tenant.ozqzb
```

### Multi-Tenant Production Setup

```bash
# Default configuration for most tenants
Temporal__FlowServerUrl=shared.xyz.tmprl.cloud:7233
Temporal__FlowServerNamespace=shared.xyz
Temporal__CertificateBase64=LS0tLS1CRUdJTi...
Temporal__PrivateKeyBase64=LS0tLS1CRUdJTi...

# Enterprise tenant with dedicated namespace
Tenants__enterprise-client__Temporal__FlowServerUrl=enterprise.xyz.tmprl.cloud:7233
Tenants__enterprise-client__Temporal__FlowServerNamespace=enterprise.xyz
Tenants__enterprise-client__Temporal__CertificateBase64=LS0tLS1CRUdJTi...
Tenants__enterprise-client__Temporal__PrivateKeyBase64=LS0tLS1CRUdJTi...
```

This configuration system provides the flexibility to support various deployment scenarios while maintaining security and performance best practices. 