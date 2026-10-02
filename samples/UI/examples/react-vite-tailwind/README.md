# Example: React 19 + Vite + Tailwind CSS

A minimal, working reference implementation of the concepts described in
[../../docs/](../../docs/). A signed-in user authenticates through an identity
provider, and the application calls AdminApi's task and agent endpoints under that
user's own identity, with no API key involved.

## Scope

Included (the required core, per [Overview](../../docs/overview.md)):

- Configuration driven entirely by environment variables. No server or identity
  provider is hardcoded.
- Standard OIDC login (`oidc-client-ts`), sending the resulting ID token as
  `X-User-Token` on every request. No API key is used anywhere in this example.
- A nav bar and a 3-tab sidebar (Tenant, Agents & Activations, Tasks):
  - **Tenant**: a free-text tenant id input, validated against AdminApi before it
    becomes active.
  - **Agents & Activations**: lists agents deployed in the active tenant and their
    activations. The two underlying endpoints require different minimum roles
    (`tenant.agentDeployments.list` excludes a plain `TenantParticipant`;
    `tenant.agentActivations.list` is open to every tenant role), so each section
    shows its own content or its own permission-denied message independently.
  - **Tasks**: AdminApi's human-in-the-loop tasks, one of the few conversational
    surfaces fully reachable under this authentication mode (see
    [Authentication](../../docs/authentication.md)).

  Both the Agents & Activations and Tasks tabs are always shown enabled. This app has
  no way to know a signed-in user's role or permissions in advance (the one endpoint
  that would answer that, `GET /participants/{email}`, is `SysAdmin`-only and
  non-delegable even to query your own info), so a lacking permission surfaces as a
  permission-denied message in that tab's content once opened, not as a disabled
  sidebar button. This is a deliberate tradeoff of this architecture, not an
  oversight.

Not included (see [Optional features](../../docs/optional-features.md) for how to add
these): the messaging/chat surface, which currently still requires an API key even
under OIDC authentication and so does not fit a keyless example; a design system. Each
is additive to this structure rather than a rewrite of it.

## Setup

A working browser-based OIDC login depends on configuration in three separate
systems: the identity provider, the Xians Server's own OIDC trust configuration, and
the platform user record. Each step below addresses one of these; omitting a step
produces a specific, documented error.

### 1. Prerequisites

- A running Xians Server reachable from this application (see
  [Connecting to a server](../../docs/connecting-to-server.md) for the three
  deployment paths).
- An Admin API key (`SysAdmin` or `TenantAdmin`), required to complete the
  registration steps below. The application itself never uses this key as it is used
  only from a terminal during setup.

### 2. Register an application with the identity provider

Any standard OIDC provider is supported (see
[Authentication](../../docs/authentication.md) for the full list). The steps below
use **Microsoft Entra ID** as a worked example, since its configuration is the most
likely to require troubleshooting:

1. In the Azure Portal, go to **App registrations** → **New registration**.
2. Record the **Application (client) ID** and the **Directory (tenant) ID**; both are
   required in later steps.
3. Under **Authentication** → **Add a platform**, select **Single-page application**,
   not **Web**. A redirect URI registered under "Web" causes Azure to reject this
   application's token exchange with `AADSTS9002326: Cross-origin token redemption is
   permitted only for the 'Single-Page Application' client-type`, since this example
   has no backend and redeems the authorization code directly from the browser.
4. Add the redirect URI `http://localhost:5173/auth/callback`. This value must stay
   synchronized with the development server port configured in `vite.config.ts` (see
   Step 7); if that port changes, the registered URI no longer matches, and login
   fails with `AADSTS50011` (redirect URI mismatch).
5. Select an authority consistent with how the application registration is scoped:
   - **Single tenant** (the common case for an internal tool):
     `https://login.microsoftonline.com/<tenant-id>/v2.0`.
   - **Multi-tenant**: `https://login.microsoftonline.com/common/v2.0`, valid only if
     the application registration's "Supported account types" setting permits other
     tenants. A single-tenant registration used with `/common` fails with
     `AADSTS700016: Application ... was not found in the directory ...`.
   - In either case, use the **v2.0** endpoint (the `/v2.0` suffix). The bare
     `https://login.microsoftonline.com/<tenant-id>` path serves v1-style metadata,
     whose issuer (`https://sts.windows.net/<tenant-id>/`) does not match the value
     registered server-side in the next step.

