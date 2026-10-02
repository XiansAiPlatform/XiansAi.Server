# Overview

A custom UI for Xians is any client application, web, mobile, desktop, or a backend
service acting on a user's behalf, that talks to AdminApi which is the versioned REST
surface (`/api/v1/admin/...`) Xians exposes for managing tenants, agents, templates,
knowledge, workflows, schedules, secrets, and integrations, plus a conversational layer
for chatting with agents and handling human-in-the-loop tasks.

## What a Xians UI has to do

Every custom UI, regardless of framework, performs the same five steps:

```
1. Know where the server is   →  a server URL, and usually which tenant you belong to
2. Prove who's calling        →  an admin API key, or your signed-in user's own OIDC identity
3. Call AdminApi               →  versioned REST, plus one SSE stream for live messages
4. Respect what your role allows →  RBAC decides what each action lets a caller do
5. Render the result            →  however fits your product
```


## The core concepts

- **Tenant**: the organization or workspace whose agents and data you're working with.
  Most AdminApi routes are scoped to one, `/tenants/{tenantId}/...`.
- **Agent**: a deployed AI agent definition within a tenant.
- **Activation**: one running, named instance of an agent, for example "Order Manager
  Agent - Remote Peafowl". Messaging and tasks are addressed to a specific activation.
- **Role**: what a signed-in caller is, resolved from the platform's own user record:
  `SysAdmin`, `TenantAdmin`, `TenantUser`, `TenantParticipant`, or
  `TenantParticipantAdmin`. See [Authentication](authentication.md).
- **Capability**: a named action AdminApi guards (e.g. `tenant.tasks.list`), with a
  default set of roles allowed to perform it. This is what actually decides what a
  caller may do, on top of authentication. See [Authentication](authentication.md).
- **Task**: a human-in-the-loop work item an agent has handed off for a person to
  review, draft, or act on (approve/reject/etc.).
- **Message**: the unit of agent conversation. Types include `Chat`, `Data`, `File`,
  and `Handoff`; see [Optional features](optional-features.md).

## End-to-end flow

```
 Your UI                                   Xians Server (AdminApi)
 ───────                                   ────────────────────────
   │  1. Authenticate (API key, or OIDC     │
   │     ID token via X-User-Token)         │
   ├────────────────────────────────────────▶
   │                                        │
   │  2. Call a REST endpoint, scoped to    │
   │     /tenants/{tenantId}/...            │
   ├────────────────────────────────────────▶
   │                                        │
   │            3. RBAC checks the caller's │
   │               role against the action  │
   │◀────────────────────────────────────────┤
   │                                        │
   │  4. Render the result, or subscribe to │
   │     the SSE stream for live updates    │
```

## Where to go next

- Not sure which Xians deployment you're building against? See [Connecting to a server](connecting-to-server.md)
- Need to authenticate your users, with or without an API key? See [Authentication](authentication.md)
- Wondering how to actually call AdminApi? See [Calling AdminApi](integration-options.md)
- Want to know what's required vs. optional? See [Optional features](optional-features.md)
- Want to see this as working code? See [examples/react-vite-tailwind](../examples/react-vite-tailwind/)
