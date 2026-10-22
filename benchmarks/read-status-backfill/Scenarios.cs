using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using Shared.Auth;
using Shared.Data;
using Shared.Providers;
using Shared.Repositories;
using Shared.Services;
using Shared.Utils;

namespace ReadStatusBackfillBenchmark;

public static class Scenarios
{
    private static readonly TimeSpan BaselineDuration = TimeSpan.FromSeconds(15);

    /// <summary>
    /// The original startup backfill: one UpdateMany over the whole collection, wrapped in
    /// MongoRetryHelper with maxRetries: 3, exactly as v3.40.5 ran it.
    /// </summary>
    public static async Task<int> RunOldAsync(
        IMongoDatabase database,
        AppTrafficProbe probe,
        BenchmarkOptions options,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("OldBackfill");
        var messages = database.GetCollection<ConversationMessage>("conversation_message");
        var filter = Builders<ConversationMessage>.Filter;
        var cannotBeUnread = filter.And(
            filter.Ne(x => x.Status, MessageStatus.Read),
            filter.Not(filter.And(
                filter.Eq(x => x.Direction, MessageDirection.Outgoing),
                filter.Or(
                    filter.Eq(x => x.MessageType, MessageType.Chat),
                    filter.Eq(x => x.MessageType, MessageType.File)))));
        var update = Builders<ConversationMessage>.Update.Set(x => x.Status, MessageStatus.Read);

        using var traffic = await probe.StartWithBaselineAsync(BaselineDuration);
        var started = Stopwatch.StartNew();
        string outcome;
        try
        {
            var modified = await MongoRetryHelper.ExecuteWithRetryAsync(async () =>
            {
                var result = await messages.UpdateManyAsync(cannotBeUnread, update);
                return result.ModifiedCount;
            }, logger, maxRetries: 3, baseDelayMs: 100, operationName: "BackfillMessagesThatCannotBeUnread");
            outcome = $"client finished, modified {modified}";
        }
        catch (Exception ex)
        {
            outcome = $"client gave up: {ex.GetType().Name}: {ex.Message}";
        }

        var clientElapsed = started.Elapsed;
        logger.LogInformation("Old backfill {Outcome} after {Seconds:F1} s", outcome, clientElapsed.TotalSeconds);

        var serverDone = await probe.WaitForServerUpdatesToFinishAsync();
        var totalElapsed = started.Elapsed;
        var report = traffic.Stop();

        Console.WriteLine();
        Console.WriteLine("=== OLD: single UpdateMany during startup ===");
        Console.WriteLine($"client wait            : {clientElapsed.TotalSeconds,8:F1} s ({outcome})");
        Console.WriteLine($"server still updating  : {(totalElapsed - clientElapsed).TotalSeconds,8:F1} s after the client stopped waiting ({(serverDone ? "finished" : "gave up waiting")})");
        Console.WriteLine($"startup probe budget   : {options.ProbeBudgetSeconds,8} s -> {(clientElapsed.TotalSeconds > options.ProbeBudgetSeconds ? "POD KILLED before startup finished" : "startup finished in time")}");
        report.Print();
        return 0;
    }

    /// <summary>
    /// The batched backfill exactly as ConversationMessageReadStatusBackfillService runs it.
    /// With --kill-after the process exits abruptly mid-run, like a SIGKILL'd pod.
    /// </summary>
    public static async Task<int> RunNewAsync(
        IMongoDatabase database,
        AppTrafficProbe probe,
        BenchmarkOptions options,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("NewBackfill");
        var repository = CreateRepository(database, loggerFactory);
        var ownerId = options.OwnerId ?? Guid.NewGuid().ToString("N");

        using var traffic = await probe.StartWithBaselineAsync(BaselineDuration);
        if (options.KillAfterSeconds is int killAfter)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(killAfter));
                Console.WriteLine($"=== KILLED after {killAfter} s (simulated pod kill, no cleanup) ===");
                Environment.Exit(137);
            });
        }

        var started = Stopwatch.StartNew();
        var updated = await repository.BackfillMessagesThatCannotBeUnreadOnceAsync(CancellationToken.None, ownerId);
        var elapsed = started.Elapsed;
        var report = traffic.Stop();
        var claim = await database.GetCollection<BsonDocument>("data_migrations")
            .Find(Builders<BsonDocument>.Filter.Eq("_id", ConversationRepository.ReadStatusBackfillMigrationId))
            .FirstOrDefaultAsync();

        Console.WriteLine();
        Console.WriteLine("=== NEW: batched background backfill ===");
        Console.WriteLine($"run time               : {elapsed.TotalSeconds,8:F1} s (startup is not waiting on this)");
        Console.WriteLine($"messages updated       : {updated,8}{(updated == 0 ? "  (0 means skipped: claim held by another owner, or already completed)" : string.Empty)}");
        Console.WriteLine($"claim                  : {claim?.ToJson() ?? "none"}");
        report.Print();
        return 0;
    }

    private static ConversationRepository CreateRepository(IMongoDatabase database, ILoggerFactory loggerFactory)
    {
        var databaseService = new Mock<IDatabaseService>();
        databaseService.Setup(x => x.GetDatabaseAsync()).ReturnsAsync(database);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["EncryptionKeys:UniqueSecrets:ConversationMessageKey"] = "benchmark-only-secret"
            })
            .Build();

        // The backfill only touches the database; the other dependencies are unused here.
        return new ConversationRepository(
            databaseService.Object,
            loggerFactory.CreateLogger<ConversationRepository>(),
            Mock.Of<ITenantContext>(),
            Mock.Of<ISecureEncryptionService>(),
            configuration,
            Mock.Of<IBackgroundTaskService>(),
            new MemoryCache(new MemoryCacheOptions()),
            Mock.Of<IIncomingOriginCache>(),
            Mock.Of<ICacheInvalidationBus>());
    }
}
