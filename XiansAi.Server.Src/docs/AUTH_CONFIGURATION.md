# Authentication Provider Configuration

This document provides a comprehensive guide for configuring authentication providers in the XiansAi Server. The system supports multiple authentication providers through a unified interface, allowing you to switch between providers with minimal configuration changes.

> **Note:** Configuring an identity provider is optional. It is only required for Agent Studio user (browser) login via the WebAPI. If `AuthProvider__Provider` is omitted (or set to `None`), the WebAPI login surface is not wired and the platform runs in Admin-API-key-only mode — Admin APIs authenticate with the bootstrapped API key and agents authenticate with certificates.

## Architecture Overview

The authentication system uses a factory pattern to support multiple providers:

- **Auth0** - Third-party authentication service
- **Azure AD/Entra ID** - Microsoft's identity platform
- **Azure B2C** - Microsoft's customer identity platform
- **Keycloak** - Open-source identity and access management

All providers implement the `IAuthProvider` interface, ensuring consistent behavior across different authentication backends.

## Core Configuration

### Provider Selection

The primary configuration that determines which provider to use:

```bash
# Choose your authentication provider
AuthProvider__Provider=Auth0          # Options: Auth0, AzureB2C, Keycloak
AuthProvider__TenantClaimType=https://your-domain.com/tenants  # Custom claim type for tenant information
```

## Provider-Specific Configuration

### Auth0 Configuration

Auth0 is a popular third-party authentication service that handles user management and authentication flows.

**Required Configuration:**

```bash
AuthProvider__Provider=Auth0

# Auth0 Domain and Audience
Auth0__Domain=your-domain.auth0.com
Auth0__Audience=https://your-api-audience/api

# Management API Credentials (for user management)
Auth0__ManagementApi__ClientId=your-management-client-id
Auth0__ManagementApi__ClientSecret=your-management-client-secret
```

**Key Features:**

- Automatic JWT validation through Auth0's public keys
- Built-in user management through Management API
- Support for custom claims and tenant information
- Automatic role synchronization from database

**Setup Steps:**

1. Create an Auth0 application
2. Configure the audience and domain
3. Set up a Machine-to-Machine application for the Management API
4. Configure custom claims rules for tenant information

### Azure AD/Entra ID Configuration

Azure AD (now called Microsoft Entra ID) is Microsoft's enterprise identity platform.

**Required Configuration:**

```bash
AuthProvider__Provider=AzureB2C

# Azure AD Configuration
AzureB2C__TenantId=your-tenant-id-guid
AzureB2C__Audience=api://your-app-registration-id
AzureB2C__JwksUri=https://login.microsoftonline.com/your-tenant-id/discovery/v2.0/keys
AzureB2C__Issuer=https://sts.windows.net/your-tenant-id/
AzureB2C__Authority=https://login.microsoftonline.com/your-tenant-id/v2.0/

# Optional
AuthProvider__TenantClaimType=https://your-domain.com/tenants

```

**Key Features:**

- Enterprise-grade security and compliance
- Integration with Microsoft ecosystem
- Advanced conditional access policies
- Support for multi-factor authentication

**Setup Steps:**

1. Register an application in Azure AD
2. Configure API permissions and scopes
3. Set up app registration for your API
4. Configure token claims for tenant information

### Azure B2C Configuration

Azure B2C is Microsoft's customer identity platform, designed for customer-facing applications.

**Required Configuration:**

```bash
AuthProvider__Provider=AzureB2C

# Azure B2C Configuration
AzureB2C__TenantId=your-b2c-tenant-id
AzureB2C__Audience=your-app-registration-id
AzureB2C__JwksUri=https://your-tenant.b2clogin.com/your-tenant-id/B2C_1A_SIGNUP_SIGNIN/discovery/v2.0/keys
AzureB2C__Issuer=https://your-tenant.b2clogin.com/your-tenant-id/v2.0/
AzureB2C__Authority=https://your-tenant.b2clogin.com/your-tenant-id/B2C_1A_SIGNUP_SIGNIN/v2.0/

# Optional
AuthProvider__TenantClaimType=https://login-dev.parkly.no/tenants

```

**Key Features:**

- Customer identity and access management
- Custom branding and user experiences
- Social identity provider integration
- Custom user attributes and claims

**Setup Steps:**

1. Create an Azure B2C tenant
2. Set up user flows or custom policies
3. Register your application
4. Configure custom attributes for tenant information

### Keycloak Configuration

Keycloak is an open-source identity and access management solution.

**Required Configuration:**

```bash
AuthProvider__Provider=Keycloak

# Keycloak Configuration
Keycloak__AuthServerUrl=https://your-keycloak-server/
Keycloak__Realm=your-realm-name
Keycloak__ValidIssuer=https://your-keycloak-server/realms/your-realm-name
```

**Key Features:**

- Open-source and self-hosted
- Fine-grained authorization policies
- Federation with external identity providers
- Extensive customization options

**Setup Steps:**

1. Deploy Keycloak server
2. Create a realm for your application
3. Configure clients and users
4. Set up custom attributes for tenant information

## Advanced Configuration

