# XiansAi Server Tests

Automated tests for the XiansAi Server.

The full description of the integration suite (host, fixtures, API catalog, Temporal, Echo
cycle) is in
[`XiansAi.Server.Src/docs/integration-tests/`](../XiansAi.Server.Src/docs/integration-tests/index.md).

## Quick start

```bash
# From the repository root or this project directory
dotnet test
```

No environment variables or certificates are required for the default (Mongo-backed) suite.

```bash
# A single test
dotnet test --filter "FullyQualifiedName~CacheEndpointTests.SetAndGetCacheValue_ReturnsExpectedResult"

# All tests in a class
dotnet test --filter "FullyQualifiedName~KnowledgeEndpointsTests"

# Admin API tests that use the local Temporal CLI/dev server
dotnet test --filter "FullyQualifiedName~AdminApiTemporal"

# Generate an HTML report
dotnet test --logger "html;LogFileName=test-results.html"
```

Override any key in [`appsettings.Tests.json`](appsettings.Tests.json) with ASP.NET Core
double-underscore environment variables (for example `EncryptionKeys__BaseSecret`). Never
commit real credentials.

The [`http/`](http/) directory contains `.http` request files for exploring endpoints by hand.
They are not part of `dotnet test`.
