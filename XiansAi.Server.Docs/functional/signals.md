# Signaling Agent Workflows

Signals allow you to communicate with running Agent workflows, sending data or instructions to influence workflow execution. This document explains how to use the Agent workflow signaling API.

## Authentication

All requests to the Agent API require token-based authentication:

1. Retrieve your AppServer API key from the Xian AI Server portal.
2. Include the following header with each request:

   ```
   Authorization: Bearer {APP_SERVER_API_KEY}
   ```

The server will validate that:

- The API key is present and correctly formatted
- The workflow belongs to the correct tenant

## API Reference

### Signal a Workflow

Send a signal to a running Agent workflow to provide data or instructions.

**Endpoint:** `POST https://api.xians.ai/api/agent/signal`

**Headers:**

- `Content-Type: application/json`
- `Authorization: Bearer {APP_SERVER_API_KEY}`

**Request Body:**

> **Note:** The `signalName` property in the request body must exactly match the function name of the signal defined in your workflow.

```json
{
  "workflowId": "string",  // ID of the running workflow
  "signalName": "string",          // Must match the signal function name in your workflow
  "payload": {                     // Optional data payload to send with the signal
    // Any JSON object
  }
}
```

**Response:**

- `200 OK`: Signal was successfully delivered
- `400 Bad Request`: Invalid request format or missing parameters
- `401 Unauthorized`: Authentication failed (invalid API key)
- `503 Service Unavailable`: The workflow engine is not available
- `500 Internal Server Error`: An unexpected error occurred

**Response Body:** 

The response will include details about the signal delivery status. Here is a sample response body for a request.

```json
{
    "message": "Signal sent successfully",
    "workflowId": "ID-1123145",
    "signalName": "ReceiveEmail"
}
```

## Defining a Signal in a Workflow

When designing your workflow, you can define signals to allow external systems to interact with a running workflow. Here’s a concise step-by-step guide on how to define signals:

1. **Annotate Your Method:**  
   Use the `[WorkflowSignal]` attribute on a workflow method. This informs the workflow engine that the method can be invoked as a signal.

2. **Match the Signal Name:**  
   The name of the signal is by default the name of the method. Make sure the `signalName` in your HTTP request matches this method name.

3. **Define the Method Signature:**  
   The signal method should return a `Task` and accept any parameters that represent the data you want to pass. Only include the code necessary for signal handling.

4. **Process the Signal Data:**  
   Within the method, write logic (or enqueue the data) to process the signal.

### Minimal Example:

```csharp
using Temporalio.Workflows;

namespace XiansAi.EmailChannel.Flow
{
    [Workflow("Email Channel")]
    public class EmailChannelWorkflow
    {
        // Signal method to receive external data.
        [WorkflowSignal]
        public Task ReceiveEmail(EmailMessage emailMessage)
        {
            Console.WriteLine($"Received Message ={emailMessage.message}");
            // Process signal data 
            return Task.CompletedTask;
        }
    }

    public class EmailMessage
    {
        public string? message { get; set; }
        public string? emailAddress { get; set; }
    }
}
```

> **Note:** In the HTTP request body, ensure the `signalName` is set to `"ReceiveEmail"` so that the signal is correctly routed to this method.

## Example

```bash
{
# Send a signal to a workflow
curl -X POST https://api.xians.ai/api/agent/signal \
  -H "Content-Type: application/json" \
  -H "Authorization: Bearer ${APP_SERVER_API_KEY}" \
  -d '{
    "workflowId": "ID-1123145",
    "signalName": "ReceiveEmail",
    "payload": {
      "message": "Hello, please process this signal.",
      "emailAddress": "user@example.com"
    }
  }'
```

## Notes

- Ensure that the workflow is running when the signal is sent.
- Signals are delivered asynchronously and processed based on the workflow's execution state.
- The `signalName` in the HTTP request body must match the signal method name defined in the workflow.
