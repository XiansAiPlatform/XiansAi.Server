# Outbound Event Webhooks

The server can notify external listeners when significant events happen (for example when a user
is added through the Admin API). Listener URLs are configured in application settings, and each
event is delivered reliably even when several server instances run at once.

> This is different from the inbound webhooks documented in [WEBHOOKS.md](WEBHOOKS.md), which
> receive HTTP calls and drive Temporal workflow updates. This document covers **outbound**
> event notifications produced by the server.

## How it works

```
Business service ──PublishAsync──▶ webhook_deliveries (Mongo outbox)
                                          │
                     WebhookDispatcherService (runs on every instance)
                                          │ atomically claims each row (lease)
                                          ▼
                                  HTTP POST to listener URL
```

1. When an event occurs, a service calls `IWebhookEventPublisher.PublishAsync(...)`.
2. The publisher expands the event into one row per matching subscription (snapshotting the
   tenant/actor context and serializing the payload on the caller's thread — fast, no I/O), then
   inserts them into the `webhook_deliveries` collection **in the background**. `PublishAsync`
   returns as soon as the rows are built, so the outbox write (and its retries) never adds latency
   to the business operation. Publishing never throws to the caller; a failed insert is logged and
   the event is dropped (best-effort). This trades a small durability window for the guarantee that
   a slow or unavailable database can neither delay nor fail the originating operation.
3. `WebhookDispatcherService` (a `BackgroundService`) polls the outbox, atomically claims a batch
   of due rows via `FindOneAndUpdate` (setting a short lease), and POSTs each payload.
4. On a `2xx` response the row is marked `Delivered`. On failure it is retried with exponential
   backoff up to `MaxAttempts`, after which it is marked `Failed` (kept for inspection).

Because the claim is atomic and lease-based, exactly one instance delivers each row, and a row
abandoned by a crashed instance is automatically reclaimed once its lease expires.

## Configuration

Bind under the `Webhooks` section (env var form shown; `.` maps to `__`):

| Setting | Default | Description |
| --- | --- | --- |
| `Webhooks:Enabled` | `false` | Master switch. When false nothing is queued and the dispatcher does not run. |
| `Webhooks:PollIntervalSeconds` | `5` | Dispatcher poll interval. |
| `Webhooks:RequestTimeoutSeconds` | `10` | Per-request HTTP timeout. |
| `Webhooks:MaxAttempts` | `5` | Attempts before a delivery is marked `Failed`. |
| `Webhooks:LeaseSeconds` | `60` | Claim lease duration (crash recovery window). |
| `Webhooks:BatchSize` | `20` | Max deliveries processed per poll. |
| `Webhooks:AllowInsecureHttp` | `false` | Allow `http://` listener URLs (otherwise HTTPS is required). |
| `Webhooks:Subscriptions[]` | `[]` | Listener definitions (see below). |

Each subscription:

| Field | Description |
| --- | --- |
| `Name` | Stable identifier, persisted on delivery rows. |
| `Url` | Destination that receives the POST. |
| `Secret` | Optional. When set, an HMAC-SHA256 signature of the body is sent. |
| `EventTypes` | Event types to receive. Empty or `["*"]` means all events. |
| `Enabled` | Whether the subscription is active. |

### Example (appsettings.json)

```jsonc
"Webhooks": {
  "Enabled": true,
  "Subscriptions": [
    {
      "Name": "crm-listener",
      "Url": "https://example.com/hooks/xians",
      "Secret": "shared-secret-for-hmac",
      "EventTypes": ["user.created", "user.tenant.added", "tenant.created", "user.role.changed"],
      "Enabled": true
    }
  ]
}
```

## Request format

The dispatcher sends `POST <Url>` with `Content-Type: application/json` and headers:

| Header | Meaning |
| --- | --- |
| `X-Xians-Event` | Event type (e.g. `user.created`). |
| `X-Xians-Event-Id` | Logical event id (identical across all subscription deliveries). |
| `X-Xians-Delivery` | Unique id of this delivery row. |
| `X-Xians-Attempt` | Attempt number (1-based). |
| `X-Xians-Timestamp` | Unix time (seconds) when the request was signed; bound into the signature. |
| `X-Xians-Signature` | Present only when a `Secret` is configured: `sha256=<hmac over "{timestamp}.{body}">`. |

Body (envelope):

```json
{
  "eventType": "user.created",
  "eventId": "0f4c...",
  "tenantId": "acme",
  "occurredAt": "2026-07-01T05:00:00Z",
  "actor": {
    "userId": "admin@acme.com",
    "userType": "UserApiKey",
    "tenantId": "acme",
    "roles": ["TenantAdmin"]
  },
  "data": {
    "userId": "…",
    "email": "…",
    "name": "…",
    "tenantId": "acme",
    "role": "TenantUser"
  }
}
```

The `actor` object identifies who triggered the event (from the authenticated request context)
and is `null` for system-originated events.

## Auditing

Each delivery row in `webhook_deliveries` also records audit fields for querying inside the
database, independent of the listener:

| Field | Description |
| --- | --- |
| `actor_user_id` | The user who triggered the event (indexed with `created_at` for per-user audit queries). |
| `actor_user_type` | How that user authenticated (e.g. `UserApiKey`). |
| `tenant_id` | The event's target tenant. |
| `event_type`, `event_id` | The event and its logical id. |
| `created_at` | When the event was queued. |
| `status`, `attempt_count`, `delivered_at`, `last_error` | Delivery outcome. |

### Verifying the signature

1. Read the `X-Xians-Timestamp` header and reject the request if it is too old (e.g. more than a
   few minutes) to defend against replay.
2. Compute `HMAC-SHA256(secret, "{timestamp}.{rawRequestBody}")`, hex-encode it, and compare it
   (constant-time) with the value after `sha256=` in `X-Xians-Signature`.

## Transport hardening

- Listener URLs must use **HTTPS**. Set `Webhooks:AllowInsecureHttp=true` only for trusted
  internal listeners on plain HTTP (payloads may contain PII).
- The dispatcher **does not follow redirects** (a `3xx` is treated as a delivery failure). This
  prevents a compromised listener from redirecting the request toward internal endpoints.
- The dispatcher does not read the response body, sends no cookies, and does not decompress
  responses, limiting the blast radius of a hostile or misbehaving listener.
- Server TLS certificates are validated normally; there is no option to bypass validation.

## Delivery semantics

Delivery is **at-least-once**. A listener may occasionally receive the same event more than once
(for example if it returns `2xx` after a network timeout). Listeners should deduplicate using
`eventId`.

## Currently emitted events

The full catalog of event type constants lives in `WebhookEventTypes`
(`Shared/Data/Models/WebhookDelivery.cs`). Events are published from shared services (so they fire
regardless of whether the action came via the Admin API, Agent API or Web API) wherever possible.

High-volume telemetry — per-message conversation writes, agent logs, activity history, usage
metrics, ephemeral cache writes and heartbeats — is intentionally **not** tracked, to avoid
overwhelming listeners.

### Tenants

| Event type | Emitted when |
| --- | --- |
| `tenant.created` | A new tenant is created. |
| `tenant.updated` | A tenant profile, theme or logo is updated. |
| `tenant.enabled` / `tenant.disabled` | A tenant's enabled flag is toggled. |
| `tenant.deleted` | A tenant is deleted. |
| `tenant.oidc.updated` | A tenant's OIDC config is created or updated. |
| `tenant.oidc.deleted` | A tenant's OIDC config is deleted. |

### Users

| Event type | Emitted when |
| --- | --- |
| `user.created` | A new user account is created. |
| `user.tenant.added` | A user is granted membership in a tenant. |
| `user.tenant.removed` | A user's tenant membership is removed (also when their last role is removed). |
| `user.updated` | A user's name/email is updated (tenant participant or global). |
| `user.approved` / `user.unapproved` | A user's tenant membership approval changes. |
| `user.role.changed` | A tenant role is added to a user. |
| `user.role.removed` | A tenant role is removed from a user. |
| `user.sysadmin.granted` / `user.sysadmin.revoked` | The system-admin flag is toggled. |
| `user.enabled` / `user.disabled` | A user account is enabled/locked out. |

### Agents, deployments, templates and flow definitions

| Event type | Emitted when |
| --- | --- |
| `agent.registered` | An agent is registered for the first time (Agent API). |
| `agent.deleted` | An agent and its dependent resources are deleted (Admin or Agent API). |
| `agent.deployment.updated` | An agent deployment's config is updated (Admin API). |
| `agent.ownership.transferred` | Agent ownership is transferred to another user. |
| `agent.template.deployed` | A system template agent is deployed into a tenant. |
| `template.updated` / `template.deleted` | A system-scoped template agent is updated/deleted. |
| `flow.definition.created` / `flow.definition.updated` | A workflow definition is registered or changes hash. |

### Activations

| Event type | Emitted when |
| --- | --- |
| `activation.created` / `activation.updated` | An activation is created/updated. |
| `activation.activated` / `activation.deactivated` | An activation's workflows are started/stopped. |
| `activation.deleted` | An activation is deleted. |

### Knowledge, secrets, keys and certificates

| Event type | Emitted when |
| --- | --- |
| `knowledge.created` / `knowledge.updated` / `knowledge.deleted` | Knowledge items change (Admin or Agent API). |
| `secret.created` / `secret.updated` / `secret.deleted` | Vault secrets change (values are never included). |
| `apikey.created` / `apikey.revoked` / `apikey.rotated` | API keys change. |
| `certificate.created` / `certificate.revoked` | Client certificates are issued/revoked. |

### App integrations

| Event type | Emitted when |
| --- | --- |
| `integration.created` / `integration.updated` / `integration.deleted` | An app integration changes. |
| `integration.enabled` / `integration.disabled` | An app integration is enabled/disabled. |
| `integration.webhook.created` | A builtin webhook integration is created (deletion emits `integration.deleted`). |

## Adding a new event

1. Add a constant to `WebhookEventTypes` (`Shared/Data/Models/WebhookDelivery.cs`).
2. Inject `IWebhookEventPublisher` into the relevant service and call `PublishAsync(...)` after
   the operation succeeds.

## Implementation

- Options: `Shared/Configuration/Options/WebhooksOptions.cs`
- Model / outbox document: `Shared/Data/Models/WebhookDelivery.cs`
- Repository (outbox + atomic claim): `Shared/Repositories/WebhookDeliveryRepository.cs`
- Publisher: `Shared/Services/WebhookEventPublisher.cs`
- Dispatcher: `Shared/Services/WebhookDispatcherService.cs`
- Indexes: `mongodb-indexes.yaml` (`webhook_deliveries`)
