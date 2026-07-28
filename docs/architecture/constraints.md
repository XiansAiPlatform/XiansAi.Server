# Architecture Constraints

This document defines testable architectural constraints for the XiansAi Server. All constraints marked as `proposed` require human ratification before enforcement.

---

### ARCH-001 — Feature-slice organization

| Field | Value |
|---|---|
| **ID** | ARCH-001 |
| **Severity** | high |
| **Scope** | `XiansAi.Server.Src/Features/*` |
| **Status** | proposed |
| **Rationale** | Feature slices provide vertical cohesion, reducing coupling between unrelated API domains and enabling independent scaling and deployment |

**Rule:** Each API feature must be organized as a self-contained slice under `Features/<FeatureName>Api/` with subdirectories for Endpoints, Services, Configuration, and Auth.

**Allowed:**
- `Features/AdminApi/Endpoints/AdminStatsEndpoints.cs`
- `Features/WebApi/Services/WorkflowService.cs`
- `Features/AppsApi/Configuration/AppsApiConfiguration.cs`

**Forbidden:**
- `Services/AdminStatsService.cs` (cross-feature service outside the slice)
- `Features/AdminApi/WebApiController.cs` (mixing feature concerns)
- `Features/Shared/Endpoints/` (shared code belongs in `Shared/`, not `Features/`)

---

### ARCH-002 — Shared layer for cross-cutting concerns

| Field | Value |
|---|---|
| **ID** | ARCH-002 |
| **Severity** | high |
| **Scope** | `XiansAi.Server.Src/Shared/*` |
| **Status** | proposed |
| **Rationale** | Centralizing infrastructure (data access, auth primitives, providers) prevents duplication and ensures consistent security and persistence patterns across features |

**Rule:** Cross-cutting concerns (Data access, Auth primitives, Providers, Utils, Repositories, shared Services) must reside under `Shared/` and must not contain feature-specific business logic.

**Allowed:**
- `Shared/Data/MongoDbContext.cs`
- `Shared/Auth/ApiKeyAuthenticationHandler.cs`
- `Shared/Providers/Cache/RedisCacheProvider.cs`
- `Shared/Repositories/ConversationRepository.cs` (generic conversation storage)

**Forbidden:**
- `Shared/Services/AdminStatsService.cs` (feature-specific logic)
- `Shared/Endpoints/AdminEndpoints.cs` (endpoints belong in Features)
- `Features/AdminApi/Data/MongoConnection.cs` (data access duplication)

---

### ARCH-003 — Feature dependencies flow outward

| Field | Value |
|---|---|
| **ID** | ARCH-003 |
| **Severity** | critical |
| **Scope** | `XiansAi.Server.Src/Features/*` |
| **Status** | proposed |
| **Rationale** | Feature slices may depend on Shared, but Shared must never depend on Features to prevent circular dependencies and maintain a clear layering boundary |

**Rule:** Feature slices may reference `Shared/` namespaces. `Shared/` must never reference `Features/` namespaces.

**Allowed:**
```csharp
// In Features/AdminApi/Endpoints/AdminStatsEndpoints.cs
using Shared.Services;
using Shared.Utils.Services;
```

**Forbidden:**
```csharp
// In Shared/Services/MessageService.cs
using Features.AdminApi.Services;  // Violation: Shared depends on Feature
```

---

### ARCH-004 — Feature slice isolation

| Field | Value |
|---|---|
| **ID** | ARCH-004 |
| **Severity** | medium |
| **Scope** | `XiansAi.Server.Src/Features/*` |
| **Status** | proposed |
| **Rationale** | Cross-feature dependencies create coupling and break the feature slice pattern. Shared concerns should be extracted to Shared/ |

**Rule:** Feature slices should not directly reference other feature slices. Shared abstractions must be extracted to `Shared/`.

**Allowed:**
```csharp
// Features communicate via shared services/repositories
// In Features/AdminApi/Endpoints/
using Shared.Services.IMessageService;
```

**Forbidden:**
```csharp
// In Features/WebApi/Services/
using Features.AdminApi.Services.AdminStatsService;  // Violation: direct feature coupling
```

---

### ARCH-005 — Endpoint-scoped authentication policies

| Field | Value |
|---|---|
| **ID** | ARCH-005 |
| **Severity** | critical |
| **Scope** | `XiansAi.Server.Src/Features/*/Endpoints/*.cs` |
| **Status** | proposed |
| **Rationale** | Each API surface has distinct authentication requirements (AdminApi uses API keys, WebApi uses OIDC). Explicit policy declaration prevents accidental exposure |

**Rule:** All endpoint groups must explicitly declare an authorization policy via `.RequireAuthorization("<PolicyName>")`. No endpoints may be registered without authentication.

**Allowed:**
```csharp
var group = adminApiGroup.MapGroup("/tenants/{tenantId}")
    .RequireAuthorization("AdminEndpointAuthPolicy");
```

**Forbidden:**
```csharp
app.MapGet("/api/v1/admin/stats", handler);  // No policy specified
```

---

### ARCH-006 — Tenant isolation enforcement

| Field | Value |
|---|---|
| **ID** | ARCH-006 |
| **Severity** | critical |
| **Scope** | All tenant-scoped endpoints and repositories |
| **Status** | proposed |
| **Rationale** | Multi-tenant SaaS requires strict tenant data isolation to prevent unauthorized cross-tenant access (IDOR) |

**Rule:** All tenant-scoped endpoints must apply `TenantRouteScopeFilter` or equivalent to validate that the authenticated user/key has access to the requested tenantId.

