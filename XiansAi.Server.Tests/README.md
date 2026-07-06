# XiansAi Server Tests

Automated tests for the XiansAi Server. The suite favours a small, meaningful set of tests over
exhaustive coverage: unit tests target security-sensitive and conversion logic, and integration
tests exercise the real HTTP pipeline (routing, auth, validation, and CRUD) for the core API
groups.

## Quick start

```bash
# From the repository root or this project directory
dotnet test
```

No environment variables or certificates are required. Each integration test spins up the full
application in-process with an ephemeral MongoDB instance, so a clean `dotnet test` run is
self-contained.

## What we test

| Layer | Focus | Examples |
|-------|-------|----------|
| Unit | Pure logic where correctness matters and host startup is unnecessary | SSRF URL validation, secret-vault business rules, workflow parameter conversion, `TimeSpan` parsing, JWT claim extraction |
| Integration | The real request pipeline for each API surface: routing, authentication, validation, and persistence | WebApi (knowledge, messaging, permissions, roles, workflows), AgentApi (cache, logs, secrets, definitions), AdminApi (agents, tenants, users, secrets), UserApi and AppsApi |

We deliberately avoid:

- Weak assertions such as `Assert.True(status == OK || status == BadRequest)`. Every test
  asserts a single, deterministic outcome, seeding data where necessary.
- Duplicated tests that re-exercise the same code path with cosmetic differences.
- Endpoints that cannot run meaningfully in-process (SignalR hubs, SSE streams, and live
  Temporal workflow start/cancel). These are covered by manual `.http` files and higher-level
  environments instead.

## How the integration test host works

Integration tests derive from `IntegrationTestBase` (and its `WebApiIntegrationTestBase` /
`AdminApiIntegrationTestBase` specializations). The host is configured by
[`XiansAiWebApplicationFactory`](TestUtils/XiansAiWebApplicationFactory.cs):

- **Configuration** is loaded from [`appsettings.Tests.json`](appsettings.Tests.json), which is
  copied to the test output directory by the csproj and resolved from `AppContext.BaseDirectory`.
  The file holds only synthetic, non-sensitive fixtures (including `EncryptionKeys:BaseSecret` and
  `EncryptionKeys:UniqueSecrets:AppIntegrationSecretKey`).
- **`AuthProvider:Provider` is set to `Oidc`** so the WebApi endpoints are registered. Without it,
  `Program.cs` skips mapping the WebApi routes and those tests would 404.
- **MongoDB** points at the in-process `MongoDbFixture` (a throwaway database per test run).
- **External dependencies are mocked**: `ITemporalClientService` (no live Temporal), email,
  background tasks, and certificate generation.
- **Authentication is stubbed** via `TestAuthHandler`, which authenticates every request and
  grants `SysAdmin`, `TenantAdmin`, and `TenantUser` roles. Endpoints guarded by API-key policies
  that are *not* overridden (for example the UserApi `EndpointAuthPolicy`) still require a real
  key, so their tests create one through the repository.

## Overriding values with environment variables

Any key in `appsettings.Tests.json` can be replaced at runtime using ASP.NET Core's
double-underscore syntax (`Section:Key` becomes `Section__Key`). This is the recommended way to
inject a real secret in CI without committing it:

```bash
export EncryptionKeys__BaseSecret="$(openssl rand -base64 48)"
dotnet test
```

Never commit real credentials into `appsettings.Tests.json` or a `.env` file.

## Running subsets and reports

```bash
# A single test
dotnet test --filter "FullyQualifiedName~CacheEndpointTests.SetAndGetCacheValue_ReturnsExpectedResult"

# All tests in a class
dotnet test --filter "FullyQualifiedName~KnowledgeEndpointsTests"

# Generate an HTML report
dotnet test --logger "html;LogFileName=test-results.html"
```

## Manual HTTP files

The [`http/`](http/) directory contains `.http` request files for exploring endpoints by hand.
They are a developer convenience and are not part of the automated suite.
