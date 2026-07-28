# ADR-0004: Repository Pattern for Data Access

**Status:** Proposed

**Date:** 2026-07-16

## Context

The XiansAi Server uses MongoDB for persistence. Allowing endpoints and services to directly interact with `IMongoCollection<T>` creates several issues:

- Scattered query logic across the codebase
- Difficulty changing data models or indexes
- Hard to mock or test data access
- No single location for tenant scoping or security checks

We needed a consistent data access pattern that:
- Encapsulates MongoDB operations
- Provides a clear abstraction boundary
- Enables testability and future data store changes

## Decision

We adopt the **Repository Pattern** for all MongoDB data access:

1. Repositories reside in `Shared/Repositories/` for cross-feature entities (Conversation, Tenant, Agent) or in feature-specific directories for feature-local entities
2. Each repository exposes domain-focused methods (e.g., `GetByTenantAsync`, `CreateAsync`, `UpdateAsync`)
3. Repositories encapsulate MongoDB collection access, query logic, and index definitions
4. Endpoints and services depend on repository interfaces, not `IMongoDatabase` or `IMongoCollection<T>`

Example:
```csharp
public interface IConversationRepository
{
    Task<Conversation?> GetByIdAsync(string tenantId, string conversationId);
    Task<List<Conversation>> GetByTenantAsync(string tenantId, int limit);
    Task CreateAsync(Conversation conversation);
}
```

## Consequences

**Positive:**
- Centralized query logic and index management
- Easier to test: mock repository interfaces instead of MongoDB
- Clear data access boundary for security reviews
- Supports future data store changes (e.g., Cosmos DB, PostgreSQL)
- Tenant scoping can be enforced at repository level

**Negative:**
- Additional abstraction layer adds indirection
- Repository explosion if not carefully designed (one per entity)
- May duplicate some query logic across repositories

**Mitigations:**
- Use generic base repositories for common CRUD patterns
- Extract shared query logic to helper methods
- Prefer coarse-grained repositories over fine-grained (e.g., one ConversationRepository, not separate MessageRepository and ParticipantRepository)