### Token Validation Caching

To improve performance, token validation results can be cached. The cache uses an in-memory store with configurable size limits to prevent memory exhaustion attacks:

```bash
# Token validation cache duration in minutes (default: 5)
Auth__TokenValidationCacheDurationMinutes=5

# Maximum number of cache entries to prevent DoS attacks (default: 10000)
# This limits how many tokens can be cached simultaneously
Auth__TokenValidationCacheSizeLimit=10000

# Size per cache entry for eviction policy (default: 1)
# Used by the cache eviction algorithm when size limit is reached
Auth__TokenValidationCacheEntrySize=1
```

**Security Notes:**

- Only successful token validations are cached to prevent cache poisoning
- Cache uses SHA-256 hashes of tokens as keys to avoid storing sensitive data
- Cache entries use normal priority to allow proper eviction under memory pressure
- Failed validations always trigger fresh validation to prevent attacks

### OIDC Validation Caching (User API)

The User API validates a JWT against the calling tenant's OIDC rules on every request, which means
verifying a signature and reading the tenant's provider configuration. Successful validations are
cached briefly so that chatty clients and SSE reconnect loops do not repeat that work:

```bash
# How long a successful OIDC validation may be reused, in seconds (default: 60)
# Set to 0 to validate every request from scratch.
Auth__OidcValidationCacheDurationSeconds=60

# How long to wait for a provider's discovery document, in seconds (default: 30)
Auth__OidcDiscoveryTimeoutSeconds=30
```

**Security Notes:**

- A cache entry never outlives the token itself, so caching cannot extend a token's lifetime
- Only successful validations are cached; a rejected token is always re-validated
- Entries are keyed on the tenant and a SHA-256 hash of the token, never the token itself
- Tenant membership is cached separately and more briefly — see `Auth__ApprovedTenantCacheDurationSeconds`

### OIDC Hardening

Tenant OIDC rules are per-tenant records edited at runtime through an API. Writing them requires
SysAdmin, but they are still records rather than reviewed deployment configuration, so a few of them
are not taken at face value. Some settings are simply overridden; the two that would change who can
sign in are behind switches that start off, warn about every affected tenant, and can be turned on
once the warnings stop.

```bash
# Refuse a provider that does not declare the audiences it accepts (default: false)
Auth__RequireOidcAudience=false

# Read the subject only from claims OIDC guarantees to be stable (default: false)
Auth__StrictSubjectClaim=false

# How often a recurring misconfiguration is logged, per tenant and provider (default: 15)
Auth__OidcWarningIntervalMinutes=15
```

**Always enforced, regardless of tenant configuration:**

- Tokens are signature-verified. A provider setting `requireSignedTokens: false` is rejected when
  saved, and ignored if an older record still has it.
- `none` is stripped from the accepted algorithms, so an unsigned token can never be accepted.
- In Production, a provider authority must be an `https` URL that is not loopback, private, or
  link-local. This stops a tenant configuration from pointing the server at an internal address
  such as the cloud instance metadata endpoint. Outside Production these are permitted, so local
  development can run an identity provider on localhost.

  This blocks addresses written directly into a configuration. It cannot stop a hostname that
  resolves to an internal address, which needs egress control at the network layer.
- A provider must declare `expectedAudience`. Without one it accepts any token its issuer signed,
  including one minted for an unrelated application at that same identity provider, and a UserApi
  sign-in turns a valid token into approved tenant membership. This is refused at save time even for
  a provider that predates the rule and even on a save that does not touch it, so a tenant
  configured that way cannot save any OIDC change until it declares one. Sign-in is unaffected until
  `Auth__RequireOidcAudience` is enabled; membership is (see below).
- A mutable `userIdClaim` / `userIdClaims` entry (`email`, `emails`, `preferred_username`, `upn`,
  `name`, `nameid`, `unique_name`, and the matching claim-type URIs) is refused when newly
  introduced or changed. The portal resolves identity from the deployment auth provider's stable
  subject (`sub`/`oid`); nominating an address as the UserApi subject is what creates a second
  account for the same person. An unchanged pre-existing mutable claim is grandfathered so the
  tenant can still edit unrelated settings without moving every `ParticipantId` — those sign-ins
  keep working and emit a throttled warning. Leave `userIdClaim` unset (or set it to `sub`/`oid`)
  for new configurations.

Two per-provider settings are consequently no longer read, though existing records keep them:
`requireSignedTokens` (always on) and `requireHttpsMetadata` (decided by `ASPNETCORE_ENVIRONMENT`).

**Rolling out the two switches:**

Both default to off because turning them on changes who can authenticate. To enable either one,
watch the logs for the warning it emits, fix each tenant it names, then set the flag.

| Switch | Warning it emits | Fix before enabling |
| --- | --- | --- |
| `Auth__RequireOidcAudience` | Provider declares no `expectedAudience` | Set `expectedAudience` on the provider. Until then, any token that issuer signed is accepted — including one minted for an unrelated application at the same identity provider. New configurations cannot be saved without one, so this warning only names tenants configured before that rule. |
| `Auth__StrictSubjectClaim` | Identity fell back to a claim users can change | Leave `userIdClaim` unset (defaults to `sub`/`oid`), or set it to a stable claim. Note that this changes the user id of anyone currently signing in through a fallback claim, orphaning their existing record — naming the claim they already resolve to keeps them on it. Do not set it to a mutable claim; that is refused at save time for new configurations. |