### 3. Register the provider with the Xians Server

AdminApi's keyless OIDC path trusts only providers explicitly registered against its
`admin-console` configuration (see [Authentication](../../docs/authentication.md),
Option B). Using the Admin API key from Step 1:

```bash
# Optional: inspect the current configuration first
curl http://localhost:5005/api/v1/admin/admin-console/oidc-config \
  -H "Authorization: Bearer <admin-api-key>"

# Register or replace the provider
curl -X PUT http://localhost:5005/api/v1/admin/admin-console/oidc-config \
  -H "Authorization: Bearer <admin-api-key>" -H "Content-Type: application/json" \
  -d '{
    "allowedProviders": ["entra"],
    "providers": {
      "entra": {
        "authority": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "issuer": "https://login.microsoftonline.com/<tenant-id>/v2.0",
        "expectedAudience": ["<client-id>"]
      }
    }
  }'
```

The `issuer` value must match the token's `iss` claim exactly, and the provider name
(`entra` above) must appear in `allowedProviders`; otherwise every token is rejected
with `"Invalid user token"` (logged server-side as `"Provider not allowed for
tenant"`). Update this configuration whenever the frontend's authority changes.

### 4. Confirm the signed-in account is a registered platform user

A valid, trusted ID token is not sufficient on its own: the signed-in account must
also have an existing user record on the platform, approved as a member of the tenant
under test. If it does not, the application returns `"User is not registered on this
platform"` or `"User is not an approved member of any tenant"` instead of a task
list. Request that the server operator create or approve this membership if it does
not already exist.

### 5. Confirm CORS allows this origin and the `X-User-Token` header

Because this example calls AdminApi directly from the browser, the server's CORS
policy must allow both this application's origin and the `X-User-Token` header. If
either is missing, the browser console reports an error similar to:

```
Access to fetch at '.../tasks' from origin 'http://localhost:5173' has been blocked by
CORS policy: Request header field x-user-token is not allowed by
Access-Control-Allow-Headers in preflight response.
```

Request that the server operator add `http://localhost:5173` to the server's allowed
CORS origins and confirm `X-User-Token` is present in its allowed headers list before
proceeding further.

### 6. Configure `.env.local`

```bash
npm install
cp .env.example .env.local
```

Populate the following values:

```env
VITE_XIANS_SERVER_URL=http://localhost:5005
VITE_XIANS_TENANT_ID=your-tenant-id

VITE_OIDC_AUTHORITY=https://login.microsoftonline.com/<tenant-id>/v2.0
VITE_OIDC_CLIENT_ID=<client-id>
VITE_OIDC_SCOPE=openid profile email
```

`VITE_OIDC_AUTHORITY` and `VITE_OIDC_CLIENT_ID` must match the values registered
server-side in Step 3 exactly. `VITE_XIANS_TENANT_ID` is optional: it only seeds the
Tenant tab's input with an initial value; the active tenant is otherwise switched at
runtime, not fixed by this file. See [Authentication](../../docs/authentication.md)
for the general meaning of each variable, beyond this Entra ID example.

### 7. Run the application

```bash
npm run dev
```

`vite.config.ts` pins the development server to port `5173` (`strictPort: true`) so
that the redirect URI registered in Step 2 remains valid across restarts, rather than
Vite silently selecting port `5174` or higher when `5173` is unavailable. If the
development server fails to start because the port is in use, free the port rather
than allowing Vite to select a different one; otherwise, a new redirect URI must be
registered.

Open `http://localhost:5173`, sign in, and the Tenant tab should be displayed first.
Enter a tenant id and select "Go to tenant" to reach the Agents & Activations and
Tasks tabs.

## Troubleshooting


