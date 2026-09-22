using System.Net;
using Features.AdminApi.Endpoints;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Secrets;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System Secret Vault agent authored with Xians.Lib. Creates, fetches, rotates, and deletes
/// secrets through the running supervisor, and proves fetch is a strict scope match:
/// tenant, agent, participant, and activation do not fall back to a broader secret.
/// Admin list/fetch return metadata only — never the value.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalSecretVaultAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalSecretVaultAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task SecretVaultAgent_CreateFetch_StrictScopeIsolationAndRotation()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Secrets {Guid.NewGuid():N}";
        var otherAgentName = $"Secrets Other {Guid.NewGuid():N}";
        const string ownerActivation = "front-desk";
        const string otherActivation = "back-office";
        var suffix = Guid.NewGuid().ToString("N");

        var tenantKey = $"openai-api-key-{suffix}";
        var tenantValue = $"sk-tenant-{suffix}";
        var tenantRotated = $"sk-rotated-{suffix}";
        var adminKey = $"stripe-webhook-{suffix}";
        var adminValue = $"whsec-{suffix}";
        var agentKey = $"agent-token-{suffix}";
        var agentValue = $"sk-agent-{suffix}";
        var activationKey = $"activation-hook-{suffix}";
        var activationValue = $"sk-activation-{suffix}";
        var participantKey = $"github-token-{suffix}";
        var participantValue = $"gho-{suffix}";
        var ownerParticipant = $"owner-{suffix}@example.com";
        var otherParticipant = $"other-{suffix}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var secretsAgent = RegisterSecretVaultAgent(host, agentName);
        var otherAgent = RegisterSecretVaultAgent(host, otherAgentName);
        await host.StartWorkersAsync(secretsAgent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);

        await DeployLibTemplateAsync(ownerTenant, agentName);
        await DeployLibTemplateAsync(otherTenant, agentName);
        await DeployLibTemplateAsync(ownerTenant, otherAgentName);

        var ownerActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, ownerActivation);
        var otherTenantActivationId = await ActivateLibAgentAsync(otherTenant, agentName, ownerActivation);
        var otherAgentActivationId = await ActivateLibAgentAsync(ownerTenant, otherAgentName, ownerActivation);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "created",
            userText: Command("create", "tenant", tenantKey, tenantValue));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, tenantValue,
            userText: Command("fetch", "tenant", tenantKey));
        await AssertAgentRepliesWithAsync(
            otherTenant, agentName, ownerActivation, "missing",
            userText: Command("fetch", "tenant", tenantKey));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "missing",
            userText: Command("fetch", "agent", tenantKey));
        await AssertAdminDoesNotExposeSecretValueAsync(ownerTenant, tenantKey, tenantValue);

        var adminCreate = await PostAsJsonAsync("/api/v1/admin/secrets", new SecretVaultCreateRequest
        {
            Key = adminKey,
            Value = adminValue,
            TenantId = ownerTenant
        });
        Assert.True(adminCreate.IsSuccessStatusCode, $"Admin create failed with {adminCreate.StatusCode}");
        Assert.DoesNotContain(adminValue, await adminCreate.Content.ReadAsStringAsync());
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, adminValue,
            userText: Command("fetch", "tenant", adminKey));
        await AssertAdminDoesNotExposeSecretValueAsync(ownerTenant, adminKey, adminValue);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "created",
            userText: Command("create", "agent", agentKey, agentValue));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, agentValue,
            userText: Command("fetch", "agent", agentKey));
        await AssertAgentRepliesWithAsync(
            ownerTenant, otherAgentName, ownerActivation, "missing",
            userText: Command("fetch", "agent", agentKey));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "missing",
            userText: Command("fetch", "tenant", agentKey));

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "created",
            userText: Command("create", "activation", activationKey, activationValue));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, activationValue,
            userText: Command("fetch", "activation", activationKey));
        var otherActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, otherActivation);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, otherActivation, "missing",
            userText: Command("fetch", "activation", activationKey));
        await AssertAgentRepliesWithAsync(
            ownerTenant, otherAgentName, ownerActivation, "missing",
            userText: Command("fetch", "activation", activationKey));

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "created",
            userText: Command("create", "participant", participantKey, participantValue),
            participantId: ownerParticipant);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, participantValue,
            userText: Command("fetch", "participant", participantKey),
            participantId: ownerParticipant);
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "missing",
            userText: Command("fetch", "participant", participantKey),
            participantId: otherParticipant);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "updated",
            userText: Command("update", "tenant", tenantKey, tenantRotated));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, tenantRotated,
            userText: Command("fetch", "tenant", tenantKey));
        await AssertAdminDoesNotExposeSecretValueAsync(ownerTenant, tenantKey, tenantRotated);

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "deleted",
            userText: Command("delete", "tenant", tenantKey));
        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, ownerActivation, "missing",
            userText: Command("fetch", "tenant", tenantKey));

        await RemoveLibActivationAsync(ownerTenant, ownerActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherActivationId);
        await RemoveLibActivationAsync(ownerTenant, otherAgentActivationId);
        await RemoveLibActivationAsync(otherTenant, otherTenantActivationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibDeploymentAsync(otherTenant, agentName);
        await RemoveLibDeploymentAsync(ownerTenant, otherAgentName);
        await RemoveLibTemplateAsync(agentName);
        await RemoveLibTemplateAsync(otherAgentName);
    }

    private static XiansAgent RegisterSecretVaultAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Creates and fetches Secret Vault values for the requested scope");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            try
            {
                await context.ReplyAsync(await SecretVaultChatCommands.ExecuteAsync(context.Message.Text));
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"error: {ex.Message}");
            }
        });

        return agent;
    }

    private async Task AssertAdminDoesNotExposeSecretValueAsync(string tenantId, string key, string value)
    {
        var list = await GetAsync($"/api/v1/admin/secrets?tenantId={Uri.EscapeDataString(tenantId)}");
        var listBody = await list.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        Assert.Contains(key, listBody);
        Assert.DoesNotContain(value, listBody);

        var fetch = await GetAsync(
            $"/api/v1/admin/secrets/fetch?key={Uri.EscapeDataString(key)}&tenantId={Uri.EscapeDataString(tenantId)}");
        var fetchBody = await fetch.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, fetch.StatusCode);
        Assert.Contains(key, fetchBody);
        Assert.DoesNotContain(value, fetchBody);
    }

    private static string Command(string verb, string scope, string key, string? value = null)
    {
        return value == null ? $"{verb} {scope} {key}" : $"{verb} {scope} {key} {value}";
    }

    private static class SecretVaultChatCommands
    {
        public static async Task<string> ExecuteAsync(string? text)
        {
            var parts = (text ?? string.Empty).Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return "unknown-command";
            }

            var command = parts[0];
            var vault = ResolveScope(parts[1]);
            var key = parts[2];

            if (command == "create" && parts.Length == 4)
            {
                await vault.CreateAsync(key, parts[3]);
                return "created";
            }

            if (command == "fetch")
            {
                var fetched = await vault.FetchByKeyAsync(key);
                return fetched?.Value ?? "missing";
            }

            if (command == "update" && parts.Length == 4)
            {
                await vault.UpdateAsync(await FindIdAsync(vault, key), value: parts[3]);
                return "updated";
            }

            if (command == "delete")
            {
                await vault.DeleteAsync(await FindIdAsync(vault, key));
                return "deleted";
            }

            return "unknown-command";
        }

        private static SecretVaultScopeBuilder ResolveScope(string dimension)
        {
            var vault = XiansContext.CurrentAgent.Secrets.TenantScope();
            return dimension switch
            {
                "tenant" => vault,
                "agent" => vault.AgentScope(),
                "participant" => vault.AgentScope().ParticipantScope(),
                "activation" => vault.AgentScope().ActivationScope(),
                _ => throw new InvalidOperationException($"Unknown secret scope '{dimension}'.")
            };
        }

        private static async Task<string> FindIdAsync(SecretVaultScopeBuilder vault, string key)
        {
            var item = (await vault.ListAsync()).FirstOrDefault(secret => secret.Key == key);
            if (item == null)
            {
                throw new InvalidOperationException($"Secret '{key}' was not in the list for this scope.");
            }

            return item.Id;
        }
    }
}
