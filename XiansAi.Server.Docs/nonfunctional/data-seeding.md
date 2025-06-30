# Data Seeding

The application includes an efficient data seeding system that automatically creates default data during server startup. This ensures the system has the necessary base data to function properly without manual intervention.

## Overview

The data seeding system:

- **Runs once during startup** - No performance impact during normal operation
- **Checks for existence first** - Only creates data if it doesn't already exist
- **Is configurable** - All default values can be customized via configuration
- **Fails gracefully** - Application continues to start even if seeding fails
- **Is extensible** - Easy to add new types of default data

## Architecture

### Components

1. **SeedData.cs** - Main seeding orchestrator
2. **SeedDataSettings.cs** - Configuration model
3. **Program.cs integration** - Startup hook
4. **appsettings.json** - Configuration values

### Startup Flow

```
Application Startup
├── Configure Services
├── Build Application
├── Create Database Indexes
└── Seed Default Data ← New step
    ├── Check if seeding is enabled
    ├── Load configuration settings
    └── Seed each data type (if enabled)
        ├── Check if data already exists
        ├── Create default data if missing
        └── Log results
```

## Performance Characteristics

### Startup Impact

- **Minimal overhead**: Single database query per data type to check existence
- **Efficient queries**: Uses indexed fields (e.g., tenant count query)
- **Fast execution**: Typically completes in < 100ms
- **Non-blocking**: Doesn't prevent application from starting if it fails

### Runtime Impact

- **Zero impact**: Only runs during startup
- **No recurring operations**: Data is created once and persists

## Configuration

### Basic Configuration

Add the `SeedData` section to your `appsettings.json`:

```json
{
  "SeedData": {
    "Enabled": true,
    "CreateDefaultTenant": true,
    "DefaultTenant": {
      "TenantId": "default",
      "Name": "Default Tenant",
      "Domain": "default.xiansai.com",
      "Description": "Default tenant created during system initialization",
      "Theme": "default",
      "Timezone": "UTC",
      "Enabled": true
    }
  }
}
```

### Configuration Options

| Setting | Type | Default | Description |
|---------|------|---------|-------------|
| `Enabled` | bool | `true` | Master switch for all data seeding |
| `CreateDefaultTenant` | bool | `true` | Whether to create a default tenant |
| `DefaultTenant.TenantId` | string | `"default"` | ID for the default tenant |
| `DefaultTenant.Name` | string | `"Default Tenant"` | Display name for the default tenant |
| `DefaultTenant.Domain` | string | `"default.xiansai.com"` | Domain for the default tenant |
| `DefaultTenant.Description` | string | Auto-generated | Description text |
| `DefaultTenant.Theme` | string | `"default"` | UI theme identifier |
| `DefaultTenant.Timezone` | string | `"UTC"` | Default timezone |
| `DefaultTenant.Enabled` | bool | `true` | Whether the tenant is active |

### Environment-Specific Configuration

You can override settings per environment:

**appsettings.Development.json**
```json
{
  "SeedData": {
    "DefaultTenant": {
      "TenantId": "dev-tenant",
      "Name": "Development Tenant",
      "Domain": "dev.xiansai.com"
    }
  }
}
```

**appsettings.Production.json**
```json
{
  "SeedData": {
    "DefaultTenant": {
      "TenantId": "production",
      "Name": "Production Tenant",
      "Domain": "xiansai.com"
    }
  }
}
```

### Disabling Seeding

To completely disable data seeding:

```json
{
  "SeedData": {
    "Enabled": false
  }
}
```

To disable specific types of seeding:

```json
{
  "SeedData": {
    "CreateDefaultTenant": false
  }
}
```

## Current Seeded Data

### Default Tenant

**Purpose**: Ensures at least one tenant exists for the multi-tenant system to function.

**Conditions**: Only created if no tenants exist in the database.

**Default Values**:
- **TenantId**: `"default"`
- **Name**: `"Default Tenant"`
- **Domain**: `"default.xiansai.com"`
- **CreatedBy**: `"system"`
- **Enabled**: `true`

## Extending the Seeding System

### Adding New Data Types

