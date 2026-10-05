# Calling AdminApi

AdminApi is plain, versioned REST, plus one Server-Sent Events stream for live
messages. There's no WebSocket surface here so your UI calls it directly with\
whatever HTTP client, and `EventSource`-equivalent, your language provides.

## REST

Every route lives under a versioned base path:

```
https://your-server.example.com/api/v1/admin/...
```

Versioning is URL-based (`/api/v1/`, `/api/v2/` once it exists), so don't assume a
route stays at `v1` forever, but don't build in speculative handling for versions that
don't exist yet either.

Both auth modes from [Authentication](authentication.md) work identically across every
route: `Authorization: Bearer <api-key>`, or `X-User-Token: <id_token>` with no key.

## SSE: one real-time stream

```
GET /api/v1/admin/tenants/{tenantId}/messaging/listen
```

Subscribes to live message events (new `Chat`/`Data`/`File`/`Handoff` messages) for one
agent activation and participant, over a standard SSE stream, with periodic heartbeat
events to keep the connection alive. This route currently requires an API key even
under OIDC auth (see [Authentication](authentication.md)).

As SSE just avoids the polling delay and the extra requests, if you don't need push updates, polling the equivalent history/list endpoints on an
interval works.


## Next

[Optional features](optional-features.md)
