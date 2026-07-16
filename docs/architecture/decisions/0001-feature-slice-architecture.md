# ADR-0001: Feature Slice Architecture

**Status:** Proposed

**Date:** 2026-07-16

## Context

The XiansAi Server provides multiple API surfaces (AdminApi, WebApi, AgentApi, UserApi, AppsApi) that serve different audiences and have distinct authentication, authorization, and domain concerns. Traditional N-tier layering (Controllers → Services → Repositories) would create horizontal coupling across unrelated features, making it difficult to understand, test, and deploy individual API surfaces independently.

We needed an architectural approach that:
- Provides vertical cohesion within each API surface
- Enables independent evolution and deployment of features
- Reduces coupling between unrelated concerns
- Maintains clear boundaries for testing and ownership

## Decision

We adopt a **feature-slice architecture** where each API surface is organized as a self-contained vertical slice under `Features/<FeatureName>Api/`. Each slice contains:

- `Endpoints/` - Minimal API endpoint definitions
- `Services/` - Feature-specific business logic
- `Configuration/` - Service registration and setup
- `Auth/` - Feature-specific authentication/authorization handlers
- `Models/` or `Requests/` - DTOs and request/response types (when needed)

Cross-cutting concerns (data access, shared auth primitives, providers, utilities) are extracted to a `Shared/` layer that features depend on, but which never depends back on features.

## Consequences

**Positive:**
- Each feature can be understood, tested, and changed independently
- Clear ownership boundaries (team X owns AdminApi, team Y owns WebApi)
- Easier onboarding: new developers can focus on one feature slice
- Supports future microservice extraction (each slice could become a separate service)
- Reduced merge conflicts across teams

**Negative:**
- Some duplication across features (e.g., similar validation logic)
- Requires discipline to avoid cross-feature dependencies
- Shared concerns must be identified and extracted intentionally

**Mitigations:**
- Use `ARCH-003` and `ARCH-004` constraints to enforce dependency rules
- Extract truly shared logic to `Shared/` proactively
- Code reviews focus on feature boundary violations
