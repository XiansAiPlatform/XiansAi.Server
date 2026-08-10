# Architecture Fitness Functions

This document maps each architecture constraint to a verification heuristic or check. These functions are used by automated tooling and human reviewers to assess constraint compliance.

---

## ARCH-001 — Feature-slice organization

**Heuristic:** Feature code must live under `Features/<FeatureName>Api/` with conventional subdirectories.

**Check:**
```bash
# Verify all feature APIs follow directory conventions
find XiansAi.Server.Src/Features -mindepth 1 -maxdepth 1 -type d | while read feature; do
  if [[ ! -d "$feature/Endpoints" ]]; then
    echo "VIOLATION: $feature missing Endpoints/"
  fi
done

# Detect misplaced feature code
find XiansAi.Server.Src -name '*Endpoints.cs' -o -name '*Service.cs' | grep -v 'Features/' | grep -v 'Shared/'
```

**False positives:**
- `Shared/Services/` is allowed (cross-cutting services)
- Test projects may have different structure

---

## ARCH-002 — Shared layer for cross-cutting concerns

**Heuristic:** `Shared/` must only contain infrastructure and cross-feature abstractions, not feature business logic.

**Check:**
```bash
# Look for feature-specific names in Shared
find XiansAi.Server.Src/Shared -name '*Admin*.cs' -o -name '*WebApi*.cs' -o -name '*AgentApi*.cs' -o -name '*UserApi*.cs' -o -name '*AppsApi*.cs'

# Manually review Shared/Services for feature-specific logic
```

**False positives:**
- `AdminStatsService` might be legitimately shared if used across multiple features
- Names like `AdminAuthHandler` in `Shared/Auth` may be acceptable if the auth pattern is shared

**Review trigger:** New files added to `Shared/` should be reviewed to ensure they are truly cross-cutting.

---

## ARCH-003 — Feature dependencies flow outward

**Heuristic:** Check for `using Features.*` statements in `Shared/` namespace files.

**Check:**
```bash
# Scan Shared/ for any imports of Features namespaces
grep -r "using Features\." XiansAi.Server.Src/Shared/
```

**Expected output:** None (empty result = compliant)

**False positives:** None. This is a hard constraint.

---

## ARCH-004 — Feature slice isolation

**Heuristic:** Check for cross-feature `using` statements (e.g., `Features.AdminApi` referencing `Features.WebApi`).

**Check:**
```bash
# Example: scan AdminApi for references to other features
grep -r "using Features\.WebApi\|using Features\.AgentApi\|using Features\.AppsApi\|using Features\.UserApi" XiansAi.Server.Src/Features/AdminApi/

# Generalize for all features
for feature in AdminApi WebApi AgentApi UserApi AppsApi; do
  echo "Checking $feature for cross-feature dependencies..."
  grep -r "using Features\." "XiansAi.Server.Src/Features/$feature/" | grep -v "using Features\.$feature"
done
```

**Expected output:** None

**False positives:**
- Test projects may reference multiple features
- Configuration or Program.cs orchestrates features and will reference multiple Features namespaces (acceptable)

---

## ARCH-005 — Endpoint-scoped authentication policies

**Heuristic:** All `Map*` endpoint registrations must chain `.RequireAuthorization(...)`.

**Check:**
```bash
# Find MapGet/MapPost/MapPut/MapDelete/MapPatch without RequireAuthorization
grep -E "Map(Get|Post|Put|Delete|Patch)" XiansAi.Server.Src/Features/*/Endpoints/*.cs | \
  grep -v "RequireAuthorization"
```

**False positives:**
- Webhook endpoints (`/api/apps/{platform}/webhook`) may not use `.RequireAuthorization` if they implement custom signature validation
- `.RequireAuthorization()` may be set on the parent `MapGroup` instead of each individual endpoint

**Better check:** Verify that the endpoint group declares authorization before mapping endpoints.

---

## ARCH-006 — Tenant isolation enforcement

**Heuristic:** Routes containing `{tenantId}` must apply `TenantRouteScopeFilter` or equivalent.

**Check:**
```bash
# Find routes with tenantId in path
grep -E "MapGroup\(.*tenantId" XiansAi.Server.Src/Features/*/Endpoints/*.cs | while read line; do
  file=$(echo "$line" | cut -d: -f1)
  # Check if TenantRouteScopeFilter appears in the same endpoint file
  if ! grep -q "TenantRouteScopeFilter" "$file"; then
    echo "VIOLATION: $file has tenantId route but no TenantRouteScopeFilter"
  fi
done
```

**False positives:**
- Some endpoints may validate tenant scope inline (less common but valid if equivalent logic exists)

---

## ARCH-007 — Repository layer for data access

**Heuristic:** Endpoint and service files should not directly call `GetCollection<T>()` or `IMongoDatabase`.