**Allowed:**
```csharp
var statsGroup = adminApiGroup.MapGroup("/tenants/{tenantId}")
    .AddEndpointFilter<TenantRouteScopeFilter>();
```

**Forbidden:**
```csharp
// Endpoint with tenantId in route but no filter
app.MapGet("/api/v1/admin/tenants/{tenantId}/data", async (string tenantId, ...) => { ... });
```

---

### ARCH-007 — Repository layer for data access

| Field | Value |
|---|---|
| **ID** | ARCH-007 |
| **Severity** | high |
| **Scope** | Data access operations |
| **Status** | proposed |
| **Rationale** | Encapsulating MongoDB access in repositories centralizes query logic, indexes, and schema mapping, preventing direct database coupling in endpoints |

**Rule:** Endpoints and services must access MongoDB through repository classes (`Shared/Repositories/*` or feature-specific repositories). Direct `IMongoCollection<T>` usage outside repositories is forbidden.

**Allowed:**
```csharp
// In endpoint
var stats = await statsRepository.GetByTenantAsync(tenantId);
```

**Forbidden:**
```csharp
// In endpoint
var collection = mongoDatabase.GetCollection<StatsDocument>("stats");
var stats = await collection.Find(x => x.TenantId == tenantId).ToListAsync();
```

---

### ARCH-008 — Minimal API endpoint pattern

| Field | Value |
|---|---|
| **ID** | ARCH-008 |
| **Severity** | medium |
| **Scope** | `XiansAi.Server.Src/Features/*/Endpoints/*.cs` |
| **Status** | proposed |
| **Rationale** | Consistent use of ASP.NET Core Minimal APIs reduces boilerplate, improves discoverability, and aligns with modern .NET patterns |

**Rule:** HTTP endpoints must be defined using Minimal API `Map*` methods in static endpoint classes. MVC controllers are not permitted.

**Allowed:**
```csharp
public static class AdminStatsEndpoints
{
    public static void MapAdminStatsEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/stats", handler);
    }
}
```

**Forbidden:**
```csharp
[ApiController]
[Route("api/v1/admin")]
public class AdminStatsController : ControllerBase { }
```

---

### ARCH-009 — API versioning via URL path

| Field | Value |
|---|---|
| **ID** | ARCH-009 |
| **Severity** | medium |
| **Scope** | All public API endpoints |
| **Status** | proposed |
| **Rationale** | URL-based versioning (`/api/v1/`, `/api/v2/`) provides explicit, cache-friendly version signaling and avoids header/query complexity |

**Rule:** All public API routes must include a version segment (`/api/v{version}/...`). Internal/webhook endpoints may omit versioning if they are not part of the public contract.

**Allowed:**
- `/api/v1/admin/tenants`
- `/api/v2/admin/agents`
- `/api/apps/slack/webhook/{instanceId}` (webhook endpoint, not public API)

**Forbidden:**
- `/api/admin/tenants` (missing version)

---

### ARCH-010 — Configuration via DI registration

| Field | Value |
|---|---|
| **ID** | ARCH-010 |
| **Severity** | medium |
| **Scope** | `XiansAi.Server.Src/Features/*/Configuration/*.cs` |
| **Status** | proposed |
| **Rationale** | Each feature slice registers its own services, making dependencies explicit and enabling independent feature testing |

**Rule:** Each feature must provide a `Configure<Feature>Services()` extension method on `IServiceCollection` that registers all feature-specific services.

**Allowed:**
```csharp
// Features/AdminApi/Configuration/AdminApiConfiguration.cs
public static IServiceCollection ConfigureAdminApiServices(this IServiceCollection services)
{
    services.AddScoped<IAdminStatsService, AdminStatsService>();
    return services;
}
```

**Forbidden:**
- Registering AdminApi services in `Program.cs` or another feature's configuration
- Service registration scattered across multiple files

---

### ARCH-011 — Test isolation and in-memory dependencies

| Field | Value |
|---|---|
| **ID** | ARCH-011 |
| **Severity** | high |
| **Scope** | `XiansAi.Server.Tests/*` |
| **Status** | proposed |
| **Rationale** | Integration tests must be self-contained and not depend on external services to ensure reliability and fast feedback |

**Rule:** Integration tests must use in-memory/stubbed dependencies (MongoDB in-memory, mocked Temporal, stubbed auth). No test may require a live external service.

**Allowed:**
- `MongoDbFixture` (in-memory MongoDB)
- `TestAuthHandler` (stubbed authentication)
- Mocked `ITemporalClientService`

**Forbidden:**
- Tests connecting to a live MongoDB instance
- Tests calling real Temporal workflows
- Tests requiring Auth0/Azure AD

---

### ARCH-012 — Secrets encryption at rest

| Field | Value |
|---|---|
| **ID** | ARCH-012 |
| **Severity** | critical |
| **Scope** | Secret storage (API keys, tokens, credentials) |
| **Status** | proposed |
| **Rationale** | Storing plaintext secrets in the database violates security best practices and compliance requirements |

**Rule:** All sensitive credentials (API keys, OAuth tokens, signing secrets) stored in MongoDB must be encrypted using the encryption service before persistence.

**Allowed:**
```csharp
var encryptedToken = await encryptionService.EncryptAsync(plainToken);
await repository.SaveAsync(new Config { Token = encryptedToken });
```

**Forbidden:**
```csharp
await repository.SaveAsync(new Config { Token = plainTextToken });  // Violation: plaintext secret
```

---