### Tenant membership on User API sign-in

A first-time User API sign-in records the caller as a member of the tenant they asked for. That
membership is created **approved** — no admin step — when the token was checked against the
provider's `expectedAudience`, because an audience proves the token was minted for this tenant's own
application, which is what makes holding one the tenant's own statement that the person belongs to
it.

When the provider declares no `expectedAudience`, the token was accepted on its issuer's signature
alone and could have been minted for an unrelated application at that issuer. The membership is then
created **pending** instead, waiting for an admin, and a throttled warning names the tenant. Setting
`expectedAudience` is what restores automatic approval.

The WebAPI console always creates a pending membership regardless: its tokens are validated against
the deployment-wide provider, so holding one says nothing about any particular tenant.

### Email collisions at sign-in

A user record is keyed on the provider subject (`sub` / `oid`, or the configured `userIdClaim`), and
a subject is only unique within one issuer. One person signing in through two directories therefore
has two records carrying the same address, which is expected rather than an error.

Sign-in refuses to provision a second account when the address is already held **at the same
provider**, or when either record's provider cannot be identified — there the address really does
name one account, and merging on it alone would hand over that account's access. A second account at
a genuinely different provider is allowed, except when the address belongs to a system
administrator, where the record is created disabled for an operator to review.

Leave `userIdClaim` unset (or set it to a stable claim) so the same person keeps the same account
across sessions.

Conversation threads may still be keyed by email: the User API accepts the account's stored email as
`participantId` even when the account id is the provider subject — unless the address is held by
more than one account, in which case it falls back to the subject.

See [`EMAIL_IDENTITY_RESOLUTION.md`](EMAIL_IDENTITY_RESOLUTION.md) for how identity and authority are
resolved when an address names several accounts, including the rules for system administrators.

### Certificate Validation Caching (Agent API)

The Agent API uses certificate-based authentication and caches validation results for performance:

```bash
# Idle window: an agent that keeps calling within this window never revalidates (default: 2)
AgentApi__CertificateValidationCacheDurationMinutes=2

# Hard ceiling regardless of activity (default: 5)
AgentApi__CertificateValidationCacheMaxDurationMinutes=5

# Size per cache entry for eviction policy (default: 1)
AgentApi__CertificateValidationCacheEntrySize=1
```

**Security Notes:**

- Only successful certificate validations are cached
- Revoking a certificate and disabling the account both evict the entry directly
- The ceiling bounds how long a disabled agent survives on the server instances that did not
  handle the request that disabled it, since the cache is in-process. It matches the token and
  role caches so that every cached authorization decision has the same worst case
- Set the ceiling below the idle window and it is ignored, with a warning: the idle window becomes
  the ceiling, because an entry cannot outlive the point at which it is discarded anyway
- Uses the same global cache size limit as token validation
- Failed validations always trigger fresh validation

### SSL and Security Settings

For production environments:

```bash
# Ensure HTTPS is required (set to true in production)
Auth__RequireHttpsMetadata=true

```

### Development vs Production

**Development Settings:**

```bash
ASPNETCORE_ENVIRONMENT=Development
Auth__RequireHttpsMetadata=false  # Allow HTTP for local development
```

**Production Settings:**

```bash
ASPNETCORE_ENVIRONMENT=Production
Auth__RequireHttpsMetadata=true   # Require HTTPS
```

## Multi-Tenant Support

All providers support multi-tenant configurations through custom claims:

```bash
# Custom claim type for tenant information
AuthProvider__TenantClaimType=https://your-domain.com/tenants
```

**How it works:**

1. The authentication provider includes tenant information in JWT tokens
2. The system extracts tenant IDs from the custom claim
3. User roles are loaded based on the tenant context
4. API endpoints validate tenant access automatically

## Configuration Validation

The system validates configuration at startup and will throw detailed error messages if required settings are missing:

- **Auth0**: Requires `Domain` and `Audience`
- **Azure B2C**: Requires `TenantId`, `Audience`, `JwksUri`, and `Issuer`
- **Keycloak**: Requires `AuthServerUrl` and `Realm`

## Security Best Practices

1. **Use HTTPS in production** - Always require HTTPS for token validation
2. **Rotate secrets regularly** - Change Management API credentials periodically
3. **Limit token lifetime** - Configure appropriate token expiration times
4. **Validate audiences** - Ensure tokens are intended for your API
5. **Monitor authentication logs** - Track failed authentication attempts
6. **Use strong certificates** - Implement proper certificate management

## Migration Between Providers

To migrate from one provider to another:

1. Set up the new provider configuration
2. Update the `AuthProvider__Provider` setting
3. Migrate user data if necessary
4. Update frontend authentication flows
5. Test thoroughly before production deployment

The unified interface ensures that API endpoints don't need to change when switching providers.
