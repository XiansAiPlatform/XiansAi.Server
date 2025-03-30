# Authorization Design

## Overview

This document outlines the authorization design for the XiansAi.Server application, which uses MongoDB as its backend database. The authorization system is designed to support a multi-tenant architecture with hierarchical permission levels including system administrators, tenant administrators, and tenant users, with granular permission control for entities within each tenant.

## Authorization Hierarchy

1. **System Administrators**
   - Have full access to all tenants and their resources
   - Can manage tenant configurations and system-wide settings
   - Can create, delete, and manage tenant administrator accounts

2. **Tenant Administrators**
   - Have full access to resources within their assigned tenant(s)
   - Can manage users within their tenant
   - Can view and manage all entities within their tenant
   - Can assign permissions to tenant users

3. **Tenant Users**
   - Have access to resources based on explicit permissions
   - Can create entities and become owners of those entities
   - Can share entities with other users with different permission levels

Users tenant access is set in the JWT token from Auth Server.

Token example:

```json
{
  "sub": "user123",
  "name": "John Doe",
  "email": "john@example.com",
  "tenant_access": [
    {
      "tenant_name": "Tenant 1",
      "tenant_id": "tenant1",
      "roles": ["admin", "user"]
    },
    {
      "tenant_name": "Tenant 2",
      "tenant_id": "tenant2",
      "roles": ["user"]
    },
    {
      "tenant_id": "-ROOT-",
      "roles": ["admin"]
    }
  ],
  "iat": 1516239022,
  "exp": 1516242622
}
```

System admins carry the `-ROOT-` tenant id in their JWT token.

Tenant admins carry the tenant id they are assigned to in their JWT token.

Tenant users carry the tenant id they are assigned to in their JWT token.

## Entity Permission Levels

For each entity, users can have one of the following permission levels:

1. **Owner**
   - Full rights to the entity, including deletion
   - Can share the entity with other users and assign permissions
   - Can transfer ownership to another user

2. **Editor**
   - Can edit, run, and view the entity
   - Cannot delete the entity or modify permissions

3. **Reader**
   - View-only access to the entity
   - Cannot make changes or run the entity

## MongoDB Authorization Model

### Document Structure

#### User Document

```json
{
  "_id": "user_id",
  "email": "user@example.com",
  "auth0Id": "auth0|user_identifier",
  "isSystemAdmin": false,
  "tenantMemberships": [
    {
      "tenantId": "tenant_id_1",
      "role": "TenantAdmin"
    },
    {
      "tenantId": "tenant_id_2",
      "role": "TenantUser"
    }
  ]
}
```

#### Tenant Document

```json
{
  "_id": "tenant_id",
  "name": "Tenant Name",
  "settings": {
    "allowedDomains": ["example.com"],
    "defaultUserRole": "TenantUser"
  }
}
```

#### Entity Document (Flow, Activity, etc.)

```json
{
  "_id": "entity_id",
  "name": "Entity Name",
  "tenantId": "tenant_id",
  "createdBy": "user_id",
  "createdAt": "2023-01-01T12:00:00Z",
  "lastModifiedBy": "user_id",
  "lastModifiedAt": "2023-01-02T12:00:00Z"
}
```

#### Permission Document

```json
{
  "_id": "permission_id",
  "entityId": "entity_id",
  "entityType": "Flow", // or "Activity", etc.
  "tenantId": "tenant_id",
  "permissions": [
    {
      "userId": "user_id_1",
      "level": "Owner"
    },
    {
      "userId": "user_id_2",
      "level": "Editor"
    },
    {
      "userId": "user_id_3",
      "level": "Reader"
    }
  ]
}
```

### MongoDB Indexes

To optimize authorization queries:

```csharp
// User indexes
await users.Indexes.CreateOneAsync(
    Builders<UserDocument>.IndexKeys
        .Ascending(u => u.auth0Id)
        .Ascending(u => u.email));

await users.Indexes.CreateOneAsync(
    Builders<UserDocument>.IndexKeys
        .Ascending(u => u.tenantMemberships.tenantId));

// Permission indexes
await permissions.Indexes.CreateOneAsync(
    Builders<PermissionDocument>.IndexKeys
        .Ascending(p => p.entityId)
        .Ascending(p => p.tenantId));

await permissions.Indexes.CreateOneAsync(
    Builders<PermissionDocument>.IndexKeys
        .Ascending(p => p.permissions.userId)
        .Ascending(p => p.tenantId));
```

## Authorization Implementation

### 1. JWT Token-Based Authentication

The application uses JWT tokens with claims for user identity and tenant access:

```csharp
// Example JWT claims
{
  "sub": "auth0|user_identifier",
  "email": "user@example.com",
  "https://xians.ai/tenants": ["tenant_id_1", "tenant_id_2"],
  "https://xians.ai/roles": ["TenantAdmin", "TenantUser"]
}
```

