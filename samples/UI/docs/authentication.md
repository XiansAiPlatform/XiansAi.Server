# Authentication

AdminApi supports two ways to authenticate, and you can build a UI on either one, or
both:

1. **API key**: a shared service credential. Simple, but every call acts as one
   identity, not as any particular signed-in person.
2. **OIDC ID token, no API key**: your user signs in through your identity provider,
   and their own identity, tenant membership, and role decide what they can do. This is
   the recommended path for a UI serving signed-in humans.

## Option A: API key

```
Authorization: Bearer sk-your-admin-api-key
```

**Header only.** Query parameters (`?apikey=`) are not supported, since those can leak
into proxy/CDN logs and browser history. A caller authenticated this way must hold
`SysAdmin` or `TenantAdmin`.

### Attributing actions to a signed-in person

If your UI holds a shared API key but still wants audit logs to reflect which person
triggered an action, send their identity alongside the key:

```
Authorization: Bearer sk-your-admin-api-key
X-On-Behalf-Of: auth0|64f2ab...
```

This is an assertion by whoever holds the key, not verified end-user authentication.
Treat admin API keys as backend credentials, not something a browser should hold
directly, if you use this pattern.

## Option B: OIDC ID token

Any AdminApi client, a custom UI, a CLI, another service, may authenticate with no
API key at all, presenting a verified OIDC ID token in an `X-User-Token` header:

```
X-User-Token: <id_token>
```

Unlike the API key path, the caller may hold any tenant role: `TenantUser`,
`TenantParticipant`, `TenantParticipantAdmin`, `TenantAdmin`, or `SysAdmin`. An approved
membership in one tenant is enough to authenticate. Caller's permissions are
decided per-route by RBAC (see below), not by this authentication step.

**One current limitation:** the messaging and heartbeat endpoint groups
(`/tenants/{tenantId}/messaging/*`, `/tenants/{tenantId}/heartbeat`) forward the raw API
key downstream to agents via Temporal signals, so they are not reachable this way.
They still require an API key regardless of the caller's role. Everything else on
AdminApi, tenants, agents, templates, knowledge, workflows, schedules, tasks, secrets,
integrations, is reachable with just an ID token.

### Setting it up

The identity provider(s) AdminApi's keyless path trusts are configured at runtime
against a dedicated `admin-console` pseudo-tenant, separate from any real
tenant's own OIDC config. Each custom UI/IdP registers its own provider entry
independently, `SysAdmin`-only:

```bash
# Read a starting template
curl https://your-server.example.com/api/v1/admin/admin-console/oidc-config/template \
  -H "Authorization: Bearer sk-your-admin-api-key"

# Register your provider (can be called again any time to add/change providers, no restart)
curl -X PUT https://your-server.example.com/api/v1/admin/admin-console/oidc-config \
  -H "Authorization: Bearer sk-your-admin-api-key" -H "Content-Type: application/json" \
  -d '{
    "allowedProviders": ["my-idp"],
    "providers": {
      "my-idp": {
        "authority": "https://your-idp.example.com",
        "issuer": "https://your-idp.example.com",
        "expectedAudience": ["your-client-id"]
      }
    }
  }'
```

`GET`/`DELETE` on the same path read back or remove the configuration. Leaving it unset
keeps AdminApi exactly as it was before this feature existed where it need a API key and `X-User-Token`
simply fails validation.

With that in place, your UI's own OIDC login is standard: any client library, in any
language, can redirect to the authority, handle the callback, and obtain an ID token to
send as `X-User-Token`. See [examples/react-vite-tailwind](../examples/react-vite-tailwind/) for
a generic, config-driven setup using `oidc-client-ts`.

> **Azure AD specifically**: its `sub` claim is pairwise per app registration, so a
> token from your UI's own app registration won't resolve to the same user as a token
> from a different Azure AD app in the same tenant. If your platform has another Azure
> AD sign-in path already, match its `userIdClaim` (often `oid`) so both resolve to the
> same account. Also, register each Entra tenant your UI should support explicitly by
> its real issuer URL. There's no "accept any tenant" mode.

## RBAC: what a caller may actually do

Every AdminApi route is guarded by a named capability action (e.g.
`tenant.tasks.list`, `tenant.messaging.send`), each with a default set of roles allowed
to perform it. `SysAdmin` always passes. This is what decides what an OIDC-authenticated
`TenantUser` or `TenantParticipant` can reach, since authentication alone (option B
above) is deliberately broad.

The matrix is editable at runtime (`SysAdmin`-only, no redeploy needed) via
`GET`/`PUT /api/v1/admin/admin-console/capability-matrix/*`, so an operator can widen or
narrow what a role can do without a code change. The full, current list of actions and
their default roles lives in `Features/AdminApi/Auth/CapabilityActions.cs` in the server
repo. Don't hardcode assumptions about which roles can do what into your UI beyond what
you've confirmed for your deployment. Ask your server operator, or call the matrix
endpoint yourself if you hold `SysAdmin`.

## Tenant scoping

Most routes are `/tenants/{tenantId}/...`. A `SysAdmin` caller must always supply a
`tenantId` explicitly. There's no implicit home tenant to default to. Any other caller
defaults to their own tenant when they hold an approved role in exactly one.

## Enabling browser access (CORS)

If your UI calls AdminApi directly from a browser, ask your server operator to allow
your UI's origin in the server's CORS configuration. `Authorization`, `X-User-Token`,
and `X-Tenant-Id` are already in the default allowed header set, only the origin itself
needs adding.

## Next

[Calling AdminApi](integration-options.md)
