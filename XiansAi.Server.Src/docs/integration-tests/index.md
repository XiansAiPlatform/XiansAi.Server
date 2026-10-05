# Integration tests

The integration suite lives in [`XiansAi.Server.Tests`](../../../XiansAi.Server.Tests/) and exercises the real ASP.NET Core request pipeline against an in-process host. Each test sends HTTP to the mapped endpoints (routing, authentication, validation, and persistence) and asserts a single deterministic outcome.

This folder is the canonical description of that suite. The test project [`README.md`](../../../XiansAi.Server.Tests/README.md) is a short pointer plus run commands.

## In this folder

- [Host and fixtures](./host.md) — `WebApplicationFactory`, MongoDB, auth, configuration, and mocks
- [API suites](./suites.md) — what each test class covers, and how to add one
- [Temporal tests](./temporal.md) — local Temporal CLI and the in-process stub worker
- [Lib agent workflows](./lib-agent-workflows.md) — Echo, Knowledge, Knowledge list, Knowledge SDK, Secret Vault, Secret Vault SDK, Document DB, Document DB SDK, Document context, Webhooks, Webhook SDK, Webhook context, Files, Workflow files, Custom workflow, Child workflows, Workflow handle, Schedules, Schedule SDK, Schedule create, HITL, HITL SDK, HITL conversation, HITL last task, Cross-agent, Activations SDK, Metrics, Logging, Messaging SDK, and Tenant-scoped cycles authored with Xians.Lib

## Quick start

From the repository root:

```bash
# Smoke gate, then unit, Mongo integration, and Temporal shards in parallel.
./XiansAi.Server.Tests/run-suite.sh
```

[`run-suite.sh`](../../../XiansAi.Server.Tests/run-suite.sh) runs [`SuiteSmokeTests`](../../../XiansAi.Server.Tests/Smoke/SuiteSmokeTests.cs) first (`Category=Smoke`: host `/health`, missing Admin key, one tenant round-trip, no Temporal). If that fails, the other lanes are not started. Otherwise unit tests, Mongo integration, and Temporal shards run as separate `dotnet test --filter` processes. `TEMPORAL_SHARDS` (default 2) is how many Temporal processes to start. Each process has its own local CLI, because classes in the `AdminApiTemporal` collection do not run in parallel inside one process.

`dotnet test` with no filter still runs everything in one process. Lane filters and the single-test commands are in the [test project README](../../../XiansAi.Server.Tests/README.md).

No environment variables or certificates are required for the default (Mongo-backed) suite.

The first Temporal run may download the Temporal CLI; later runs reuse the cache. See [Temporal tests](./temporal.md).

## What the suite is for

| Layer | When to use it | Examples |
| --- | --- | --- |
| Unit (`UnitTests/`) | Pure logic; no host startup | SSRF URL validation, secret-vault rules, JWT claim extraction |
| Integration (`IntegrationTests/`) | Real HTTP pipeline + Mongo (and, opt-in, local Temporal) | Admin/Web/Agent/User/Apps API groups |

The suite prefers a small, meaningful set of tests over exhaustive coverage. Assertions pick one status and body shape. Data is seeded when the path needs it. Weak checks such as `Assert.True(status == OK || status == BadRequest)` are avoided.

## What runs in-process

Every integration test starts:

1. An ephemeral MongoDB replica set ([`MongoDbFixture`](../../../XiansAi.Server.Tests/TestUtils/MongoDbFixture.cs), Mongo2Go).
2. The full application via [`XiansAiWebApplicationFactory`](../../../XiansAi.Server.Tests/TestUtils/XiansAiWebApplicationFactory.cs).
3. Stubbed authentication ([`TestAuthHandler`](../../../XiansAi.Server.Tests/TestUtils/TestAuthHandler.cs)).

By default Temporal, email, background tasks, and certificate generation are mocked. Tests in the `AdminApiTemporal` collection skip the Temporal mock and start a local CLI/dev server instead.

## What is not tested here

- Real identity providers (Auth0, Azure AD, Keycloak)
- A remote Temporal cluster or a shared MongoDB
- Native WebSocket transport (SignalR tests use long polling against TestServer)

Manual `.http` files under [`XiansAi.Server.Tests/http/`](../../../XiansAi.Server.Tests/http/) are a developer convenience and are not part of `dotnet test`.

## Architecture constraint

[ARCH-011](../../../docs/architecture/constraints.md) requires integration tests to be self-contained: in-process Mongo, stubbed auth, no live SaaS. Temporal tests still satisfy that by using `WorkflowEnvironment.StartLocalAsync` (a local CLI), not a remote cluster.

## Related documentation

- [Authentication configuration](../AUTH_CONFIGURATION.md)
- [Temporal configuration](../TEMPORAL_CONFIGURATION.md)
- [Architecture constraints](../../../docs/architecture/constraints.md)
- [Architecture fitness functions](../../../docs/architecture/fitness-functions.md)