### 2. Tenant Context Middleware

A middleware that extracts tenant context from the JWT token and request context:

```csharp
public class TenantContextMiddleware
{
    private readonly RequestDelegate _next;

    public TenantContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext)
    {
        // Extract tenant ID from route or header
        var tenantId = context.Request.RouteValues["tenantId"]?.ToString() ??
                       context.Request.Headers["X-Tenant-ID"].FirstOrDefault();

        if (string.IsNullOrEmpty(tenantId))
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsync("Tenant ID is required");
            return;
        }

        // Set tenant context
        tenantContext.TenantId = tenantId;
        
        // Extract user claims
        var userIdClaim = context.User.FindFirst("sub");
        if (userIdClaim != null)
        {
            tenantContext.LoggedInUser = userIdClaim.Value;
        }

        // Extract authorized tenants
        var tenantClaims = context.User.FindAll(BaseAuthRequirement.TENANT_CLAIM_TYPE);
        tenantContext.AuthorizedTenantIds = tenantClaims.Select(c => c.Value);

        await _next(context);
    }
}
```

### 3. Authorization Requirements and Handlers

#### System Admin Requirement

```csharp
public class SystemAdminRequirement : BaseAuthRequirement
{
    public SystemAdminRequirement(IConfiguration configuration) : base(configuration) { }
}

public class SystemAdminHandler : BaseAuthHandler<SystemAdminRequirement>
{
    private readonly IUserRepository _userRepository;

    public SystemAdminHandler(
        ILogger<SystemAdminHandler> logger,
        ITenantContext tenantContext,
        IUserRepository userRepository) : base(logger, tenantContext)
    {
        _userRepository = userRepository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        SystemAdminRequirement requirement)
    {
        var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);

        if (!success || string.IsNullOrEmpty(loggedInUser))
        {
            return;
        }

        var user = await _userRepository.GetUserByAuth0IdAsync(loggedInUser);
        
        if (user != null && user.IsSystemAdmin)
        {
            context.Succeed(requirement);
        }
    }
}
```

#### Tenant Admin Requirement

```csharp
public class TenantAdminRequirement : BaseAuthRequirement
{
    public TenantAdminRequirement(IConfiguration configuration) : base(configuration) { }
}

public class TenantAdminHandler : BaseAuthHandler<TenantAdminRequirement>
{
    private readonly IUserRepository _userRepository;

    public TenantAdminHandler(
        ILogger<TenantAdminHandler> logger,
        ITenantContext tenantContext,
        IUserRepository userRepository) : base(logger, tenantContext)
    {
        _userRepository = userRepository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TenantAdminRequirement requirement)
    {
        var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);

        if (!success || string.IsNullOrEmpty(loggedInUser) || !authorizedTenantIds.Any())
        {
            return;
        }

        var user = await _userRepository.GetUserByAuth0IdAsync(loggedInUser);
        
        if (user == null)
        {
            return;
        }

        // Check if user is system admin
        if (user.IsSystemAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        // Check if user is tenant admin for the current tenant
        var tenantMembership = user.TenantMemberships
            .FirstOrDefault(m => m.TenantId == _tenantContext.TenantId && m.Role == "TenantAdmin");
            
        if (tenantMembership != null)
        {
            context.Succeed(requirement);
        }
    }
}
```

#### Entity Permission Requirement

```csharp
public class EntityPermissionRequirement : BaseAuthRequirement
{
    public string RequiredPermissionLevel { get; }

    public EntityPermissionRequirement(
        IConfiguration configuration,
        string requiredPermissionLevel) : base(configuration)
    {
        RequiredPermissionLevel = requiredPermissionLevel;
    }
}

public class EntityPermissionHandler : BaseAuthHandler<EntityPermissionRequirement>
{
    private readonly IUserRepository _userRepository;
    private readonly IPermissionRepository _permissionRepository;

    public EntityPermissionHandler(
        ILogger<EntityPermissionHandler> logger,
        ITenantContext tenantContext,
        IUserRepository userRepository,
        IPermissionRepository permissionRepository) : base(logger, tenantContext)
    {
        _userRepository = userRepository;
        _permissionRepository = permissionRepository;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        EntityPermissionRequirement requirement)
    {
        var (success, loggedInUser, authorizedTenantIds) = ValidateToken(context);

        if (!success || string.IsNullOrEmpty(loggedInUser) || !authorizedTenantIds.Any())
        {
            return;
        }

        // Get the entity from the resource
        if (!(context.Resource is EntityOperationResource resource))
        {
            return;
        }

        var user = await _userRepository.GetUserByAuth0IdAsync(loggedInUser);
        
        if (user == null)
        {
            return;
        }

        // System admins can do anything
        if (user.IsSystemAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        // Check if user is tenant admin for this entity's tenant
        var isTenantAdmin = user.TenantMemberships
            .Any(m => m.TenantId == resource.TenantId && m.Role == "TenantAdmin");
            
        if (isTenantAdmin)
        {
            context.Succeed(requirement);
            return;
        }

        // Check specific permission for this entity
        var permission = await _permissionRepository.GetEntityPermissionAsync(
            resource.EntityId,
            resource.EntityType,
            resource.TenantId);

        if (permission == null)
        {
            return;
        }

        var userPermission = permission.Permissions
            .FirstOrDefault(p => p.UserId == user.Id);

        if (userPermission == null)
        {
            return;
        }

        // Check if the user has the required permission level
        if (HasSufficientPermission(userPermission.Level, requirement.RequiredPermissionLevel))
        {
            context.Succeed(requirement);
        }
    }

    private bool HasSufficientPermission(string actualLevel, string requiredLevel)
    {
        if (actualLevel == "Owner")
        {
            return true;
        }

        if (actualLevel == "Editor" && 
            (requiredLevel == "Editor" || requiredLevel == "Reader"))
        {
            return true;
        }

        if (actualLevel == "Reader" && requiredLevel == "Reader")
        {
            return true;
        }

        return false;
    }
}
```

