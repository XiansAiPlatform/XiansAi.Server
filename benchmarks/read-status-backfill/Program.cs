using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using Shared.Data;

namespace ReadStatusBackfillBenchmark;

/// <summary>
/// Runs the conversation read-status backfill against a seeded database and reports how long
/// it takes and how it affects application queries running at the same time.
///
///   old [--probe-budget 120]          the original single UpdateMany with 3 retries
///   new [--kill-after N] [--owner X]  the batched backfill from ConversationRepository
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || (args[0] != "old" && args[0] != "new"))
        {
            Console.Error.WriteLine("usage: (old|new) [--conn uri] [--db name] [--kill-after seconds] [--owner id] [--probe-budget seconds]");
            return 2;
        }

        var options = BenchmarkOptions.Parse(args);
        using var loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddFilter("Microsoft", LogLevel.Warning)
            .AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.TimestampFormat = "HH:mm:ss.fff ";
            }));
        var logger = loggerFactory.CreateLogger("Benchmark");

        // Same client timeouts as the server: MongoDBConfig defaults (SocketTimeout 60 s).
        var serverDefaults = new MongoDBConfig
        {
            ConnectionString = options.ConnectionString,
            DatabaseName = options.DatabaseName
        };
        var settings = MongoClientSettings.FromConnectionString(options.ConnectionString);
        settings.SocketTimeout = serverDefaults.SocketTimeout;
        settings.ConnectTimeout = serverDefaults.ConnectTimeout;
        settings.ServerSelectionTimeout = serverDefaults.ServerSelectionTimeout;
        var database = new MongoClient(settings).GetDatabase(options.DatabaseName);

        // Application traffic uses its own client, like other pods sharing the database.
        var probeDatabase = new MongoClient(options.ConnectionString).GetDatabase(options.DatabaseName);
        var probe = await AppTrafficProbe.CreateAsync(probeDatabase, logger);

        try
        {
            return options.Mode == "old"
                ? await Scenarios.RunOldAsync(database, probe, options, loggerFactory)
                : await Scenarios.RunNewAsync(database, probe, options, loggerFactory);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Benchmark failed");
            return 1;
        }
    }
}

public sealed record BenchmarkOptions(
    string Mode,
    string ConnectionString,
    string DatabaseName,
    int? KillAfterSeconds,
    string? OwnerId,
    int ProbeBudgetSeconds)
{
    public static BenchmarkOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            var index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        }

        var killAfter = Value("--kill-after");
        return new BenchmarkOptions(
            args[0],
            Value("--conn") ?? "mongodb://localhost:27099",
            Value("--db") ?? "xians_bench",
            killAfter == null ? null : int.Parse(killAfter),
            Value("--owner"),
            int.Parse(Value("--probe-budget") ?? "120"));
    }
}
