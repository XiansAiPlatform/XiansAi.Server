# XiansAi User API Documentation

Direct HTTP integration guide for XiansAi backend agents without SDKs.

## Quick Reference

| Method | Endpoint | Use Case |
|--------|----------|----------|
| **REST** | `/api/user/rest/*` | Fire-and-forget, synchronous conversations |
| **WebSocket** | `/ws/chat` | Real-time bidirectional communication |
| **SSE** | `/api/user/sse/events` | Server-sent notifications |

## Authentication

All endpoints require authentication via:

### API Key (Query Parameter)
```
?apikey=sk-your-api-key&tenantId=your-tenant-id
```

### JWT Token (Authorization Header)
```
Authorization: Bearer your-jwt-token
```
Plus `tenantId` query parameter.

## Endpoints

- [REST API](./rest-api.md) - HTTP endpoints for send, converse, and history
- [WebSocket API](./websocket-api.md) - Real-time bidirectional communication
- [SSE API](./sse-api.md) - Server-sent events for live notifications

## Message Types

- **Chat**: Text-based conversations (`type=Chat`)
- **Data**: Structured data exchange (`type=Data`) 
- **Handoff**: Control transfer (`type=Handoff`)

## Base URL Structure

```
https://your-server.com/api/user/rest/*    # REST endpoints
https://your-server.com/ws/*              # WebSocket endpoints  
https://your-server.com/api/user/sse/*    # SSE endpoints
``` 