### 4. Repository Implementation for MongoDB

```csharp
public interface IPermissionRepository
{
    Task<PermissionDocument> GetEntityPermissionAsync(string entityId, string entityType, string tenantId);
    Task<bool> SetPermissionAsync(string entityId, string entityType, string tenantId, string userId, string permissionLevel);
    Task<bool> RemovePermissionAsync(string entityId, string entityType, string tenantId, string userId);
    Task<IEnumerable<PermissionDocument>> GetUserPermissionsAsync(string userId, string tenantId);
}

public class PermissionRepository : IPermissionRepository
{
    private readonly IDatabaseService _databaseService;
    private readonly ITenantContext _tenantContext;

    public PermissionRepository(IDatabaseService databaseService, ITenantContext tenantContext)
    {
        _databaseService = databaseService;
        _tenantContext = tenantContext;
    }

    public async Task<PermissionDocument> GetEntityPermissionAsync(string entityId, string entityType, string tenantId)
    {
        var database = await _databaseService.GetDatabase();
        var collection = database.GetCollection<PermissionDocument>("permissions");

        return await collection.Find(p => 
            p.EntityId == entityId && 
            p.EntityType == entityType && 
            p.TenantId == tenantId)
            .FirstOrDefaultAsync();
    }

    public async Task<bool> SetPermissionAsync(string entityId, string entityType, string tenantId, string userId, string permissionLevel)
    {
        var database = await _databaseService.GetDatabase();
        var collection = database.GetCollection<PermissionDocument>("permissions");

        var permission = await GetEntityPermissionAsync(entityId, entityType, tenantId);

        if (permission == null)
        {
            // Create new permission document
            permission = new PermissionDocument
            {
                EntityId = entityId,
                EntityType = entityType,
                TenantId = tenantId,
                Permissions = new List<UserPermission>
                {
                    new UserPermission
                    {
                        UserId = userId,
                        Level = permissionLevel
                    }
                }
            };

            await collection.InsertOneAsync(permission);
            return true;
        }
        else
        {
            // Update existing permission
            var existingPermission = permission.Permissions.FirstOrDefault(p => p.UserId == userId);
            
            if (existingPermission != null)
            {
                // Update permission level
                existingPermission.Level = permissionLevel;
            }
            else
            {
                // Add new permission
                permission.Permissions.Add(new UserPermission
                {
                    UserId = userId,
                    Level = permissionLevel
                });
            }

            var result = await collection.ReplaceOneAsync(
                p => p.EntityId == entityId && p.EntityType == entityType && p.TenantId == tenantId,
                permission);

            return result.ModifiedCount > 0;
        }
    }

    public async Task<bool> RemovePermissionAsync(string entityId, string entityType, string tenantId, string userId)
    {
        var database = await _databaseService.GetDatabase();
        var collection = database.GetCollection<PermissionDocument>("permissions");

        var permission = await GetEntityPermissionAsync(entityId, entityType, tenantId);

        if (permission == null)
        {
            return false;
        }

        permission.Permissions = permission.Permissions
            .Where(p => p.UserId != userId)
            .ToList();

        var result = await collection.ReplaceOneAsync(
            p => p.EntityId == entityId && p.EntityType == entityType && p.TenantId == tenantId,
            permission);

        return result.ModifiedCount > 0;
    }

    public async Task<IEnumerable<PermissionDocument>> GetUserPermissionsAsync(string userId, string tenantId)
    {
        var database = await _databaseService.GetDatabase();
        var collection = database.GetCollection<PermissionDocument>("permissions");

        return await collection.Find(p => 
            p.TenantId == tenantId && 
            p.Permissions.Any(up => up.UserId == userId))
            .ToListAsync();
    }
}
```

