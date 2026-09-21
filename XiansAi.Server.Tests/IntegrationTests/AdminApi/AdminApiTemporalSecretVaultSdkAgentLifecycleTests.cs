using Temporalio.Activities;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Secrets;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System Secret Vault agent authored with Xians.Lib. Chat ExecuteAsync a Manage workflow
/// that calls TenantScope Create/Fetch/GetById/Update/List/Delete from a Temporal activity
/// and, in a second test, List/Delete from workflow code (plaintext CRUD is refused so the
/// value never enters Temporal history). See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/secret-vault/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalSecretVaultSdkAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalSecretVaultSdkAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public Task SecretVaultSdkAgent_CollectionOps_FromActivity()
        => RunCollectionCycleAsync(fromWorkflow: false);

    [Fact]
    public Task SecretVaultSdkAgent_CollectionOps_FromWorkflow()
        => RunCollectionCycleAsync(fromWorkflow: true);

    private async Task RunCollectionCycleAsync(bool fromWorkflow)
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"SecretSdk {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var expectedRun = fromWorkflow ? "run:ok:workflow" : "run:ok:activity";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var agent = RegisterSecretVaultSdkAgent(host, agentName, fromWorkflow);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(ownerTenant, agentName);
        var activationId = await ActivateLibAgentAsync(ownerTenant, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, expectedRun, userText: "run");

        await RemoveLibActivationAsync(ownerTenant, activationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterSecretVaultSdkAgent(
        LibAgentWorkflowHost host,
        string agentName,
        bool fromWorkflow)
    {
        var agent = host.RegisterTemplate(agentName, "Manages secrets through SecretVaultClient dual-context paths");
        var manage = agent.Workflows.DefineCustom<SecretVaultSdkWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:Manage");
        manage.AddActivity(new SecretVaultSdkActivities());

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context => HandleSecretVaultSdkChatAsync(context, fromWorkflow));
        return agent;
    }

    private static async Task HandleSecretVaultSdkChatAsync(UserMessageContext context, bool fromWorkflow)
    {
        var command = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            var result = await XiansContext.Workflows.ExecuteAsync<SecretVaultSdkWorkflow, string>(
                new object[] { command, fromWorkflow },
                uniqueKey: Guid.NewGuid().ToString("N"));
            await context.ReplyAsync(result);
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }
}

[Workflow("placeholder:SecretManage")]
public class SecretVaultSdkWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string command, bool fromWorkflow)
    {
        if (fromWorkflow)
        {
            return SecretVaultSdkDispatch.RunFromWorkflowAsync();
        }

        return Workflow.ExecuteActivityAsync(
            (SecretVaultSdkActivities activities) => activities.DispatchAsync(command),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });
    }
}

public class SecretVaultSdkActivities
{
    [Activity]
    public Task<string> DispatchAsync(string command)
    {
        return SecretVaultSdkDispatch.RunFromActivityAsync(command);
    }
}

internal static class SecretVaultSdkDispatch
{
    public static Task<string> RunFromActivityAsync(string command)
    {
        if (command == "run")
        {
            return RunActivityCrudAsync();
        }

        if (command == "seed")
        {
            return SeedAsync();
        }

        return Task.FromResult("unknown");
    }

    public static async Task<string> RunFromWorkflowAsync()
    {
        var seed = await Workflow.ExecuteActivityAsync(
            (SecretVaultSdkActivities activities) => activities.DispatchAsync("seed"),
            new ActivityOptions { StartToCloseTimeout = TimeSpan.FromSeconds(30) });

        var parts = seed.Split('|');
        if (parts.Length != 3 || parts[0] != "seed")
        {
            return $"run:bad-seed:{seed}";
        }

        var id = parts[1];
        var key = parts[2];
        var vault = CurrentVault();

        var listed = await vault.ListAsync();
        if (listed.All(item => item.Id != id || item.Key != key))
        {
            return "run:not-listed";
        }

        if (!await RefusesPlaintextAsync(() => vault.CreateAsync(key, "must-not-enter-history")) ||
            !await RefusesPlaintextAsync(() => vault.FetchByKeyAsync(key)) ||
            !await RefusesPlaintextAsync(() => vault.GetByIdAsync(id)) ||
            !await RefusesPlaintextAsync(() => vault.UpdateAsync(id, value: "must-not-enter-history")))
        {
            return "run:plaintext-allowed";
        }

        if (!await vault.DeleteAsync(id))
        {
            return "run:delete-failed";
        }

        var remaining = await vault.ListAsync();
        if (remaining.Any(item => item.Id == id))
        {
            return "run:still-listed";
        }

        return "run:ok:workflow";
    }

    private static async Task<string> RunActivityCrudAsync()
    {
        var vault = CurrentVault();
        var key = $"sdk-key-{Guid.NewGuid():N}";
        var original = $"sdk-val-{Guid.NewGuid():N}";
        var rotated = $"sdk-rot-{Guid.NewGuid():N}";

        var created = await vault.CreateAsync(key, original);
        var fetched = await vault.FetchByKeyAsync(key);
        if (fetched?.Value != original)
        {
            return "run:fetch-mismatch";
        }

        var listed = await vault.ListAsync();
        if (listed.All(item => item.Id != created.Id || item.Key != key))
        {
            return "run:not-listed";
        }

        var byId = await vault.GetByIdAsync(created.Id);
        if (byId?.Value != original || byId.Key != key)
        {
            return "run:get-mismatch";
        }

        await vault.UpdateAsync(created.Id, value: rotated);
        var updated = await vault.GetByIdAsync(created.Id);
        if (updated?.Value != rotated)
        {
            return "run:update-mismatch";
        }

        if (!await vault.DeleteAsync(created.Id))
        {
            return "run:delete-failed";
        }

        if (await vault.FetchByKeyAsync(key) != null || await vault.GetByIdAsync(created.Id) != null)
        {
            return "run:still-present";
        }

        return "run:ok:activity";
    }

    private static async Task<string> SeedAsync()
    {
        var key = $"sdk-key-{Guid.NewGuid():N}";
        var created = await CurrentVault().CreateAsync(key, $"sdk-val-{Guid.NewGuid():N}");
        return $"seed|{created.Id}|{key}";
    }

    private static SecretVaultScopeBuilder CurrentVault()
        => XiansContext.CurrentAgent.Secrets.TenantScope();

    private static async Task<bool> RefusesPlaintextAsync(Func<Task> action)
    {
        try
        {
            await action();
            return false;
        }
        catch (Exception ex)
        {
            return IsWorkflowForbidden(ex);
        }
    }

    private static bool IsWorkflowForbidden(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current.Message.Contains("cannot run inside a workflow", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
