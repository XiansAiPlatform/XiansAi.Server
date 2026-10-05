# XiansAi Server Tests

Automated tests for the XiansAi Server.

The full description of the integration suite (host, fixtures, API catalog, Temporal, Lib
agent cycles) is in
[`XiansAi.Server.Src/docs/integration-tests/`](../XiansAi.Server.Src/docs/integration-tests/index.md).

## Quick start

One `dotnet test` run has a single filter. It cannot stop the long suite when a short check fails, and it cannot split the Temporal collection across processes (those classes share one xUnit collection, so they run one at a time). [`run-suite.sh`](run-suite.sh) is that sequence. Every step is a normal `dotnet test --filter`.

```bash
# From the repository root. Smoke first; on failure the other lanes do not start.
# Unit tests, Mongo integration, and Temporal shards then run in parallel.
./XiansAi.Server.Tests/run-suite.sh

# More Temporal processes (each starts its own local Temporal CLI). Default is 2.
TEMPORAL_SHARDS=3 ./XiansAi.Server.Tests/run-suite.sh
```

Logs for the parallel lanes are written under `XiansAi.Server.Tests/test-results/suite/`.

The same lanes, run one at a time:

```bash
# Gate: host /health, Admin auth rejects a missing key, one tenant round-trip. No Temporal.
dotnet test --filter "Category=Smoke"

# Unit tests (no host)
dotnet test --filter "FullyQualifiedName~UnitTests"

# Mongo integration. The smoke class is in Tests.Smoke, so this does not rerun it.
dotnet test --filter "FullyQualifiedName~IntegrationTests&FullyQualifiedName!~AdminApiTemporal"

# Temporal collection, one process
dotnet test --filter "FullyQualifiedName~AdminApiTemporal"
```

`dotnet test` with no filter still runs the whole suite in one process, including smoke. Use that when you want one result log and do not need the gate or the split.

No environment variables or certificates are required for the default (Mongo-backed) suite.

```bash
# A single test
dotnet test --filter "FullyQualifiedName~CacheEndpointTests.SetAndGetCacheValue_ReturnsExpectedResult"

# All tests in a class
dotnet test --filter "FullyQualifiedName~KnowledgeEndpointsTests"

# Generate an HTML report
dotnet test --logger "html;LogFileName=test-results.html"
```

Override any key in [`appsettings.Tests.json`](appsettings.Tests.json) with ASP.NET Core
double-underscore environment variables (for example `EncryptionKeys__BaseSecret`). Never
commit real credentials.

The [`http/`](http/) directory contains `.http` request files for exploring endpoints by hand.
They are not part of `dotnet test`.
