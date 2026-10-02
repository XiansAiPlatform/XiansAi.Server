# Connecting to a Server

Before writing any UI code, you need from a running Xians Server:

- a **server URL**
- your **tenant ID** (unless you're a `SysAdmin` caller who supplies one per request)
- credentials to authenticate with: an admin API key, and/or an OIDC identity provider
  your users log in through (see [Authentication](authentication.md))

How you get these depends on which of the three deployment paths you're building
against. This guide doesn't cover how to setup up a Xians Server, only what a UI
developer needs from each path once one exists. All AdminApi routes live under the same
base path regardless of deployment: `https://your-server.example.com/api/v1/admin/...`.

## Path 1: Agentri (hosted SaaS)

The managed, multi-tenant Xians.ai offering at [agentri.ai](https://agentri.ai).

- **Server URL**: provided by the Agentri platform for your tenant.
- **Tenant ID / API key**: obtained from your tenant's settings page in
  [Agent Studio](https://studio.agentri.ai), or issued directly to your account if you
  authenticate via OIDC instead (see [Authentication](authentication.md)).
- Identity provider: your tenant admin registers which provider(s) AdminApi's
  ID-token-only auth trusts for your organization; ask them which one(s) are enabled.

## Path 2: Community Edition (self-hosted)

The open-source, self-hosted distribution that bundles the server with its dependencies
(Temporal, MongoDB/Postgres, Keycloak, Agent Studio) via Docker Compose.

- **Server URL**: whatever host/port the Community Edition deployment exposes AdminApi
  on (defaults to a local address unless the operator has put it behind a domain).
- **Tenant ID / API key**: on a fresh deployment, the very first SysAdmin and API key
  are created by a one-time, anonymous bootstrap call (works exactly once, until any
  user exists):

  ```bash
  curl "https://your-server.example.com/api/v1/admin/bootstrap?email=you@example.com"
  # -> { "apiKey": "sk-Xnai-...", ... }
  ```

  After that, further tenants/API keys/OIDC providers are created through Agent Studio
  or AdminApi itself by whoever holds that first key.
- Identity provider: not pre-wired for AdminApi by default. the operator registers one
  (see [Authentication](authentication.md)) if signed-in users should call AdminApi
  directly instead of through a shared API key.

## Path 3: Your own Xians Server

An organization can run its own instance (on its own infrastructure, with its own choice
of identity provider, database, and scaling model) and expose AdminApi to its UIs however it chooses.

- **Server URL / Tenant ID / API key**: defined by whoever operates that instance.
  Get these from your platform/infra team.
- Identity provider: entirely the operator's choice, whether that's Auth0, Entra ID,
  Azure B2C, Keycloak, or API-key-only with no interactive login at all. See
  [Authentication](authentication.md).

## What your UI needs to store/configure

Regardless of path, your UI's configuration boils down to the same shape. A typical
environment-variable-driven setup looks like:

```env
# Where AdminApi lives
XIANS_SERVER_URL=https://your-server.example.com

# Which tenant this UI serves (omit only if every caller is a SysAdmin who supplies
# a tenantId per request)
XIANS_TENANT_ID=your-tenant-id

# Authenticate as a service
XIANS_API_KEY=sk-your-admin-api-key

# Or, authenticate per logged-in user via your identity provider instead
# (see authentication.md for provider-specific values)
OIDC_AUTHORITY=https://your-idp.example.com
OIDC_CLIENT_ID=your-client-id
```

Which of `XIANS_API_KEY` or the `OIDC_*` values you need depends on the authentication
model you pick, covered next.

## Next

[Authentication](authentication.md)
