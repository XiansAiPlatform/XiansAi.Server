# Host and fixtures

Integration tests derive from [`IntegrationTestBase`](../../../XiansAi.Server.Tests/TestUtils/IntegrationTestBase.cs). The constructor builds a [`XiansAiWebApplicationFactory`](../../../XiansAi.Server.Tests/TestUtils/XiansAiWebApplicationFactory.cs) around the shared [`MongoDbFixture`](../../../XiansAi.Server.Tests/TestUtils/MongoDbFixture.cs) and an optional [`TemporalFixture`](../../../XiansAi.Server.Tests/TestUtils/TemporalFixture.cs).

```text
Test class
  └─ IntegrationTestBase / WebApiIntegrationTestBase / AdminApiIntegrationTestBase
       ├─ MongoDbFixture          (ephemeral Mongo2Go replica set)
       ├─ TemporalFixture?        (only AdminApiTemporal collection)
       └─ XiansAiWebApplicationFactory
            ├─ appsettings.Tests.json + env overrides
            ├─ TestAuthHandler
            ├─ real Mongo client (fixture)
            └─ mocked Temporal / email / background tasks  (unless Temporal is opted in)
```

## Application factory

[`XiansAiWebApplicationFactory`](../../../XiansAi.Server.Tests/TestUtils/XiansAiWebApplicationFactory.cs) hosts `Program` with `UseEnvironment("Tests")`.

On each host:

- Configuration sources are cleared, then [`appsettings.Tests.json`](../../../XiansAi.Server.Tests/appsettings.Tests.json) is loaded from the test output directory (`AppContext.BaseDirectory`).
- Mongo connection string and database name are replaced with the live fixture values.
- Environment variables are added so any JSON key can be overridden at runtime (`Section__Key`).
- If a `TemporalFixture` was passed in, `Temporal:FlowServerUrl` and `Temporal:FlowServerNamespace` are pinned **after** env vars so leftover `Temporal__*` values cannot point tests at a remote cluster.
- `IMongoDbClientService` is the fixture's client.
- Seed tenants `test-tenant` and `99x.io` are inserted if missing.

`AuthProvider:Provider` is `Oidc` in the test settings so `Program.cs` maps the WebApi routes. Without that value those tests 404.

## MongoDB fixture

[`MongoDbFixture`](../../../XiansAi.Server.Tests/TestUtils/MongoDbFixture.cs) starts Mongo2Go with `singleNodeReplSet: true`, waits until a PRIMARY exists, creates a `test_db` database, and applies a small set of collections and indexes.

It is an `IClassFixture` on `IntegrationTestBase`, so each test **class** gets its own replica set. Tests in the same class share that database; seed unique tenant and agent names (`Guid`) so cases do not collide.

The fixture starts Mongo as a replica set so change streams work. It also creates `conversation_message` / `conversation_thread` so [`MongoChangeStreamService`](../../Features/UserApi/Services/MongoChangeStreamService.cs) can watch without racing collection creation. That is what fans Echo replies out to Admin SSE and SignalR.

Do not hard-code `mongodb://` or `mongodb+srv://` connection strings in tests. The fixture supplies the address.

## Authentication

[`TestAuthHandler`](../../../XiansAi.Server.Tests/TestUtils/TestAuthHandler.cs) authenticates every request and issues `SysAdmin`, `TenantAdmin`, and `TenantUser` roles plus `test-tenant` / `99x.io` as authorized tenants. On Agent API paths (`/api/agent`) it also copies `X-Tenant-Id` onto the singleton `ITenantContext`, standing in for `CertificateAuthenticationHandler` so Lib SDK calls (activations, replies) see the acting tenant.

The factory rebinds these policies onto the `Test` scheme:

- Agent API: `RequireCertificate`, `RequireCertificateSysAdmin`, `RequireCertificateTenantAdmin`
- Web API: `RequireTokenAuth`, `RequireTenantAuth`, `RequireTenantAuthWithoutConfig`
- Admin-style role policies: `RequireSysAdmin`, `RequireTenantAdmin`

Policies that are **not** overridden still need a real credential. UserApi `EndpointAuthPolicy` is one of those: [`RestEndpointsTests`](../../../XiansAi.Server.Tests/IntegrationTests/UserApi/RestEndpointsTests.cs) creates an API key through the repository and passes it as `apikey`.

### Per-surface HTTP headers

