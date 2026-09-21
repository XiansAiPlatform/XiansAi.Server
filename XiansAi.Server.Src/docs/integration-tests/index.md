# Integration tests

The integration suite lives in [`XiansAi.Server.Tests`](../../../XiansAi.Server.Tests/) and exercises the real ASP.NET Core request pipeline against an in-process host. Each test sends HTTP to the mapped endpoints (routing, authentication, validation, and persistence) and asserts a single deterministic outcome.

This folder is the canonical description of that suite. The test project [`README.md`](../../../XiansAi.Server.Tests/README.md) is a short pointer plus run commands.

## In this folder

- [Host and fixtures](./host.md) — `WebApplicationFactory`, MongoDB, auth, configuration, and mocks
- [API suites](./suites.md) — what each test class covers, and how to add one
- [Temporal tests](./temporal.md) — local Temporal CLI and the in-process stub worker
- [Lib agent workflows](./lib-agent-workflows.md) — Echo, Knowledge, and Secret Vault cycles authored with Xians.Lib

## Quick start

From the repository root or `XiansAi.Server.Tests/`:

```bash
dotnet test
```

No environment variables or certificates are required for the default (Mongo-backed) suite.

```bash
# One test
dotnet test --filter "FullyQualifiedName~CacheEndpointTests.SetAndGetCacheValue_ReturnsExpectedResult"

# One class
dotnet test --filter "FullyQualifiedName~KnowledgeEndpointsTests"

# Admin API tests that start the local Temporal CLI
dotnet test --filter "FullyQualifiedName~AdminApiTemporal"

# HTML report
dotnet test --logger "html;LogFileName=test-results.html"
```

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
