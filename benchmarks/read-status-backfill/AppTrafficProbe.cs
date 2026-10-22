using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ReadStatusBackfillBenchmark;

/// <summary>
/// Simulates normal application reads while a backfill runs, and samples how many update
/// operations the server is executing on conversation_message at the same time.
/// </summary>
public sealed class AppTrafficProbe
{
    private const int TrafficWorkers = 2;
    private static readonly TimeSpan TrafficInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan SampleInterval = TimeSpan.FromSeconds(1);

    private readonly IMongoDatabase _database;
    private readonly IMongoCollection<BsonDocument> _messages;
    private readonly List<(string TenantId, string ThreadId)> _threads;
    private readonly ILogger _logger;

    private AppTrafficProbe(IMongoDatabase database, List<(string, string)> threads, ILogger logger)
    {
        _database = database;
        _messages = database.GetCollection<BsonDocument>("conversation_message");
        _threads = threads;
        _logger = logger;
    }

    public static async Task<AppTrafficProbe> CreateAsync(IMongoDatabase database, ILogger logger)
    {
        var sampled = await database.GetCollection<BsonDocument>("conversation_message")
            .Aggregate()
            .Sample(500)
            .Project(new BsonDocument { { "tenant_id", 1 }, { "thread_id", 1 } })
            .ToListAsync();
        var threads = sampled
            .Select(doc => (doc["tenant_id"].AsString, doc["thread_id"].AsString))
            .ToList();
        return new AppTrafficProbe(database, threads, logger);
    }

    public async Task<RunningTraffic> StartWithBaselineAsync(TimeSpan baseline)
    {
        var running = new RunningTraffic(this);
        _logger.LogInformation("Measuring baseline app query latency for {Seconds} s", baseline.TotalSeconds);
        await Task.Delay(baseline);
        running.Phase = "during";
        return running;
    }

    /// <summary>
    /// A client-side timeout does not stop the server, so after the old backfill returns we wait
    /// until the server has no update left running on the collection.
    /// </summary>
    public async Task<bool> WaitForServerUpdatesToFinishAsync()
    {
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromMinutes(30))
        {
            if (await CountActiveUpdatesAsync() == 0)
            {
                return true;
            }

            await Task.Delay(SampleInterval);
        }

        return false;
    }

    private async Task<int> CountActiveUpdatesAsync()
    {
        var command = new BsonDocument
        {
            { "currentOp", 1 },
            { "active", true },
            { "op", "update" },
            { "ns", $"{_database.DatabaseNamespace.DatabaseName}.conversation_message" }
        };
        var result = await _database.Client.GetDatabase("admin").RunCommandAsync<BsonDocument>(command);
        return result["inprog"].AsBsonArray.Count;
    }

    private async Task RunQueriesAsync(RunningTraffic traffic, CancellationToken stop)
    {
        var random = new Random();
        var filter = Builders<BsonDocument>.Filter;
        while (!stop.IsCancellationRequested)
        {
            var (tenantId, threadId) = _threads[random.Next(_threads.Count)];
            var inThread = filter.And(filter.Eq("tenant_id", tenantId), filter.Eq("thread_id", threadId));

            await Measure(traffic, "thread history (50 msgs)", () => _messages
                .Find(inThread)
                .Sort(Builders<BsonDocument>.Sort.Descending("created_at"))
                .Limit(50)
                .ToListAsync(stop));

            await Measure(traffic, "unread count", () => _messages.CountDocumentsAsync(
                filter.And(
                    inThread,
                    filter.Eq("direction", "Outgoing"),
                    filter.In("message_type", new[] { "Chat", "File" }),
                    filter.Ne("status", "Read")),
                cancellationToken: stop));

            try
            {
                await Task.Delay(TrafficInterval, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static async Task Measure<T>(RunningTraffic traffic, string query, Func<Task<T>> run)
    {
        var timer = Stopwatch.StartNew();
        try
        {
            await run();
            traffic.Record(query, timer.Elapsed.TotalMilliseconds);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception)
        {
            traffic.RecordError(query);
        }
    }

    private async Task SampleServerAsync(RunningTraffic traffic, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            try
            {
                traffic.RecordConcurrentUpdates(await CountActiveUpdatesAsync());
                await Task.Delay(SampleInterval, stop);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "currentOp sample failed");
            }
        }
    }

    public sealed class RunningTraffic : IDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly ConcurrentDictionary<(string Phase, string Query), ConcurrentBag<double>> _latencies = new();
        private readonly ConcurrentDictionary<(string Phase, string Query), int> _errors = new();
        private readonly Task[] _workers;
        private int _maxConcurrentUpdates;

        public RunningTraffic(AppTrafficProbe probe)
        {
            _workers = Enumerable.Range(0, TrafficWorkers)
                .Select(_ => Task.Run(() => probe.RunQueriesAsync(this, _stop.Token)))
                .Append(Task.Run(() => probe.SampleServerAsync(this, _stop.Token)))
                .ToArray();
        }

        public string Phase { get; set; } = "baseline";

        public void Record(string query, double milliseconds) =>
            _latencies.GetOrAdd((Phase, query), _ => new ConcurrentBag<double>()).Add(milliseconds);

        public void RecordError(string query) =>
            _errors.AddOrUpdate((Phase, query), 1, (_, count) => count + 1);

        public void RecordConcurrentUpdates(int count)
        {
            if (Phase == "during" && count > _maxConcurrentUpdates)
            {
                _maxConcurrentUpdates = count;
            }
        }

        public TrafficReport Stop()
        {
            _stop.Cancel();
            Task.WaitAll(_workers, TimeSpan.FromSeconds(10));
            return new TrafficReport(
                _latencies.ToDictionary(pair => pair.Key, pair => pair.Value.OrderBy(ms => ms).ToArray()),
                _errors.ToDictionary(pair => pair.Key, pair => pair.Value),
                _maxConcurrentUpdates);
        }

        public void Dispose() => _stop.Cancel();
    }
}

public sealed record TrafficReport(
    Dictionary<(string Phase, string Query), double[]> Latencies,
    Dictionary<(string Phase, string Query), int> Errors,
    int MaxConcurrentServerUpdates)
{
    public void Print()
    {
        Console.WriteLine($"max concurrent server-side updates on conversation_message: {MaxConcurrentServerUpdates}");
        Console.WriteLine("app query latency (ms)         phase      count    p50     p95     p99     max   errors");
        foreach (var key in Latencies.Keys.OrderBy(k => k.Query).ThenBy(k => k.Phase))
        {
            var values = Latencies[key];
            Errors.TryGetValue(key, out var errors);
            Console.WriteLine(
                $"  {key.Query,-28} {key.Phase,-9} {values.Length,6} {Percentile(values, 0.50),7:F1} {Percentile(values, 0.95),7:F1} {Percentile(values, 0.99),7:F1} {values[^1],7:F1} {errors,7}");
        }
    }

    private static double Percentile(double[] sorted, double percentile)
    {
        var index = (int)Math.Ceiling(percentile * sorted.Length) - 1;
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }
}
