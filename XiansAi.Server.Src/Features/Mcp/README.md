# Xians MCP

Runs inside the server's `WebApi` and `All` modes using Streamable HTTP.

**URL:** `/api/v1/admin/mcp`

**Authentication:** `Authorization: Bearer <Xians admin API key>`. Use a tenant-scoped key; never store the key in Rules JSON. Agent read/write permissions and explicit target boundaries are checked for every tool call. Tools cannot override the authenticated tenant; SysAdmins may select a tenant using the `X-Tenant-Id` connection header.

## Tools

| Tool | Purpose |
| --- | --- |
| `list_tenants` | Discover the authenticated tenant. |
| `list_agents` | Discover accessible agents in that tenant. |
| `list_activations` | Discover an accessible agent’s activations. |
| `list_workflows` | Discover registered workflow types and ordered input parameter definitions. |
| `list_schedules` | List activation schedules, 100 per zero-based page. |
| `create_schedule` | Start a registered workflow on a cron schedule with ordered JSON arguments. |
| `update_schedule_timing` | Change cron/timezone, keeping inputs and pause state. |
| `delete_schedule` | Delete by exact schedule ID. |
| `pause_schedule` | Suspend future runs. |
| `resume_schedule` | Resume future runs. |
| `list_data_types` | Discover record categories in the activation. |
| `list_data_records` | Browse records by type/date with pagination. |
| `save_data_record` | Save a new JSON object visible in Data Explorer; pass `content` as JSON text, e.g. `"{\"title\":\"Report\"}"`. |
| `delete_data_record` | Permanently delete an exact record ID after confirmation. |
| `delete_data_records` | Permanently delete up to 100 records by type/date after confirmation; larger matches are rejected. |

Schedule and data tools require `target: { "tenantId": "...", "agentName": "...", "activationName": "..." }` and are restricted to that authorized target. Data browsing/deletion uses date ranges up to 365 days; browsing returns at most 100 records per call. Deletions are irreversible. Confirmation flags guide the model, not a separate human-approval security boundary.

List first and reuse exact IDs. Duplicate schedule names fail rather than silently keeping old inputs. MCP manages schedules; an existing agent worker executes them and decides where results go.

Schedule target identifiers and schedule names cannot contain `:`. Listing and modifications also verify the schedule's tenant, agent, and activation memo.

Bulk deletion is restricted to the previewed record IDs, so concurrent writes cannot expand it. Every attempt writes a structured server audit log with the authenticated user, target, filters, completion status, and deleted count; confirmation flags are not independent human approval.

## Scheduled prompt example

Connect from Prompt Defined Agent's Rules JSON (replace the Server URL and store `XIANS_MCP_KEY` in the platform secret vault):

```json
{
  "mcpServers": [{
    "name": "xians",
    "url": "https://your-server/api/v1/admin/mcp",
    "transport": "streamableHttp",
    "context": "xians",
    "authentication": { "type": "bearer", "secret": "XIANS_MCP_KEY" }
  }]
}
```

Arguments for `create_schedule` targeting `Prompt Defined Agent:Scheduled Prompt Workflow`:

```json
{
  "target": { "tenantId": "your-tenant", "agentName": "Prompt Defined Agent", "activationName": "your-activation" },
  "scheduleName": "React stars",
  "workflowType": "Prompt Defined Agent:Scheduled Prompt Workflow",
  "arguments": [{
    "Prompt": "Report how many stars facebook/react has.",
    "Parameters": {},
    "ParticipantId": "your-chat-participant-id",
    "Scope": null
  }],
  "cron": "0 9 * * *",
  "timezone": "Asia/Colombo"
}
```

This workflow sends its output to the specified participant. This is an administrative MCP: credentials permit administrative operations in the authenticated tenant, not just one chat participant. Do not expose these credentials to untrusted users. Prompt Defined Agent uses MCP tools exclusively; scheduled prompt inputs must specify the correct chat participant.

With `context: "xians"`, Prompt Defined Agent supplies target fields programmatically from XiansContext. Other clients pass discovery/target fields explicitly. The previous activation-scoped URL is no longer supported.
