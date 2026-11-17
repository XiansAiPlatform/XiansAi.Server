using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Shared.Data;
using Shared.Data.Models.Usage;
using Shared.Repositories;
using Tests.TestUtils;
using Xunit;

namespace Tests.UnitTests.Shared.Repositories;

/// <summary>
/// DB-level tests for the admin metrics time series bucketing, for both the MongoDB ($dateTrunc) and the
/// DocumentDB ($dateToString + C# roll-up) query paths. Both must produce the same API contract:
/// UTC calendar days, Sunday-based weeks and month starts, all stamped at 00:00:00Z.
///
/// By default this runs against the ephemeral Mongo2Go fixture (MongoDB 4.4). That server predates
/// $dateTrunc, so the MongoDB-path rows are skipped there and only the DocumentDB path is exercised.
/// Set XIANS_TEST_TIMESERIES_MONGO_URI (a full connection string including a database name) to run every
/// row against a real engine, e.g. a local mongo:8.0 or a local Azure DocumentDB emulator; on the latter
/// the MongoDB-path rows are expected to fail for day/week, which is the engine bug being worked around.
/// </summary>
public class UsageEventRepositoryTimeSeriesTests : IClassFixture<MongoDbFixture>
{
    private const string ExternalUriVariable = "XIANS_TEST_TIMESERIES_MONGO_URI";
    private const string AgentName = "Lead Discovery Agent";
    private const string Category = "agent_performance";
    private const string Type = "leads_discovered";

    private readonly IMongoDatabase _database;
    private readonly bool _serverSupportsDateTrunc;

    public UsageEventRepositoryTimeSeriesTests(MongoDbFixture fixture)
    {
        var externalUri = Environment.GetEnvironmentVariable(ExternalUriVariable);
        if (string.IsNullOrWhiteSpace(externalUri))
        {
            _database = fixture.Database;
        }
        else
        {
            var url = new MongoUrl(externalUri);
            _database = new MongoClient(url).GetDatabase(url.DatabaseName ?? "xians_timeseries_tests");
        }

        var buildInfo = _database.RunCommand<BsonDocument>(new BsonDocument("buildInfo", 1));
        var major = int.Parse(buildInfo["version"].AsString.Split('.')[0]);
        _serverSupportsDateTrunc = major >= 5;
    }

    private UsageEventRepository CreateRepository(MongoProvider provider)
    {
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(_database);

        var config = new MongoDBConfig
        {
            ConnectionString = "mongodb://unused",
            DatabaseName = _database.DatabaseNamespace.DatabaseName,
            Provider = provider
        };

        return new UsageEventRepository(databaseService.Object, config, NullLogger<UsageEventRepository>.Instance);
    }

    /// <summary>True when the row should be skipped because the server cannot run the MongoDB-path query.</summary>
    private bool CannotRun(MongoProvider provider) => provider == MongoProvider.MongoDB && !_serverSupportsDateTrunc;

