using Shared.Utils;
using Temporalio.Common;
using Temporalio.Testing;

namespace Tests.TestUtils;

/// <summary>
/// Starts one Temporal CLI/dev server for the process via
/// <see cref="WorkflowEnvironment.StartLocalAsync"/>. Tests that need a real Temporal
/// cluster opt in through <c>ICollectionFixture&lt;TemporalFixture&gt;</c>.
/// </summary>
public sealed class TemporalFixture : IAsyncLifetime
{
    private WorkflowEnvironment? _environment;

    public WorkflowEnvironment Environment =>
        _environment ?? throw new InvalidOperationException("Temporal local server has not started.");

    public string TargetHost =>
        Environment.Client.Connection.Options.TargetHost
        ?? throw new InvalidOperationException("Temporal local server has no target host.");

    public string Namespace =>
        string.IsNullOrWhiteSpace(Environment.Client.Options.Namespace)
            ? "default"
            : Environment.Client.Options.Namespace;

    public async Task InitializeAsync()
    {
        _environment = await StartAdditionalServerAsync();
    }

    /// <summary>
    /// Starts another local Temporal server with the same namespace and search attributes
    /// as the collection fixture. Used when a test assigns a tenant its own cluster.
    /// </summary>
    /// <remarks>
    /// Parallel suite shards download one shared CLI into the temp directory. Temporalio
    /// 1.13 gives up after 20 seconds while that file is still being written, so a shard
    /// that loses the race retries until the download finishes.
    /// </remarks>
    public static async Task<WorkflowEnvironment> StartAdditionalServerAsync()
    {
        const int maxAttempts = 6;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await WorkflowEnvironment.StartLocalAsync(CreateStartOptions());
            }
            catch (InvalidOperationException ex) when (
                attempt < maxAttempts && IsCliDownloadContended(ex))
            {
                Console.Error.WriteLine(
                    $"Temporal CLI download is still in progress in another process " +
                    $"(attempt {attempt} of {maxAttempts}). Waiting for it to finish.");
            }
        }
    }

    public async Task DisposeAsync()
    {
        if (_environment == null)
        {
            return;
        }

        await _environment.DisposeAsync();
        _environment = null;
    }

    private static WorkflowEnvironmentStartLocalOptions CreateStartOptions()
    {
        return new WorkflowEnvironmentStartLocalOptions
        {
            Namespace = "default",
            SearchAttributes =
            [
                SearchAttributeKey.CreateKeyword(Constants.TenantIdKey),
                SearchAttributeKey.CreateKeyword(Constants.AgentKey),
                SearchAttributeKey.CreateKeyword(Constants.UserIdKey),
                SearchAttributeKey.CreateKeyword(Constants.IdPostfixKey)
            ],
            DevServerOptions = CreateDevServerOptions()
        };
    }

    /// <summary>
    /// True when another process still holds <c>temporal-sdk-dotnet-*.downloading</c>.
    /// </summary>
    private static bool IsCliDownloadContended(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current.Message.Contains(".downloading", StringComparison.Ordinal)
                && current.Message.Contains("not complete", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static DevServerOptions CreateDevServerOptions()
    {
        var options = new DevServerOptions
        {
            LogLevel = "error"
        };

        var existingPath =
            System.Environment.GetEnvironmentVariable("XIANS_TEMPORAL_CLI_PATH")
            ?? System.Environment.GetEnvironmentVariable("TEMPORAL_CLI_PATH");

        if (!string.IsNullOrWhiteSpace(existingPath))
        {
            options.ExistingPath = existingPath;
        }

        return options;
    }
}