## API Authorization Examples

### Controller Authorization

```csharp
[ApiController]
[Route("api/tenants/{tenantId}/flows")]
public class FlowController : ControllerBase
{
    private readonly IAuthorizationService _authorizationService;
    private readonly IFlowRepository _flowRepository;
    private readonly IPermissionRepository _permissionRepository;
    private readonly ITenantContext _tenantContext;

    public FlowController(
        IAuthorizationService authorizationService,
        IFlowRepository flowRepository,
        IPermissionRepository permissionRepository,
        ITenantContext tenantContext)
    {
        _authorizationService = authorizationService;
        _flowRepository = flowRepository;
        _permissionRepository = permissionRepository;
        _tenantContext = tenantContext;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetFlows()
    {
        // Check if user is tenant admin or system admin
        var authResult = await _authorizationService.AuthorizeAsync(
            User, null, new TenantAdminRequirement(null));
            
        if (authResult.Succeeded)
        {
            // Admin can see all flows
            var allFlows = await _flowRepository.GetAllFlowsAsync(_tenantContext.TenantId);
            return Ok(allFlows);
        }
        else
        {
            // Regular user can only see flows they have access to
            var userPermissions = await _permissionRepository.GetUserPermissionsAsync(
                _tenantContext.LoggedInUser,
                _tenantContext.TenantId);
                
            var flowIds = userPermissions
                .Where(p => p.EntityType == "Flow")
                .Select(p => p.EntityId)
                .ToList();
                
            var flows = await _flowRepository.GetFlowsByIdsAsync(_tenantContext.TenantId, flowIds);
            return Ok(flows);
        }
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetFlow(string id)
    {
        var flow = await _flowRepository.GetFlowByIdAsync(_tenantContext.TenantId, id);
        
        if (flow == null)
        {
            return NotFound();
        }

        // Create resource for authorization
        var resource = new EntityOperationResource
        {
            EntityId = id,
            EntityType = "Flow",
            TenantId = _tenantContext.TenantId
        };

        // Check if user can read this flow
        var authResult = await _authorizationService.AuthorizeAsync(
            User, resource, new EntityPermissionRequirement(null, "Reader"));
            
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        return Ok(flow);
    }

    [HttpDelete("{id}")]
    [Authorize]
    public async Task<IActionResult> DeleteFlow(string id)
    {
        var flow = await _flowRepository.GetFlowByIdAsync(_tenantContext.TenantId, id);
        
        if (flow == null)
        {
            return NotFound();
        }

        // Create resource for authorization
        var resource = new EntityOperationResource
        {
            EntityId = id,
            EntityType = "Flow",
            TenantId = _tenantContext.TenantId
        };

        // Check if user can delete this flow (must be Owner)
        var authResult = await _authorizationService.AuthorizeAsync(
            User, resource, new EntityPermissionRequirement(null, "Owner"));
            
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        await _flowRepository.DeleteFlowAsync(_tenantContext.TenantId, id);
        return NoContent();
    }

    [HttpPost("{id}/share")]
    [Authorize]
    public async Task<IActionResult> ShareFlow(string id, [FromBody] ShareFlowRequest request)
    {
        var flow = await _flowRepository.GetFlowByIdAsync(_tenantContext.TenantId, id);
        
        if (flow == null)
        {
            return NotFound();
        }

        // Create resource for authorization
        var resource = new EntityOperationResource
        {
            EntityId = id,
            EntityType = "Flow",
            TenantId = _tenantContext.TenantId
        };

        // Check if user can share this flow (must be Owner)
        var authResult = await _authorizationService.AuthorizeAsync(
            User, resource, new EntityPermissionRequirement(null, "Owner"));
            
        if (!authResult.Succeeded)
        {
            return Forbid();
        }

        // Share the flow with the specified user
        await _permissionRepository.SetPermissionAsync(
            id,
            "Flow",
            _tenantContext.TenantId,
            request.UserId,
            request.PermissionLevel);

        return Ok();
    }
}
```

## Conclusion

This authorization design provides a flexible, MongoDB-compatible authorization system that supports:

1. **Multi-tenancy**: Clear separation between tenants and their data
2. **Hierarchical roles**: System admins > Tenant admins > Tenant users
3. **Granular entity permissions**: Owner, Editor, Reader levels for entities
4. **MongoDB-optimized storage**: Document-based permission structure with efficient indexes
5. **JWT integration**: Leveraging token claims for tenant and user information

The implementation uses a combination of role-based authorization for higher-level permissions (system/tenant admin) and resource-based authorization for entity-level permissions, all backed by MongoDB collections for scalable and flexible storage.
