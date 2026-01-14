# AdminApi Versioning Guide

## Overview

AdminApi uses URL path versioning (e.g., `/api/v1/admin/...`). The current implementation makes it easy to add new API versions (v2, v3, etc.) without duplicating code or changing hardcoded paths.

## Current Implementation

- **Current Version**: `v1` (defined in `AdminApiConstants.CurrentVersion`)
- **Base Path Pattern**: `/api/{version}/admin`
- **All endpoints** use `AdminApiConstants` helper methods to build versioned paths

## How to Add v2 (or any new version)

### Step 1: Update Configuration

In `AdminApiConfiguration.cs`, uncomment and add the new version mapping:

```csharp
public static WebApplication UseAdminApiEndpoints(this WebApplication app)
{
    // Map v1 endpoints (current version)
    MapAdminApiVersion(app, "v1");
    
    // Add v2 endpoints
    MapAdminApiVersion(app, "v2");
    
    return app;
}
```

### Step 2: Implement v2-Specific Changes (if needed)

If v2 requires different behavior, you have two options:

#### Option A: Version-Specific Logic in Existing Methods

Add version-specific logic within the existing endpoint mapping methods:

```csharp
public static void MapAdminTenantEndpoints(this WebApplication app, string version = AdminApiConstants.CurrentVersion)
{
    var adminTenantGroup = app.MapGroup(AdminApiConstants.GetVersionedPath("tenants", version))
        .WithTags("AdminAPI - Tenant Management");

    // v1 and v2 share the same implementation
    if (version == "v1" || version == "v2")
    {
        adminTenantGroup.MapGet("", async (...) => { /* existing code */ });
    }
    
    // v2-specific endpoint
    if (version == "v2")
    {
        adminTenantGroup.MapGet("/enhanced", async (...) => { /* v2-only feature */ });
    }
}
```

#### Option B: Create Separate v2 Endpoint Classes

For major changes, create version-specific endpoint classes:

```csharp
// Features/AdminApi/Endpoints/V2/AdminTenantEndpointsV2.cs
public static class AdminTenantEndpointsV2
{
    public static void MapAdminTenantEndpointsV2(this WebApplication app)
    {
        var adminTenantGroup = app.MapGroup(AdminApiConstants.GetVersionedPath("tenants", "v2"))
            .WithTags("AdminAPI - Tenant Management (v2)");
        
        // v2-specific implementations
    }
}
```

Then in `AdminApiConfiguration.cs`:

```csharp
private static void MapAdminApiVersion(WebApplication app, string version)
{
    if (version == "v1")
    {
        AdminTenantEndpoints.MapAdminTenantEndpoints(app, version);
    }
    else if (version == "v2")
    {
        AdminTenantEndpointsV2.MapAdminTenantEndpointsV2(app);
    }
    // ... other endpoints
}
```

### Step 3: Update Documentation

1. Update OpenAPI YAML files to include v2 endpoints
2. Update `admin-api-tests.http` with v2 test cases
3. Update API documentation to explain differences between versions

### Step 4: Update Constants (if changing default)

If you want to make v2 the default version:

```csharp
public static class AdminApiConstants
{
    public const string CurrentVersion = "v2";  // Changed from "v1"
    // ...
}
```

**Note**: This will affect all default parameter values. Consider keeping v1 as default for backward compatibility.

## Best Practices

1. **Backward Compatibility**: Keep v1 endpoints working when adding v2
2. **Deprecation**: Use OpenAPI `deprecated: true` for older versions
3. **Versioning Strategy**: 
   - Use v2 for breaking changes
   - Use v1 for backward-compatible additions
4. **Testing**: Test both versions when making changes
5. **Documentation**: Clearly document differences between versions

## Example: Adding a New Endpoint in v2

```csharp
public static void MapAdminTenantEndpoints(this WebApplication app, string version = AdminApiConstants.CurrentVersion)
{
    var adminTenantGroup = app.MapGroup(AdminApiConstants.GetVersionedPath("tenants", version))
        .WithTags("AdminAPI - Tenant Management");

    // Existing v1 endpoint (works for both v1 and v2)
    adminTenantGroup.MapGet("", async (...) => { /* existing code */ });
    
    // New v2-only endpoint
    if (version == "v2")
    {
        adminTenantGroup.MapGet("/analytics", async (...) => 
        {
            // v2-specific analytics endpoint
        });
    }
}
```

## File Structure

```
Features/AdminApi/
├── Constants/
│   └── AdminApiConstants.cs          # Version constants and helpers
├── Configuration/
│   └── AdminApiConfiguration.cs      # Version mapping logic
├── Endpoints/
│   ├── AdminTenantEndpoints.cs       # v1 (and v2 if compatible)
│   ├── AdminAgentEndpoints.cs
│   ├── AdminTemplateEndpoints.cs
│   ├── AdminKnowledgeEndpoints.cs
│   ├── AdminReportingUsersEndpoints.cs
│   ├── AdminTokenEndpoints.cs
│   ├── AdminOwnershipEndpoints.cs
│   └── V2/                           # Optional: v2-specific implementations
│       └── AdminTenantEndpointsV2.cs
└── API_VERSIONING_GUIDE.md            # This file
```

## Summary

Adding a new API version is now straightforward:
1. Add one line in `AdminApiConfiguration.cs` to map the new version
2. Optionally add version-specific logic in endpoint methods
3. Update documentation

All path construction is handled by `AdminApiConstants`, so there are no hardcoded version strings to update.