    private static UsageMetric Metric(string tenantId, DateTime createdAtUtc, double value) => new()
    {
        TenantId = tenantId,
        AgentName = AgentName,
        ActivationName = "activation-1",
        Category = Category,
        Type = Type,
        Value = value,
        Unit = "count",
        CreatedAt = createdAtUtc
    };

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0) =>
        new(year, month, day, hour, minute, second, DateTimeKind.Utc);

    private static AdminMetricsTimeSeriesRequest Request(
        string tenantId, string groupBy, string aggregation = "sum", DateTime? start = null, DateTime? end = null) => new()
    {
        TenantId = tenantId,
        AgentName = AgentName,
        Category = Category,
        Type = Type,
        StartDate = start ?? Utc(2026, 9, 1),
        EndDate = end ?? Utc(2026, 9, 30, 23, 59, 59),
        GroupBy = groupBy,
        Aggregation = aggregation
    };

    private async Task<string> Seed(params UsageMetric[] metrics)
    {
        await CreateRepository(MongoProvider.MongoDB).InsertBatchAsync(metrics.ToList());
        return metrics[0].TenantId;
    }

    private Task<string> SeedThreeRecordsOnSep9()
    {
        var tenantId = $"ts-{Guid.NewGuid()}";
        return Seed(
            Metric(tenantId, Utc(2026, 9, 9, 4, 4, 22), 1),
            Metric(tenantId, Utc(2026, 9, 9, 11, 59, 0), 2),
            Metric(tenantId, Utc(2026, 9, 9, 12, 0, 0), 3));
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task GroupByDay_BucketsRecordsIntoTheirUtcCalendarDay(MongoProvider provider)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedThreeRecordsOnSep9();

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(Request(tenantId, "day"));

        var point = Assert.Single(response.DataPoints);
        Assert.Equal(Utc(2026, 9, 9), point.Timestamp);
        Assert.Equal(DateTimeKind.Utc, point.Timestamp.Kind);
        Assert.Equal(3, point.Count);
        Assert.Equal(6, point.Value);
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task GroupByWeek_BucketsRecordsIntoSundayBasedWeek(MongoProvider provider)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedThreeRecordsOnSep9();

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(Request(tenantId, "week"));

        var point = Assert.Single(response.DataPoints);
        // 2026-09-09 is a Wednesday; the week starts Sunday 2026-09-06
        Assert.Equal(Utc(2026, 9, 6), point.Timestamp);
        Assert.Equal(3, point.Count);
        Assert.Equal(6, point.Value);
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task GroupByMonth_BucketsRecordsIntoFirstOfMonth(MongoProvider provider)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedThreeRecordsOnSep9();

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(Request(tenantId, "month"));

        var point = Assert.Single(response.DataPoints);
        Assert.Equal(Utc(2026, 9, 1), point.Timestamp);
        Assert.Equal(3, point.Count);
        Assert.Equal(6, point.Value);
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task GroupBy_IsCaseInsensitive_OnBothPaths(MongoProvider provider)
    {
        // groupBy is validated case-insensitively upstream; $dateTrunc units are case-sensitive,
        // so the repository must normalise before building the pipeline.
        if (CannotRun(provider)) return;
        var tenantId = await SeedThreeRecordsOnSep9();

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(Request(tenantId, "Week"));

        var point = Assert.Single(response.DataPoints);
        Assert.Equal(Utc(2026, 9, 6), point.Timestamp);
        Assert.Equal(3, point.Count);
    }

    /// <summary>
    /// Records spread across days in two Sunday-based weeks (Aug 30 - Sep 5 and Sep 6 - Sep 12), with uneven
    /// per-day counts so an average-of-daily-averages would be visibly wrong (60 instead of 40).
    /// </summary>
    private Task<string> SeedTwoWeeks()
    {
        var tenantId = $"ts-{Guid.NewGuid()}";
        return Seed(
            Metric(tenantId, Utc(2026, 9, 1, 8), 10),
            Metric(tenantId, Utc(2026, 9, 1, 9), 20),
            Metric(tenantId, Utc(2026, 9, 1, 10), 30),
            Metric(tenantId, Utc(2026, 9, 4, 8), 100),
            Metric(tenantId, Utc(2026, 9, 6, 0, 0, 7), 5),
            Metric(tenantId, Utc(2026, 9, 8, 23, 59, 59), 7),
            Metric(tenantId, Utc(2026, 9, 9, 4, 14, 54), 9));
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB, "sum", 160, 21)]
    [InlineData(MongoProvider.MongoDB, "avg", 40, 7)]
    [InlineData(MongoProvider.MongoDB, "min", 10, 5)]
    [InlineData(MongoProvider.MongoDB, "max", 100, 9)]
    [InlineData(MongoProvider.MongoDB, "count", 4, 3)]
    [InlineData(MongoProvider.DocumentDB, "sum", 160, 21)]
    [InlineData(MongoProvider.DocumentDB, "avg", 40, 7)]
    [InlineData(MongoProvider.DocumentDB, "min", 10, 5)]
    [InlineData(MongoProvider.DocumentDB, "max", 100, 9)]
    [InlineData(MongoProvider.DocumentDB, "count", 4, 3)]
    public async Task GroupByWeek_EveryAggregation_ProducesWeeklyValues(
        MongoProvider provider, string aggregation, double week1Value, double week2Value)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedTwoWeeks();

        var response = await CreateRepository(provider)
            .GetAdminMetricsTimeSeriesAsync(Request(tenantId, "week", aggregation));

        Assert.Equal(new[] { Utc(2026, 8, 30), Utc(2026, 9, 6) }, response.DataPoints.Select(p => p.Timestamp));
        Assert.Equal(new long[] { 4, 3 }, response.DataPoints.Select(p => p.Count));
        Assert.Equal(new[] { week1Value, week2Value }, response.DataPoints.Select(p => p.Value));
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB, "day")]
    [InlineData(MongoProvider.MongoDB, "month")]
    [InlineData(MongoProvider.DocumentDB, "day")]
    [InlineData(MongoProvider.DocumentDB, "month")]
    public async Task DayAndMonth_AcrossMonthBoundaries_AreSortedAndStampedAtBucketStart(MongoProvider provider, string groupBy)
    {
        if (CannotRun(provider)) return;
        var tenantId = $"ts-{Guid.NewGuid()}";
        await Seed(
            Metric(tenantId, Utc(2026, 8, 31, 23, 59, 59), 1),
            Metric(tenantId, Utc(2026, 9, 1, 0, 0, 0), 2),
            Metric(tenantId, Utc(2026, 9, 15, 12, 0, 0), 3),
            Metric(tenantId, Utc(2026, 9, 30, 23, 59, 59), 4),
            Metric(tenantId, Utc(2026, 10, 1, 0, 0, 0), 5));
        var request = Request(tenantId, groupBy, "sum", Utc(2026, 8, 1), Utc(2026, 10, 31));

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(request);

        var expected = groupBy == "day"
            ? new[] { (Utc(2026, 8, 31), 1.0), (Utc(2026, 9, 1), 2.0), (Utc(2026, 9, 15), 3.0), (Utc(2026, 9, 30), 4.0), (Utc(2026, 10, 1), 5.0) }
            : new[] { (Utc(2026, 8, 1), 1.0), (Utc(2026, 9, 1), 9.0), (Utc(2026, 10, 1), 5.0) };
        Assert.Equal(expected, response.DataPoints.Select(p => (p.Timestamp, p.Value)));
        Assert.Equal(15, response.Summary.TotalValue);
        Assert.Equal(expected.Length, response.Summary.DataPointCount);
    }

    // ---- metadata filter ----------------------------------------------------------------------

    private static UsageMetric Tagged(string tenantId, DateTime createdAtUtc, double value, params (string Key, string Value)[] metadata)
    {
        var metric = Metric(tenantId, createdAtUtc, value);
        metric.Metadata = metadata.Length == 0 ? null : metadata.ToDictionary(m => m.Key, m => m.Value);
        return metric;
    }

    /// <summary>
    /// Two days in each of two Sunday-based weeks (Aug 30 - Sep 5 and Sep 6 - Sep 12). Values are powers
    /// of two so every expected sum identifies exactly which records were bucketed.
    /// </summary>
    private Task<string> SeedTaggedRuns()
    {
        var tenantId = $"ts-{Guid.NewGuid()}";
        return Seed(
            Tagged(tenantId, Utc(2026, 9, 1, 8), 1, ("status", "completed"), ("origin", "email")),
            Tagged(tenantId, Utc(2026, 9, 1, 9), 2, ("status", "failed"), ("origin", "email")),
            Tagged(tenantId, Utc(2026, 9, 4, 8), 4, ("status", "failed"), ("origin", "chat")),
            Tagged(tenantId, Utc(2026, 9, 8, 8), 8, ("status", "skipped")),
            Tagged(tenantId, Utc(2026, 9, 8, 9), 16, ("status", "failed"), ("origin", "email")),
            Tagged(tenantId, Utc(2026, 9, 9, 8), 32));  // no metadata at all
    }

    private static Dictionary<string, string[]> Filter(params (string Key, string[] Values)[] filters) =>
        filters.ToDictionary(f => f.Key, f => f.Values);

    public static TheoryData<MongoProvider, string, double[]> OneKeyOneValueCases() => new()
    {
        // status=failed -> 2 (Sep 1), 4 (Sep 4), 16 (Sep 8)
        { MongoProvider.MongoDB, "day", new double[] { 2, 4, 16 } },
        { MongoProvider.MongoDB, "week", new double[] { 6, 16 } },
        { MongoProvider.MongoDB, "month", new double[] { 22 } },
        { MongoProvider.DocumentDB, "day", new double[] { 2, 4, 16 } },
        { MongoProvider.DocumentDB, "week", new double[] { 6, 16 } },
        { MongoProvider.DocumentDB, "month", new double[] { 22 } },
    };

    [Theory]
    [MemberData(nameof(OneKeyOneValueCases))]
    public async Task MetadataFilter_OneKeyOneValue_BucketsOnlyMatchingRecords(
        MongoProvider provider, string groupBy, double[] expectedValues)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedTaggedRuns();
        var request = Request(tenantId, groupBy) with { Metadata = Filter(("status", new[] { "failed" })) };

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(request);

        Assert.Equal(expectedValues, response.DataPoints.Select(p => p.Value));
        Assert.Equal(3, response.Summary.TotalCount);
        Assert.Equal(new[] { "failed" }, response.Filters.Metadata!["status"]);
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task MetadataFilter_OneKeySeveralValues_MatchesAnyOfThem(MongoProvider provider)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedTaggedRuns();
        var request = Request(tenantId, "week") with { Metadata = Filter(("status", new[] { "completed", "skipped" })) };

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(request);

        Assert.Equal(new[] { (Utc(2026, 8, 30), 1.0), (Utc(2026, 9, 6), 8.0) }, response.DataPoints.Select(p => (p.Timestamp, p.Value)));
        Assert.Equal(new long[] { 1, 1 }, response.DataPoints.Select(p => p.Count));
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task MetadataFilter_TwoKeys_RequiresBothToMatch(MongoProvider provider)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedTaggedRuns();
        var request = Request(tenantId, "day") with
        {
            Metadata = Filter(("status", new[] { "failed" }), ("origin", new[] { "email" }))
        };

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(request);

        Assert.Equal(new[] { (Utc(2026, 9, 1), 2.0), (Utc(2026, 9, 8), 16.0) }, response.DataPoints.Select(p => (p.Timestamp, p.Value)));
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task MetadataFilter_KeyNoRecordHas_ReturnsNoDataPoints(MongoProvider provider)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedTaggedRuns();
        var request = Request(tenantId, "day") with { Metadata = Filter(("decision", new[] { "approved" })) };

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(request);

        Assert.Empty(response.DataPoints);
        Assert.Equal(0, response.Summary.TotalCount);
    }

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task NoMetadataFilter_BucketsEveryRecordAndEchoesNull(MongoProvider provider)
    {
        if (CannotRun(provider)) return;
        var tenantId = await SeedTaggedRuns();

        var response = await CreateRepository(provider).GetAdminMetricsTimeSeriesAsync(Request(tenantId, "month"));

        Assert.Equal(63, Assert.Single(response.DataPoints).Value);
        Assert.Null(response.Filters.Metadata);
        Assert.Null(response.Filters.BreakdownBy);
    }

    // ---- stats: filter and breakdown on both providers ----------------------------------------

    private static AdminMetricsStatsRequest StatsRequest(string tenantId) => new()
    {
        TenantId = tenantId,
        AgentName = AgentName,
        StartDate = Utc(2026, 9, 1),
        EndDate = Utc(2026, 9, 30, 23, 59, 59)
    };

    [Theory]
    [InlineData(MongoProvider.MongoDB)]
    [InlineData(MongoProvider.DocumentDB)]
    public async Task Stats_MetadataFilterAndBreakdown_AgreeOnBothProviders(MongoProvider provider)
    {
        // The stats query has no provider-specific path, so this runs on the 4.4 fixture for both rows.
        var tenantId = await SeedTaggedRuns();
        var repository = CreateRepository(provider);

        var filtered = await repository.GetAdminMetricsStatsAsync(
            StatsRequest(tenantId) with { Metadata = Filter(("status", new[] { "failed" })) });
        var filteredStats = Assert.Single(Assert.Single(filtered.CategoriesAndTypes).Types).Stats;
        Assert.Equal(3, filteredStats.Count);
        Assert.Equal(22, filteredStats.Sum);

        var unfiltered = await repository.GetAdminMetricsStatsAsync(StatsRequest(tenantId));
        var totals = Assert.Single(Assert.Single(unfiltered.CategoriesAndTypes).Types);
        Assert.Null(totals.Breakdown);

        var brokenDown = await repository.GetAdminMetricsStatsAsync(StatsRequest(tenantId) with { BreakdownBy = "status" });
        var type = Assert.Single(Assert.Single(brokenDown.CategoriesAndTypes).Types);
        Assert.Equal(totals.Stats, type.Stats);
        Assert.Equal(new[] { "completed", "failed", "skipped", null }, type.Breakdown!.Select(b => b.Value));
        Assert.Equal(new long[] { 1, 3, 1, 1 }, type.Breakdown!.Select(b => b.Count));
        Assert.Equal(new double[] { 1, 22, 8, 32 }, type.Breakdown!.Select(b => b.Sum));
        Assert.Equal(totals.Stats.Count, type.Breakdown!.Sum(b => b.Count));
        Assert.Equal(totals.Stats.Sum, type.Breakdown!.Sum(b => b.Sum));
    }

    [Fact]
    public async Task Stats_Breakdown_CoversRecordsStoredWithoutActivationOrMetadataFields()
    {
        // Documents written by other producers may omit activation_name and metadata entirely
        // (as opposed to storing null); they must still land in the "unknown" activation's null entry.
        var tenantId = await SeedTaggedRuns();
        await _database.GetCollection<BsonDocument>("usage_metrics").InsertOneAsync(new BsonDocument
        {
            { "tenant_id", tenantId },
            { "agent_name", AgentName },
            { "category", Category },
            { "type", Type },
            { "value", 64.0 },
            { "unit", "count" },
            { "created_at", Utc(2026, 9, 10) }
        });

        var response = await CreateRepository(MongoProvider.MongoDB)
            .GetAdminMetricsStatsAsync(StatsRequest(tenantId) with { BreakdownBy = "status" });

        var type = Assert.Single(Assert.Single(response.CategoriesAndTypes).Types);
        Assert.Equal(7, type.Stats.Count);
        Assert.Equal(new[] { "completed", "failed", "skipped", null }, type.Breakdown!.Select(b => b.Value));
        Assert.Equal(new long[] { 1, 3, 1, 2 }, type.Breakdown!.Select(b => b.Count));
        Assert.Equal(new double[] { 1, 22, 8, 96 }, type.Breakdown!.Select(b => b.Sum));

        foreach (var activation in response.ByActivation)
        {
            var activationType = Assert.Single(Assert.Single(activation.CategoriesAndTypes).Types);
            Assert.Equal(activationType.Stats.Count, activationType.Breakdown!.Sum(b => b.Count));
            Assert.Equal(activationType.Stats.Sum, activationType.Breakdown!.Sum(b => b.Sum));
        }
        Assert.Equal(7, response.ByActivation.Sum(a => a.MetricCount));
        var unknown = Assert.Single(response.ByActivation, a => a.ActivationName == "unknown");
        var unknownEntry = Assert.Single(Assert.Single(Assert.Single(unknown.CategoriesAndTypes).Types).Breakdown!);
        Assert.Null(unknownEntry.Value);
        Assert.Equal(64, unknownEntry.Sum);
    }

    [Theory]
    [InlineData("status.nested")]
    [InlineData("$where")]
    [InlineData("")]
    public async Task InvalidMetadataKey_IsRejectedBeforeItReachesTheQuery(string key)
    {
        var repository = CreateRepository(MongoProvider.DocumentDB);
        var filter = Filter((key, new[] { "x" }));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.GetAdminMetricsTimeSeriesAsync(Request("ts-invalid", "day") with { Metadata = filter }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            repository.GetAdminMetricsStatsAsync(StatsRequest("ts-invalid") with { Metadata = filter }));
    }
}
