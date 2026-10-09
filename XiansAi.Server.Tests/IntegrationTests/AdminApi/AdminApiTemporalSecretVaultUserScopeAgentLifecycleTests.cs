using System.Net;
using System.Text.Json;
using Features.AdminApi.Endpoints;
using Shared.Services;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Secrets;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Agent Studio creates two secret shapes: tenant-scoped (tenant id only) and user-scoped
/// (tenant id + participant id, no agent). A running Lib agent must read and write each shape
/// only at that scope, refuse an explicit other tenant or participant, and Admin callers must
/// not cross tenants or see the value.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalSecretVaultUserScopeAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalSecretVaultUserScopeAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task SecretVaultUserScopeAgent_AdminCreate_IsolatesTenantAndUser()
    {
        var run = await StartAgentsAsync();
        await using var host = run.Host;

        await CreateStudioSecretsAsync(run);
        await AssertRunningAgentRespectsTenantAndUserAsync(run);
        await AssertAgentWritesRespectScopeAsync(run);
        await AssertAgentCannotNameAnotherScopeAsync(run);
        await AssertAdminReadsMetadataOnlyAsync(run);
        await AssertTenantAdminCannotCrossTenantsAsync(run);
        await RemoveAgentsAsync(run);
    }

    private async Task<ScopeRun> StartAgentsAsync()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var suffix = Guid.NewGuid().ToString("N");
        var agentName = $"Secrets User {suffix}";
        var otherAgentName = $"Secrets User Other {suffix}";
        const string activation = "front-desk";

        var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var secretsAgent = RegisterUserScopeAgent(host, agentName);
        var otherAgent = RegisterUserScopeAgent(host, otherAgentName);
        await host.StartWorkersAsync(secretsAgent, otherAgent);
        await WaitForTemplateAsync(agentName);
        await WaitForTemplateAsync(otherAgentName);

        await DeployLibTemplateAsync(ownerTenant, agentName);
        await DeployLibTemplateAsync(otherTenant, agentName);
        await DeployLibTemplateAsync(ownerTenant, otherAgentName);

        return new ScopeRun
        {
            Host = host,
            OwnerTenant = ownerTenant,
            OtherTenant = otherTenant,
            AgentName = agentName,
            OtherAgentName = otherAgentName,
            Activation = activation,
            OwnerParticipant = $"owner-{suffix}@example.com",
            OtherParticipant = $"other-{suffix}@example.com",
            TenantKey = $"tenant-api-key-{suffix}",
            TenantValue = $"sk-tenant-{suffix}",
            OtherTenantValue = $"sk-other-tenant-{suffix}",
            UserKey = $"user-api-key-{suffix}",
            UserValue = $"sk-user-{suffix}",
            OtherUserValue = $"sk-other-user-{suffix}",
            AgentTenantKey = $"agent-tenant-key-{suffix}",
            AgentTenantValue = $"sk-agent-tenant-{suffix}",
            AgentUserKey = $"agent-user-key-{suffix}",
            AgentUserValue = $"sk-agent-user-{suffix}",
            OwnerActivationId = await ActivateLibAgentAsync(ownerTenant, agentName, activation),
            OtherTenantActivationId = await ActivateLibAgentAsync(otherTenant, agentName, activation),
            OtherAgentActivationId = await ActivateLibAgentAsync(ownerTenant, otherAgentName, activation)
        };
    }

    private async Task CreateStudioSecretsAsync(ScopeRun run)
    {
        var tenantSecret = await CreateSecretAsync(run.TenantKey, run.TenantValue, run.OwnerTenant, userId: null);
        var userSecret = await CreateSecretAsync(run.UserKey, run.UserValue, run.OwnerTenant, run.OwnerParticipant);
        Assert.Null(tenantSecret.UserId);
        Assert.Null(tenantSecret.AgentId);
        Assert.Equal(run.OwnerParticipant, userSecret.UserId);
        Assert.Null(userSecret.AgentId);
        Assert.Equal(run.OwnerTenant, userSecret.TenantId);
        run.TenantSecretId = tenantSecret.Id;
        run.UserSecretId = userSecret.Id;

        var shadowValue = $"sk-shadow-{Guid.NewGuid():N}";
        var sameKeyOtherUser = await PostAsJsonAsync("/api/v1/admin/secrets", new SecretVaultCreateRequest
        {
            Key = run.TenantKey,
            Value = shadowValue,
            TenantId = run.OwnerTenant,
            UserId = run.OtherParticipant
        });
        Assert.Equal(HttpStatusCode.Conflict, sameKeyOtherUser.StatusCode);
        Assert.DoesNotContain(shadowValue, await sameKeyOtherUser.Content.ReadAsStringAsync());

        await CreateSecretAsync(run.TenantKey, run.OtherTenantValue, run.OtherTenant, userId: null);
        var otherUserSecret = await CreateSecretAsync(
            run.UserKey, run.OtherUserValue, run.OtherTenant, run.OwnerParticipant);
        run.OtherUserSecretId = otherUserSecret.Id;
    }

    private async Task AssertRunningAgentRespectsTenantAndUserAsync(ScopeRun run)
    {
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "tenant", run.TenantKey, run.TenantValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "user", run.UserKey, run.UserValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "tenant", run.UserKey, Missing("tenant", run.UserKey), run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "user", run.TenantKey, Missing("user", run.TenantKey), run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "agent", run.UserKey, Missing("agent", run.UserKey), run.OwnerParticipant);

        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "tenant", run.TenantKey, run.TenantValue, run.OtherParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "user", run.UserKey, Missing("user", run.UserKey), run.OtherParticipant);

        await AssertFetchAsync(run, run.OtherTenant, run.AgentName, "tenant", run.TenantKey, run.OtherTenantValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OtherTenant, run.AgentName, "user", run.UserKey, run.OtherUserValue, run.OwnerParticipant);

        await AssertFetchAsync(run, run.OwnerTenant, run.OtherAgentName, "tenant", run.TenantKey, run.TenantValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.OtherAgentName, "user", run.UserKey, run.UserValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.OtherAgentName, "user", run.UserKey, Missing("user", run.UserKey), run.OtherParticipant);
    }

    private async Task AssertAgentWritesRespectScopeAsync(ScopeRun run)
    {
        await AssertAgentRepliesWithAsync(
            run.OwnerTenant,
            run.AgentName,
            run.Activation,
            CreatedShape("tenant", run.AgentTenantKey, userId: "none"),
            userText: $"create tenant {run.AgentTenantKey} {run.AgentTenantValue}",
            participantId: run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "tenant", run.AgentTenantKey, run.AgentTenantValue, run.OtherParticipant);
        await AssertFetchAsync(run, run.OtherTenant, run.AgentName, "tenant", run.AgentTenantKey, Missing("tenant", run.AgentTenantKey), run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.OtherAgentName, "tenant", run.AgentTenantKey, run.AgentTenantValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "user", run.AgentTenantKey, Missing("user", run.AgentTenantKey), run.OwnerParticipant);

        await AssertAgentRepliesWithAsync(
            run.OwnerTenant,
            run.AgentName,
            run.Activation,
            CreatedShape("user", run.AgentUserKey, run.OwnerParticipant),
            userText: $"create user {run.AgentUserKey} {run.AgentUserValue}",
            participantId: run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "user", run.AgentUserKey, run.AgentUserValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "user", run.AgentUserKey, Missing("user", run.AgentUserKey), run.OtherParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.OtherAgentName, "user", run.AgentUserKey, run.AgentUserValue, run.OwnerParticipant);
        await AssertFetchAsync(run, run.OwnerTenant, run.AgentName, "tenant", run.AgentUserKey, Missing("tenant", run.AgentUserKey), run.OwnerParticipant);
        await AssertFetchAsync(run, run.OtherTenant, run.AgentName, "user", run.AgentUserKey, Missing("user", run.AgentUserKey), run.OwnerParticipant);
    }

    private async Task AssertAgentCannotNameAnotherScopeAsync(ScopeRun run)
    {
        await AssertAgentRepliesWithAsync(
            run.OwnerTenant,
            run.AgentName,
            run.Activation,
            $"error: Secret Vault tenantId scope '{run.OtherTenant}'",
            userText: $"escape tenant {run.OtherTenant} {run.TenantKey}",
            participantId: run.OwnerParticipant);
        await AssertAgentRepliesWithAsync(
            run.OwnerTenant,
            run.AgentName,
            run.Activation,
            $"error: Secret Vault userId scope '{run.OtherParticipant}'",
            userText: $"escape user {run.OtherParticipant} {run.UserKey}",
            participantId: run.OwnerParticipant);
    }

    private async Task AssertAdminReadsMetadataOnlyAsync(ScopeRun run)
    {
        await AssertListHidesValuesAsync(run.OwnerTenant, run, run.UserSecretId, run.OtherUserSecretId);
        await AssertListHidesValuesAsync(run.OtherTenant, run, run.OtherUserSecretId, run.UserSecretId);

        var tenantFetch = await FetchMetadataAsync(run.TenantKey, run.OwnerTenant, userId: null);
        Assert.Equal(HttpStatusCode.OK, tenantFetch.StatusCode);
        var (tenantMetadata, tenantBody) = await ReadMetadataAndBodyAsync(tenantFetch);
        Assert.Equal(run.TenantSecretId, tenantMetadata.Id);
        Assert.Null(tenantMetadata.UserId);
        AssertBodyHidesValues(tenantBody, run);

        var userWithoutParticipant = await FetchMetadataAsync(run.UserKey, run.OwnerTenant, userId: null);
        Assert.Equal(HttpStatusCode.NotFound, userWithoutParticipant.StatusCode);

        var wrongParticipant = await FetchMetadataAsync(run.UserKey, run.OwnerTenant, run.OtherParticipant);
        Assert.Equal(HttpStatusCode.NotFound, wrongParticipant.StatusCode);

        var userFetch = await FetchMetadataAsync(run.UserKey, run.OwnerTenant, run.OwnerParticipant);
        Assert.Equal(HttpStatusCode.OK, userFetch.StatusCode);
        var (userMetadata, userBody) = await ReadMetadataAndBodyAsync(userFetch);
        Assert.Equal(run.UserSecretId, userMetadata.Id);
        Assert.Equal(run.OwnerParticipant, userMetadata.UserId);
        Assert.Null(userMetadata.AgentId);
        AssertBodyHidesValues(userBody, run);

        var crossTenantFetch = await FetchMetadataAsync(run.UserKey, run.OtherTenant, run.OwnerParticipant);
        Assert.Equal(HttpStatusCode.OK, crossTenantFetch.StatusCode);
        var (crossTenantMetadata, crossTenantBody) = await ReadMetadataAndBodyAsync(crossTenantFetch);
        Assert.Equal(run.OtherUserSecretId, crossTenantMetadata.Id);
        AssertBodyHidesValues(crossTenantBody, run);

        var get = await GetAsync($"/api/v1/admin/secrets/{run.UserSecretId}");
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var (_, getBody) = await ReadMetadataAndBodyAsync(get);
        AssertBodyHidesValues(getBody, run);
    }

    private async Task AssertTenantAdminCannotCrossTenantsAsync(ScopeRun run)
    {
        await ConfigureAdminApiClientAsync(run.OtherTenant, SystemRoles.TenantAdmin);

        var spoofedCreate = await PostAsJsonAsync("/api/v1/admin/secrets", new SecretVaultCreateRequest
        {
            Key = $"spoof-{Guid.NewGuid():N}",
            Value = run.UserValue,
            TenantId = run.OwnerTenant,
            UserId = run.OwnerParticipant
        });
        Assert.Equal(HttpStatusCode.Forbidden, spoofedCreate.StatusCode);

        var listOther = await GetAsync($"/api/v1/admin/secrets?tenantId={Uri.EscapeDataString(run.OwnerTenant)}");
        Assert.Equal(HttpStatusCode.Unauthorized, listOther.StatusCode);

        var fetchOther = await FetchMetadataAsync(run.UserKey, run.OwnerTenant, run.OwnerParticipant);
        Assert.Equal(HttpStatusCode.Unauthorized, fetchOther.StatusCode);

        var getOther = await GetAsync($"/api/v1/admin/secrets/{run.UserSecretId}");
        Assert.Equal(HttpStatusCode.Forbidden, getOther.StatusCode);

        var updateOther = await PutAsJsonAsync($"/api/v1/admin/secrets/{run.UserSecretId}", new SecretVaultUpdateRequest
        {
            Value = $"sk-stolen-{Guid.NewGuid():N}",
            TenantId = run.OwnerTenant,
            UserId = run.OtherParticipant
        });
        Assert.Equal(HttpStatusCode.Forbidden, updateOther.StatusCode);

        var deleteOther = await DeleteAsync($"/api/v1/admin/secrets/{run.UserSecretId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteOther.StatusCode);

        var ownList = await GetAsync($"/api/v1/admin/secrets?tenantId={Uri.EscapeDataString(run.OtherTenant)}");
        Assert.Equal(HttpStatusCode.OK, ownList.StatusCode);
        var ownListBody = await ownList.Content.ReadAsStringAsync();
        Assert.Contains(run.OtherUserSecretId, ownListBody);
        Assert.DoesNotContain(run.UserSecretId, ownListBody);
        Assert.DoesNotContain(run.TenantSecretId, ownListBody);
        AssertBodyHidesValues(ownListBody, run);

        await ConfigureAdminApiClientAsync(run.OwnerTenant, SystemRoles.TenantAdmin);
        await AssertListHidesValuesAsync(run.OwnerTenant, run, run.UserSecretId, run.OtherUserSecretId);

        var stillThere = await GetAsync($"/api/v1/admin/secrets/{run.UserSecretId}");
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
        var (stillThereMetadata, stillThereBody) = await ReadMetadataAndBodyAsync(stillThere);
        Assert.Equal(run.OwnerParticipant, stillThereMetadata.UserId);
        AssertBodyHidesValues(stillThereBody, run);
    }

    private async Task RemoveAgentsAsync(ScopeRun run)
    {
        await ConfigureAdminApiClientAsync(run.OwnerTenant);
        await RemoveLibActivationAsync(run.OwnerTenant, run.OwnerActivationId);
        await RemoveLibActivationAsync(run.OwnerTenant, run.OtherAgentActivationId);
        await RemoveLibActivationAsync(run.OtherTenant, run.OtherTenantActivationId);
        await RemoveLibDeploymentAsync(run.OwnerTenant, run.AgentName);
        await RemoveLibDeploymentAsync(run.OtherTenant, run.AgentName);
        await RemoveLibDeploymentAsync(run.OwnerTenant, run.OtherAgentName);
        await RemoveLibTemplateAsync(run.AgentName);
        await RemoveLibTemplateAsync(run.OtherAgentName);
    }

    private async Task<SecretVaultMetadataResponse> CreateSecretAsync(
        string key,
        string value,
        string tenantId,
        string? userId)
    {
        var response = await PostAsJsonAsync("/api/v1/admin/secrets", new SecretVaultCreateRequest
        {
            Key = key,
            Value = value,
            TenantId = tenantId,
            UserId = userId
        });
        Assert.True(response.IsSuccessStatusCode, $"Create '{key}' failed with {response.StatusCode}");
        var (created, body) = await ReadMetadataAndBodyAsync(response);
        Assert.DoesNotContain(value, body);
        return created;
    }

    private async Task AssertFetchAsync(
        ScopeRun run,
        string tenantId,
        string agentName,
        string scope,
        string key,
        string expected,
        string participantId)
    {
        await AssertAgentRepliesWithAsync(
            tenantId,
            agentName,
            run.Activation,
            expected,
            userText: $"fetch {scope} {key}",
            participantId: participantId);
    }

    private async Task AssertListHidesValuesAsync(
        string tenantId,
        ScopeRun run,
        string presentSecretId,
        string absentSecretId)
    {
        var list = await GetAsync($"/api/v1/admin/secrets?tenantId={Uri.EscapeDataString(tenantId)}");
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var body = await list.Content.ReadAsStringAsync();
        Assert.Contains(presentSecretId, body);
        Assert.DoesNotContain(absentSecretId, body);
        AssertBodyHidesValues(body, run);
    }

    private Task<HttpResponseMessage> FetchMetadataAsync(string key, string tenantId, string? userId)
    {
        var query =
            $"key={Uri.EscapeDataString(key)}&tenantId={Uri.EscapeDataString(tenantId)}";
        if (!string.IsNullOrEmpty(userId))
        {
            query += $"&userId={Uri.EscapeDataString(userId)}";
        }

        return GetAsync($"/api/v1/admin/secrets/fetch?{query}");
    }

    private async Task<(SecretVaultMetadataResponse Metadata, string Body)> ReadMetadataAndBodyAsync(
        HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var metadata = JsonSerializer.Deserialize<SecretVaultMetadataResponse>(body, JsonReadOptions);
        Assert.NotNull(metadata);
        return (metadata!, body);
    }

    private static void AssertBodyHidesValues(string body, ScopeRun run)
    {
        Assert.DoesNotContain(run.TenantValue, body);
        Assert.DoesNotContain(run.OtherTenantValue, body);
        Assert.DoesNotContain(run.UserValue, body);
        Assert.DoesNotContain(run.OtherUserValue, body);
        Assert.DoesNotContain(run.AgentTenantValue, body);
        Assert.DoesNotContain(run.AgentUserValue, body);
    }

    private static string Missing(string scope, string key) => $"missing:{scope}:{key}";

    private static string CreatedShape(string scope, string key, string userId)
        => $"created:{scope}:{key}:user={userId}:agent=none";

    private static XiansAgent RegisterUserScopeAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Fetches tenant-scoped and user-scoped Secret Vault values");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            try
            {
                await context.ReplyAsync(await UserScopeChatCommands.ExecuteAsync(context.Message.Text));
            }
            catch (Exception ex)
            {
                await context.ReplyAsync($"error: {ex.Message}");
            }
        });

        return agent;
    }

    private sealed class ScopeRun
    {
        public required LibAgentWorkflowHost Host { get; init; }
        public required string OwnerTenant { get; init; }
        public required string OtherTenant { get; init; }
        public required string AgentName { get; init; }
        public required string OtherAgentName { get; init; }
        public required string Activation { get; init; }
        public required string OwnerParticipant { get; init; }
        public required string OtherParticipant { get; init; }
        public required string TenantKey { get; init; }
        public required string TenantValue { get; init; }
        public required string OtherTenantValue { get; init; }
        public required string UserKey { get; init; }
        public required string UserValue { get; init; }
        public required string OtherUserValue { get; init; }
        public required string AgentTenantKey { get; init; }
        public required string AgentTenantValue { get; init; }
        public required string AgentUserKey { get; init; }
        public required string AgentUserValue { get; init; }
        public required string OwnerActivationId { get; init; }
        public required string OtherTenantActivationId { get; init; }
        public required string OtherAgentActivationId { get; init; }
        public string TenantSecretId { get; set; } = "";
        public string UserSecretId { get; set; } = "";
        public string OtherUserSecretId { get; set; } = "";
    }

    private static class UserScopeChatCommands
    {
        public static async Task<string> ExecuteAsync(string? text)
        {
            var parts = (text ?? string.Empty).Split(' ', 4, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 3)
            {
                return "unknown-command";
            }

            if (parts[0] == "create" && parts.Length == 4)
            {
                var created = await ResolveScope(parts[1]).CreateAsync(parts[2], parts[3]);
                var userId = created.UserId ?? "none";
                var agentId = created.AgentId ?? "none";
                return $"created:{parts[1]}:{created.Key}:user={userId}:agent={agentId}";
            }

            if (parts[0] == "escape" && parts.Length == 4)
            {
                var fetched = await ResolveEscape(parts[1], parts[2]).FetchByKeyAsync(parts[3]);
                return fetched?.Value ?? $"escaped:{parts[1]}:{parts[3]}";
            }

            if (parts[0] != "fetch" || parts.Length != 3)
            {
                return "unknown-command";
            }

            var scope = parts[1];
            var key = parts[2];
            var fetchedByScope = await ResolveScope(scope).FetchByKeyAsync(key);
            return fetchedByScope?.Value ?? $"missing:{scope}:{key}";
        }

        private static SecretVaultScopeBuilder ResolveEscape(string dimension, string foreignId)
        {
            var vault = XiansContext.CurrentAgent.Secrets.TenantScope();
            return dimension switch
            {
                "tenant" => vault.TenantScope(foreignId),
                "user" => vault.ParticipantScope(foreignId),
                _ => throw new InvalidOperationException($"Unknown secret scope '{dimension}'.")
            };
        }

        private static SecretVaultScopeBuilder ResolveScope(string scope)
        {
            var vault = XiansContext.CurrentAgent.Secrets.TenantScope();
            return scope switch
            {
                "tenant" => vault,
                "user" => vault.ParticipantScope(),
                "agent" => vault.AgentScope(),
                _ => throw new InvalidOperationException($"Unknown secret scope '{scope}'.")
            };
        }
    }
}
