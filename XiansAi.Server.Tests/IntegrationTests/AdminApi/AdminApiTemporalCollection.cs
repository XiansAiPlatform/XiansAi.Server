using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Shares one <see cref="TemporalFixture"/> (one Temporal CLI/dev server) across
/// Admin API tests that talk to Temporal. xUnit does not run classes in the same
/// collection in parallel, so workflow IDs stay unique without extra locking.
/// </summary>
[CollectionDefinition(Name)]
public sealed class AdminApiTemporalCollection : ICollectionFixture<TemporalFixture>
{
    public const string Name = "AdminApiTemporal";
}