**Check:**
```bash
# Scan endpoints and services for direct MongoDB usage
grep -r "GetCollection<" XiansAi.Server.Src/Features/*/Endpoints/
grep -r "IMongoDatabase" XiansAi.Server.Src/Features/*/Endpoints/
grep -r "GetCollection<" XiansAi.Server.Src/Features/*/Services/ | grep -v Repository
```

**Expected output:** None outside of Repository classes

**False positives:**
- Repository classes themselves will use `GetCollection<T>()` (allowed)
- Migration or seeding scripts may use direct access (acceptable)

---

## ARCH-008 — Minimal API endpoint pattern

**Heuristic:** No `[ApiController]` or `ControllerBase` usage in Features.

**Check:**
```bash
# Detect MVC controller usage
grep -r "\[ApiController\]" XiansAi.Server.Src/Features/
grep -r ": ControllerBase" XiansAi.Server.Src/Features/
```

**Expected output:** None

**False positives:** None. This is a hard constraint.

---

## ARCH-009 — API versioning via URL path

**Heuristic:** All public API routes should match `/api/v\d+/` pattern.

**Check:**
```bash
# Find MapGroup calls without /api/v
grep -E "MapGroup\(" XiansAi.Server.Src/Features/*/Configuration/*.cs | grep -v "/api/v"
```

**False positives:**
- Internal webhook endpoints (`/api/apps/`) are exempt
- Health check endpoints (`/health`) are exempt

**Manual review:** Examine each result to confirm it's a public API that requires versioning.

---

## ARCH-010 — Configuration via DI registration

**Heuristic:** Each feature should have a `Configuration/*Configuration.cs` file with a `Configure<Feature>Services()` method.

**Check:**
```bash
# Verify each feature has a Configuration directory
for feature in AdminApi WebApi AgentApi UserApi AppsApi; do
  if [[ ! -d "XiansAi.Server.Src/Features/$feature/Configuration" ]]; then
    echo "VIOLATION: $feature missing Configuration/"
  fi
done

# Check for Configure*Services method in each feature
for feature in AdminApi WebApi AgentApi UserApi AppsApi; do
  if ! grep -q "Configure${feature}Services" "XiansAi.Server.Src/Features/$feature/Configuration/"*.cs 2>/dev/null; then
    echo "WARNING: $feature may be missing Configure${feature}Services method"
  fi
done
```

**False positives:**
- Small features may register services inline in Program.cs (acceptable if documented)

---

## ARCH-011 — Test isolation and in-memory dependencies

**Heuristic:** Integration tests should use `MongoDbFixture`, `TestAuthHandler`, and mocked external services.

**Check:**
```bash
# Verify no hardcoded MongoDB connection strings in tests
grep -r "mongodb://" XiansAi.Server.Tests/ | grep -v "localhost"
grep -r "mongodb+srv://" XiansAi.Server.Tests/

# Ensure TestAuthHandler is used
grep -r "AddAuthentication" XiansAi.Server.Tests/TestUtils/ | grep -q "TestAuthHandler"
```

**Manual review:**
- Check `XiansAiWebApplicationFactory.cs` for proper service mocking
- Verify `appsettings.Tests.json` points to in-memory or test fixtures

---

## ARCH-012 — Secrets encryption at rest

**Heuristic:** Look for sensitive field assignments without encryption service usage.

**Check:**
```bash
# Search for patterns like Token =, ApiKey =, Secret = without Encrypt
grep -r "Token = " XiansAi.Server.Src/Features/ | grep -v "Encrypt"
grep -r "ApiKey = " XiansAi.Server.Src/Features/ | grep -v "Encrypt"
grep -r "Secret = " XiansAi.Server.Src/Features/ | grep -v "Encrypt"
```

**False positives:**
- Local variables or DTOs that hold pre-encrypted values
- Decryption operations (`Token = await Decrypt(...)`)
- Configuration loading (environment variables are not stored in DB)

**Manual review:** Examine repository `SaveAsync` / `CreateAsync` methods to verify encryption is applied before persistence.

---

## Summary

| Constraint ID | Automated? | Tool |
|---|---|---|
| ARCH-001 | Partial | Shell script / directory structure check |
| ARCH-002 | Manual | Code review for Shared/ additions |
| ARCH-003 | Yes | `grep` for `using Features.*` in Shared |
| ARCH-004 | Yes | `grep` for cross-feature using statements |
| ARCH-005 | Partial | Grep + manual review of authorization chains |
| ARCH-006 | Partial | Grep for tenantId routes + filter check |
| ARCH-007 | Yes | Grep for `GetCollection<T>` in Endpoints/Services |
| ARCH-008 | Yes | Grep for `[ApiController]` or `ControllerBase` |
| ARCH-009 | Partial | Grep for MapGroup without `/api/v` |
| ARCH-010 | Partial | Check for Configuration/ directory and method |
| ARCH-011 | Manual | Review test setup and appsettings.Tests.json |
| ARCH-012 | Manual | Review repository methods for encryption usage |

Future enhancements may include Roslyn analyzers for constraints ARCH-003, ARCH-004, ARCH-005, ARCH-007, and ARCH-008.
