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
        var options = new WorkflowEnvironmentStartLocalOptions
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

        _environment = await WorkflowEnvironment.StartLocalAsync(options);
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