| Surface | Base class | How the client authenticates |
| --- | --- | --- |
| WebApi, AgentApi | `IntegrationTestBase` / `WebApiIntegrationTestBase` | Bearer `test-api-key`, `X-Tenant-Id: test-tenant`, `X-Test-Certificate` |
| AdminApi | [`AdminApiIntegrationTestBase`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiIntegrationTestBase.cs) | Real `sk-Xnai-…` key from `ConfigureAdminApiClientAsync(tenantId)` plus `X-Tenant-Id` |
| UserApi (policy not stubbed) | `IntegrationTestBase` | Fresh client + repository-created API key on the query string |
| Xians.Lib worker | Echo, Knowledge, Secret Vault, Document DB, Webhooks, Files, Custom workflow, Schedules, HITL, Cross-agent, and Activations SDK cycles | PFX-shaped key from [`XiansLibTestCertificate`](../../../XiansAi.Server.Tests/TestUtils/XiansLibTestCertificate.cs) over [`TestServerLoopback`](../../../XiansAi.Server.Tests/TestUtils/TestServerLoopback.cs), owned by [`LibAgentWorkflowHost`](../../../XiansAi.Server.Tests/TestUtils/LibAgentWorkflowHost.cs) |
| Tenant SignalR (`/ws/tenant/chat`) | Echo cycle | Same Admin `sk-Xnai-…` key as `apikey` query (UserApi websocket policy is not stubbed) |
| UserApi SSE (`/api/user/sse/events`) | Echo cycle | Same Admin key as `Authorization: Bearer` on a separate streaming `HttpClient` |
| ChatHub (`/ws/chat`) | Echo cycle | Same Admin key as `apikey` query; `SubscribeToAgent(workflow, participantId, tenantId)` |

`ITenantContext` is a Moq singleton with `SetupProperty`, so tests that need a specific tenant (Temporal Echo, some Agent API paths) can assign `TenantId`, `LoggedInUser`, `ParticipantId`, and roles on the resolved instance.

## Configuration and secrets

[`appsettings.Tests.json`](../../../XiansAi.Server.Tests/appsettings.Tests.json) holds only synthetic fixtures (RFC-2606 `.invalid` URLs, non-secret encryption padding). Never copy production values into that file. `WebSockets:Enabled` is `true` so UserApi SignalR handshakes are accepted.

Override any key without editing JSON:

```bash
export EncryptionKeys__BaseSecret="$(openssl rand -base64 48)"
dotnet test
```

The csproj copies `appsettings.Tests.json` to the output directory. The factory looks there first, then a couple of source-relative fallbacks.

## Default mocks

When `TemporalFixture` is **null** (the usual case):

- `ITemporalGatewayService` returns a mock `ITemporalClient`
- `IActivationCleanupService` no-ops so deactivate/delete do not talk to Temporal

Always mocked, Temporal or not:

- `IEmailService`
- `IBackgroundTaskService`
- `CertificateGenerator` (dummy PFX config)
- `IUserTenantService` (returns the two seed tenants)
- `IAuthProvider` / `IAuthProviderFactory` (token validation succeeds as `test-user`)

When Temporal is opted in, the real gateway and `ActivationCleanupService` stay registered. Details are in [Temporal tests](./temporal.md).

## Base classes and HTTP helpers

| Type | Role |
| --- | --- |
| [`IntegrationTestBase`](../../../XiansAi.Server.Tests/TestUtils/IntegrationTestBase.cs) | Factory, Mongo, default headers, retry wrapper |
| [`WebApiIntegrationTestBase`](../../../XiansAi.Server.Tests/IntegrationTests/WebApi/WebApiIntegrationTestBase.cs) | `GetAsync` / `PostAsJsonAsync` / `PutAsJsonAsync` / `DeleteAsync` and JSON read options |
| [`AdminApiIntegrationTestBase`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiIntegrationTestBase.cs) | Admin key setup plus seed helpers (`CreateTestTenantAsync`, `CreateTestAgentAsync`, `CreateTestActivationAsync`, `CreateBuiltInFlowDefinitionAsync`, …) |
| [`AdminApiTemporalIntegrationTestBase`](../../../XiansAi.Server.Tests/IntegrationTests/AdminApi/AdminApiTemporalIntegrationTestBase.cs) | Shared Temporal client, stub worker start, workflow/HITL wait helpers |

[`RetryHttpClient`](../../../XiansAi.Server.Tests/TestUtils/RetryHttpClient.cs) retries unauthorized and timeout responses. Prefer the helpers on the Web/Admin bases over constructing `HttpRequestMessage` by hand.

## Logging

The factory sets console logging to `Warning` to keep `dotnet test` output readable. Raise the level locally only while diagnosing a single failing test.
