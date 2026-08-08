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
| `Auth__RequireOidcAudience` | Provider declares no `expectedAudience` | Set `expectedAudience` on the provider. Until then, any token that issuer signed is accepted — including one minted for an unrelated application at the same identity provider. |
| `Auth__StrictSubjectClaim` | Identity fell back to a claim users can change | Leave `userIdClaim` unset (defaults to `sub`/`oid`), or set it to a stable claim. Note that this changes the user id of anyone currently signing in through a fallback claim, orphaning their existing record — naming the claim they already resolve to keeps them on it. Do not set it to a mutable claim; that is refused at save time for new configurations. |

### Linking a Second Identity to an Existing Account

A user record is keyed on the provider subject that created it, and everything else — threads,
agents, keys, audit trails — is stored against that key. So when the same person arrives with a
different subject (a new identity provider, or a record created before subjects were used), a second
account would detach them from all of it. Sign-in therefore refuses to create one when the email
already belongs to somebody, and the two identities are joined by a link instead.

**Automatically, on a verified email:**

```bash
# Providers whose verified email addresses may attach a sign-in to the account already holding
# that address (default: https://accounts.google.com)
Auth__AutoLinkTrustedProviders__0=https://accounts.google.com
```

A sign-in is attached automatically only when the token carries an `email` claim, the provider says
the holder owns it (`email_verified`, or `xms_edov` for Entra), and the provider is named in this
list. Otherwise the sign-in is refused and an administrator has to link it.

The list is deployment configuration, and deliberately not part of tenant OIDC config, even though
only a SysAdmin may now write either. Configuring a provider is a separate decision from believing
what it says about identity: adding a partner's directory so their staff can sign in says nothing
about whether an address it emits that matches an existing account is the same person. The scopes
differ too — OIDC config governs one tenant, while the account an auto-link attaches to may be in
any tenant or be a SysAdmin's — and keeping this out of the API means a stolen SysAdmin token cannot
widen it.

Trusting a provider asserts that it verifies address ownership and that no third party can make it
say otherwise. That holds for Google. It does **not** hold for a multi-tenant Microsoft endpoint
(`/common`, `/organizations`, `/consumers`), where any directory in the world can issue tokens and
its administrators choose their users' email addresses — this is the nOAuth account-takeover
pattern, and such entries are refused at startup with an error in the log. A single Entra tenant
(`https://login.microsoftonline.com/<tenant-id>/v2.0`) may be trusted, since that names one
directory whose administrators you have decided to rely on.

**Providers that verify addresses but do not say so (Azure AD B2C):**

```bash
# Providers whose email is accepted with no verification claim in the token, because you know the
# directory verifies addresses itself (default: none)
Auth__AutoLinkProvidersWithoutVerifiedEmailClaim__0=https://contoso.b2clogin.com/contoso.onmicrosoft.com/v2.0
```

B2C issues its address as `emails` and sends no verification claim at all, so the trusted list alone
can never match one of its sign-ins however it is set — every returning user would need an
administrator. Naming a provider here supplies your assertion in place of the provider's. It also
trusts the provider, so it does not need to appear in both lists, and the same startup checks apply.

Only assert this where every address in the directory got there under control you can account for:
an invite-only or admin-provisioned directory, or one whose sign-up proves the person owns the
address (B2C local accounts verify by one-time passcode). It is **not** safe where a person can
bring an address in from elsewhere unchecked — self-service sign-up federated to social providers is
the case to watch, since the address comes from Google or Facebook and B2C may not re-verify it.
Where that is possible, whoever supplies an address takes over the account already holding it.

Links made this way are logged as attached "on an email match this deployment vouches for without a
verification claim", so an audit can tell them apart from ones a provider verified.

**Manually, as a System Admin:**

```bash
POST   /api/v1/admin/users/{userId}/identities   { "subject": "...", "authority": "https://..." }
DELETE /api/v1/admin/users/{userId}/identities?subject=...&authority=...
```

The subject is the `sub` claim from the person's token; the authority is the provider's issuer URL.
Linking is recorded against the administrator who did it. A subject that already owns an account
cannot be linked, because sign-in matches its own id first and the link would never resolve. If that
subject has an account you want to retire, delete it first, then link.

**What a link does and does not move:**

| Follows the linked account | Stays on the token's own subject |
| --- | --- |
| Tenant memberships and roles, so access is decided by one record | The conversation participant id |
| `LoggedInUser`, so records are attributed to that account | |

Conversation identity is deliberately left behind. Threads are keyed on
`(tenant, workflow, participant)`, and clients pass the participant id explicitly and may only name
their own — so moving it would both strand every existing thread and get the caller's next request
refused. The consequence is that a person with two linked subjects keeps two conversation histories,
while still being a single account everywhere access is concerned.

**Where links are stored:**

Links live in their own `user_linked_identities` collection, one document per link, with a unique
index over `(subject, authority)` that is what actually prevents an identity resolving to two
accounts. They are deliberately not embedded in the user document: most users have no links, and an
index that had to skip those documents would depend on `sparse`, which Azure Cosmos DB does not
implement — it counts a missing field as null toward the unique constraint, so only one user could
ever exist without a link and every subsequent sign-up would fail to provision.

Links were stored on the user document before v3.36.0. A Cosmos DB deployment upgrading from an
earlier build has to drop the old index by hand, or it will persist and reject every new user — see
[v3.36.0 — Cosmos DB Migration](migrations/v3.36.0-cosmos.md).

### Certificate Validation Caching (Agent API)

The Agent API uses certificate-based authentication and caches validation results for performance:

```bash
# Certificate validation cache duration in minutes (default: 10)
AgentApi__CertificateValidationCacheDurationMinutes=10

# Size per cache entry for eviction policy (default: 1)
AgentApi__CertificateValidationCacheEntrySize=1
```

**Security Notes:**

- Only successful certificate validations are cached
- Cache is automatically invalidated when a certificate is revoked
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
