# Signaling Agent Workflows

Signals allow you to communicate with running Agent workflows, sending data or instructions to influence workflow execution. This document explains how to use the Agent workflow signaling API.

## Authentication

All requests to the Agent API require certificate-based authentication:

1. Download your AppServer API key from the Xians Portal settings page
2. Include this certificate in the `X-Client-Cert` HTTP header with each request (base64 encoded)

The server will validate that:

- The certificate is present and properly formatted
- It was issued by the Xians root certificate authority
- The tenant and user information in the certificate is valid
- The requested Workflow belongs to the user's tenant

## API Reference

### Signal a Workflow

Send a signal to a running Agent workflow to provide data or instructions.

**Endpoint:** `POST https://api.xians.ai/api/agent/signal`

**Headers:**

- `Content-Type: application/json`
- `X-Client-Cert: <base64-encoded-certificate>`

**Request Body:**

```json
{
  "workflowInstanceId": "string",  // ID of the running workflow
  "signalName": "string",          // Name of the signal to send
  "payload": {                     // Optional data payload to send with the signal
    // Any JSON object
  }
}
```

**Response:**

- `200 OK`: Signal was successfully delivered
- `400 Bad Request`: Invalid request format or missing parameters
- `401 Unauthorized`: Authentication failed (invalid certificate)
- `503 Service Unavailable`: The workflow engine is not available
- `500 Internal Server Error`: An unexpected error occurred

**Response Body:** 

The response will include details about the signal delivery status.

## Example

```bash
# Send a signal to a workflow
curl -X POST https://api.xians.ai/api/agent/signal \
  -H "Content-Type: application/json" \
  -H "X-Client-Cert: $CERT_BASE64" \
  -d '{
    "workflowInstanceId": "wf-new-purchases",
    "signalName": "approval",
    "payload": {
      "approved": true,
      "message": "LGTM, proceed with deployment"
    }
  }'
```

## Notes

- The workflow must be running for the specific signal you're sending
- Signals are delivered asynchronously and processed based on the workflow's execution state