1. **Add configuration settings** to `SeedDataSettings.cs`:

```csharp
public class SeedDataSettings
{
    // ... existing settings ...
    
    public bool CreateDefaultRoles { get; set; } = true;
    public DefaultRoleSettings DefaultRoles { get; set; } = new();
}

public class DefaultRoleSettings
{
    public List<string> AdminRoles { get; set; } = new() { "admin", "super-admin" };
    public List<string> UserRoles { get; set; } = new() { "user", "viewer" };
}
```

2. **Add seeding method** to `SeedData.cs`:

```csharp
private static async Task SeedDefaultRolesAsync(IRoleRepository roleRepository, DefaultRoleSettings roleSettings, ILogger logger)
{
    try
    {
        // Check if roles already exist
        var existingRoles = await roleRepository.GetAllAsync();
        if (existingRoles.Any())
        {
            logger.LogDebug("Roles already exist, skipping default role seeding");
            return;
        }
        
        // Create default roles
        foreach (var roleName in roleSettings.AdminRoles.Concat(roleSettings.UserRoles))
        {
            var role = new Role
            {
                Name = roleName,
                CreatedBy = "system",
                CreatedAt = DateTime.UtcNow
            };
            await roleRepository.CreateAsync(role);
        }
        
        logger.LogInformation("Default roles created successfully");
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Failed to seed default roles");
    }
}
```

3. **Call the method** in `SeedDefaultDataAsync`:

```csharp
// Seed default roles
if (seedSettings.CreateDefaultRoles)
{
    var roleRepository = scope.ServiceProvider.GetRequiredService<IRoleRepository>();
    await SeedDefaultRolesAsync(roleRepository, seedSettings.DefaultRoles, logger);
}
```

### Best Practices for Extensions

1. **Always check for existing data** before creating new data
2. **Use configuration** instead of hardcoded values
3. **Handle exceptions gracefully** - log warnings but don't throw
4. **Add comprehensive logging** for troubleshooting
5. **Follow existing naming conventions** for consistency
6. **Document new configuration options** in this file

## Troubleshooting

### Common Issues

**Seeding not running**
- Check that `SeedData.Enabled` is `true` in configuration
- Look for "Data seeding is disabled" message in logs

**Default tenant not created**
- Check that `SeedData.CreateDefaultTenant` is `true`
- Verify database connectivity
- Check for constraint violations (duplicate tenant ID/domain)

**Configuration not loading**
- Verify JSON syntax in appsettings.json
- Check that configuration section name matches `SeedDataSettings.SectionName`
- Ensure environment-specific config files override correctly

### Logging

The seeding system provides detailed logging:

- **Information**: Normal seeding operations and results
- **Debug**: Skipped operations (data already exists)
- **Warning**: Failed operations (continues execution)
- **Error**: Critical failures (shouldn't occur with proper error handling)

### Database Constraints

Be aware of database constraints when configuring default values:

- **Tenant ID**: Must be unique across all tenants
- **Domain**: Must be unique across all tenants
- **Required fields**: All required model properties must have valid values

## Testing

### Integration Tests

The seeding system is covered by integration tests that verify:

- Seeding creates data when database is empty
- Seeding skips creation when data already exists
- Configuration changes affect seeded data
- Seeding failures don't prevent application startup

### Manual Testing

1. **Clean database**: Drop the database or clear tenant collection
2. **Start application**: Normal startup should create default tenant
3. **Restart application**: Second startup should skip tenant creation
4. **Check logs**: Verify appropriate log messages appear

## Security Considerations

- **System user**: All seeded data is created with `CreatedBy = "system"`
- **Default credentials**: No default user accounts are created (by design)
- **Configuration exposure**: Be careful not to expose sensitive defaults in logs
- **Production data**: Ensure production configuration uses appropriate values

## Future Enhancements

Potential improvements to the seeding system:

- **Migration support**: Track seeding versions and handle updates
- **Conditional seeding**: More complex conditions for when to seed data
- **Bulk operations**: Optimize performance for large seed datasets
- **Rollback support**: Ability to remove seeded data
- **External data sources**: Load seed data from files or external APIs 