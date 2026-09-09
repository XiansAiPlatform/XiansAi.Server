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
}