| Error | Cause | Resolution |
|---|---|---|
| `AADSTS50011`: redirect URI mismatch | The redirect URI on file with the identity provider does not match the one sent by the application, often due to a changed development server port | Add the exact URI shown in the error to the application registration, or correct the port generating it |
| `AADSTS700016`: application not found in directory | The authority is `/common` (or the wrong tenant), but the application registration is single-tenant | Use the specific tenant's authority, or configure the application registration as genuinely multi-tenant |
| `AADSTS9002326`: cross-origin token redemption only for SPA client-type | The redirect URI is registered under the "Web" platform instead of "Single-page application" | Move the redirect URI to a Single-page application platform block |
| Browser console: CORS blocked, `x-user-token` not allowed | The server's CORS configuration does not allow this header, this origin, or both | Request that the server operator add the origin and `X-User-Token` to the server's CORS configuration |
| `401 {"error":"Unauthorized","message":"Invalid user token"}` | No provider is registered for `admin-console`, or its `issuer`/`expectedAudience` does not match the token | Register or update the provider (Step 3) so that `issuer` matches the token's `iss` claim exactly and `expectedAudience` includes the client ID |
| `"User is not registered on this platform"` | The signed-in account has no platform user record | Request that a platform account be created for this identity |
| `"User is not an approved member of any tenant"` | A platform user exists but has no approved tenant membership | Request approval for that membership, or confirm `VITE_XIANS_TENANT_ID` refers to the correct tenant |
| Tenant tab: `"Tenant ID does not match any tenant where the user is an approved member"` | The typed tenant id is misspelled, or the signed-in account has no approved membership in it | Confirm the tenant id, or request approval for that membership |
| Agents & Activations: Deployments section shows "You don't have access to this" | The signed-in user's role is `TenantParticipant`, which `tenant.agentDeployments.list` excludes by default | Expected for that role; the Activations section still populates. Request `TenantParticipantAdmin`/`TenantUser`/`TenantAdmin` if deployment visibility is required |

## Token storage

`useAuth.ts` uses the `oidc-client-ts` default, which keeps the signed-in user
(including the ID token sent as `X-User-Token`) in `sessionStorage`. Any script running
on the page can read it, so serve the app with a strict Content-Security-Policy.

To keep tokens out of web storage, pass an in-memory `userStore` to `UserManager`
(`new WebStorageStateStore({ store: new InMemoryWebStorage() })`). Leave `stateStore` at
its default, since the login state must survive the redirect to the identity provider.
The trade-off is that the session is lost on every page reload, which sends the user back
through the identity provider, and token renewal becomes your responsibility.

## Token expiry

The sample does not enable `automaticSilentRenew`. Once the ID token expires mid-session,
requests fail with a 401 until the page is reloaded and the user re-authenticates. For
real applications, set `automaticSilentRenew: true` on the `UserManager` settings in
`oidcConfig.ts` instead of lengthening token lifetimes at the identity provider. Renewal
uses a refresh token if the provider issues one (request the `offline_access` scope).
Otherwise it uses a hidden iframe, which also needs a `silent_redirect_uri` page
registered as a redirect URI with the provider.

## Code reference

| File | Demonstrates |
|---|---|
| [`src/config.ts`](src/config.ts) | Reading server and OIDC configuration from environment variables |
| [`src/auth/useAuth.ts`](src/auth/useAuth.ts) | OIDC login (`oidc-client-ts`), exposing an ID token, name, and email for AdminApi/the UI |
| [`src/adminApi/client.ts`](src/adminApi/client.ts) | A thin `fetch` wrapper that calls AdminApi with `X-User-Token`; no SDK is used |
| [`src/adminApi/useAdminApiClient.ts`](src/adminApi/useAdminApiClient.ts) | Recreating the AdminApi client when the active tenant changes |
| [`src/tenant/useTenantSwitcher.ts`](src/tenant/useTenantSwitcher.ts), [`TenantSwitcher.tsx`](src/tenant/TenantSwitcher.tsx) | Validating a typed tenant id against AdminApi before switching to it |
| [`src/agents/useAgentsAndActivations.ts`](src/agents/useAgentsAndActivations.ts), [`AgentsAndActivations.tsx`](src/agents/AgentsAndActivations.tsx) | Listing agent deployments and activations, with independent permission handling per endpoint |
| [`src/tasks/useTasks.ts`](src/tasks/useTasks.ts), [`TaskList.tsx`](src/tasks/TaskList.tsx) | Listing tasks and performing an action on one |
| [`src/shell/`](src/shell/) | The nav bar, sidebar, and the shared `PermissionDenied`/`NeedsTenantNotice` content states |

The frontend framework (React) and styling approach (Tailwind CSS) may be replaced
freely. Only `config.ts`, `useAuth.ts`, and `adminApi/` contain logic specific to
Xians.
