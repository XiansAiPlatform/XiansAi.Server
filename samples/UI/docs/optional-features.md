# Optional Features

## Management operations

Each gated by its own capability action (see [Authentication](authentication.md)):

- **Tenants**: list/get/create/update/delete, per-tenant metadata, logo, theme,
  Temporal config, and OIDC config.
- **Agents, templates, knowledge**: browse and manage what your agents are and know.
- **Agent activations & deployments**: create/update/activate/deactivate a running
  instance of an agent, promote a deployment to a template, manage worker deployment
  versions.
- **Workflows**: list/get/activate/cancel workflow runs, and stream their events.
- **Schedules**: list, inspect upcoming runs and history, pause/resume/delete.
- **Metrics & stats**: usage stats, time series, categories.
- **Secrets vault**: create/list/fetch/update/delete tenant secrets.
- **App integrations & webhooks**: connect and manage external platform integrations.
- **Audit logs**: query who did what.

See the [AdminApi README](../../../XiansAi.Server.Src/Features/AdminApi/README.md)
in the server repo for the complete endpoint list.

## Conversational and human-in-the-loop surface

- **Messaging**: send/listen/history/topics, plus file send/download, addressed to a
  specific agent activation and participant. Requires an API key even under OIDC
  auth (see [Authentication](authentication.md)); it isn't reachable with only an ID
  token today.
- **Tasks**: list/get a human-in-the-loop work item, update its draft or metadata, and
  perform an action on it (e.g. approve/reject). Fully reachable under OIDC auth with no
  API key. A `TenantParticipant` can act on their own tasks, `TenantAdmin` can see the
  tenant's full list.

## Message types

- `Chat`: free-text conversation.
- `Data`: structured JSON payload for machine-to-machine or app-specific exchanges. The
  server doesn't know or care about the payload shape, it's just structured JSON your
  agent and your UI agree on.
- `File`: one or more file attachments (up to 5 files, 10MB each, 20MB combined per
  message), sent as base64 content or, for outbound messages an agent sends back,
  referenced by `fileId` rather than inlined.
- `Handoff`: signals a control transfer, e.g. to a human agent or another workflow.
- `Reasoning` / `Tool`: streamed agent-internals messages (the agent's reasoning steps,
  or a tool call in progress).

## RBAC customization

The capability matrix deciding which roles can do what is editable at runtime, no
redeploy, via `GET`/`PUT /api/v1/admin/admin-console/capability-matrix/*`
(`SysAdmin`-only). If your UI needs a role to be able to do something its default
doesn't allow, ask the server operator (sys admin) for a configuration change.

## Deciding what to add

Start with the required core (see [Overview](overview.md)) against whichever management
or task endpoints your product needs first, then layer in messaging, files, or RBAC
customization as you you need them. The [examples/react-vite-tailwind](../examples/react-vite-tailwind/)
sample deliberately implements only the OIDC login and the tasks surface, so you can see the
minimum working shape before deciding what to add.
