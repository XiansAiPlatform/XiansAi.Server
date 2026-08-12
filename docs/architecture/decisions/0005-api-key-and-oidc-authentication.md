# ADR-0005: API Key and OIDC Authentication Strategy

**Status:** Proposed

**Date:** 2026-07-16

## Context

The XiansAi Server exposes multiple API surfaces with different authentication requirements:

- **AdminApi**: Server-to-server or CLI access requiring high privileges (manage tenants, agents)
- **WebApi**: Client applications (web, mobile) accessing tenant-scoped resources on behalf of users
- **AgentApi**: Agent workflows accessing internal services (secrets, cache, logs)
- **UserApi**: End-user webhooks and websocket connections
- **AppsApi**: External platform webhooks (Slack, Teams) with signature-based verification

We needed an authentication strategy that:
- Supports both machine (API key) and human (OIDC/OAuth) authentication
- Applies the appropriate mechanism per API surface
- Allows flexible provider configuration (Auth0, Azure AD, Keycloak)

## Decision

We implement a **dual authentication strategy**:

1. **API Key Authentication** for AdminApi and AgentApi:
   - Custom `AuthenticationHandler` validates Bearer tokens against stored API keys
   - API keys are tenant-scoped or system-scoped
   - Authorization policies (`AdminEndpointAuthPolicy`, `AgentEndpointAuthPolicy`) enforce role requirements

2. **OIDC/OAuth Authentication** for WebApi and UserApi:
   - JWT Bearer tokens issued by external identity providers (Auth0, Azure AD, Azure B2C, Keycloak)
   - Provider configured via `appsettings.json` (`AuthProvider:Provider`)
   - Standard OIDC claims mapped to application roles and tenant context

3. **Signature-based Verification** for AppsApi webhooks:
   - Platform-specific signature validation (HMAC-SHA256 for Slack, JWT for Teams)
   - No traditional Bearer token authentication

Each feature registers its own authentication scheme and authorization policies in its Configuration class.

## Consequences

**Positive:**
- Flexibility: each API surface uses the authentication model that fits its use case
- Security: strong machine authentication (API keys) and standard human authentication (OIDC)
- Provider independence: swap Auth0 for Keycloak without changing application logic
- Clear separation: AdminApi keys cannot access WebApi endpoints and vice versa

**Negative:**
- Complexity: multiple authentication schemes in a single application
- Configuration overhead: each provider (Auth0, Azure AD) requires specific settings
- Testing challenges: must stub multiple authentication mechanisms

**Mitigations:**
- Clear documentation per API surface (see `docs/AUTH_CONFIGURATION.md`)
- Use `TestAuthHandler` in integration tests to unify test authentication
- Endpoint filters and policies make authentication enforcement explicit and auditable
