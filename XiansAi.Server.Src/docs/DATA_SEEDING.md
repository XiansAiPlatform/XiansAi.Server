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

```text
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
      "Enabled": true,
      "TokenUsage": {
        "Enabled": true,
        "MaxTokens": 200000,
        "WindowSeconds": 86400
      }
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

### Default Token Usage Limits

When `SeedData.DefaultTenant.TokenUsage.Enabled` is true, the seeder will create a tenant-level token usage limit using the configured `MaxTokens` and `WindowSeconds` values. This ensures the default tenant immediately participates in the token limiting feature without manual setup.

If you prefer to skip seeding usage limits (e.g., because you manage them manually), set `SeedData.DefaultTenant.TokenUsage.Enabled` to `false`.
