using System.Net;
using Features.AdminApi.Auth;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Regression coverage for the 15 groups migrated onto the capability matrix alongside
/// <c>tenant.users.*</c>/<c>global.users.*</c> (see <see cref="AdminCapabilityEnforcementTests"/> for
/// those two, which already proved the mechanism itself).
///
/// This migration reuses that same proven mechanism ~91 more times rather than introducing a new
/// one, so what needs proving here is narrower: that each route was wired to the <em>right</em>
/// action. A typo in an action name is a compile error (the constants are checked), so the residual
/// risk is a route wired to the wrong — but valid — action, which only a real HTTP call per route can
/// catch. Coverage is calibrated to that risk: every route in the smaller, higher-consequence
/// SysAdmin-only groups (a mis-wire there is a privilege escalation), and two representative routes
/// per larger open group (within one file every open route shares one default, so a mis-wire is as
/// likely to be caught by the first route tested as the ninth).
///
/// Two streaming routes (<c>messaging/listen</c>, <c>workflows/events/stream</c>) are excluded from
/// direct HTTP calls: both hold the connection open as <c>text/event-stream</c>, and the capability
/// filter runs identically ahead of that streaming, already proven by every sibling route in the same
/// group.
/// </summary>
public class AdminCapabilityMigratedRoutesTests : AdminApiIntegrationTestBase
{
    private const string MatrixRoute = "/api/v1/admin/admin-console/capability-matrix";

    public AdminCapabilityMigratedRoutesTests(MongoDbFixture mongoDbFixture) : base(mongoDbFixture)
    {
    }

    private async Task SeedEveryActionToNobodyAsync()
    {
        foreach (var action in CapabilityActions.All.Where(a => !a.NonDelegable))
        {
            var response = await PutAsJsonAsync($"{MatrixRoute}/{action.Name}", new { allowedRoles = Array.Empty<string>() });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }

    /// <summary>
    /// Routes that carried no role check before migration (now default <c>[TenantAdmin]</c>): two
    /// representative calls per group, or every route for a group with two or fewer. Unknown ids are
    /// used throughout — what matters is that the response is never 403; a 404/400/500 downstream is
    /// proof the request reached the handler.
    /// </summary>
    private List<(string Description, Func<string, Task<HttpResponseMessage>> Call)> OpenRoutes() =>
    [
        ("agentActivations.list", t => GetAsync($"/api/v1/admin/tenants/{t}/agentActivations")),
        ("agentActivations.delete", t => DeleteAsync($"/api/v1/admin/tenants/{t}/agentActivations/unknown-id")),

        ("data.schema", t => GetAsync($"/api/v1/admin/tenants/{t}/data/schema?startDate=2024-01-01&endDate=2024-01-02&agentName=test-agent")),
        ("data.deleteRecord", t => DeleteAsync($"/api/v1/admin/tenants/{t}/data/unknown-record")),

        ("feedback.list", t => GetAsync($"/api/v1/admin/tenants/{t}/feedback")),
        ("feedback.submit", t => PostAsJsonAsync($"/api/v1/admin/tenants/{t}/feedback",
            new { messageId = "m", threadId = "th", agentName = "test-agent", workflowId = "w", workflowType = "wt", rating = 4 })),

        ("heartbeat.check", t => GetAsync($"/api/v1/admin/tenants/{t}/heartbeat?agentName=test-agent&activationName=test-activation")),

        ("logs.streams", t => GetAsync($"/api/v1/admin/tenants/{t}/logs/streams")),
        ("logs.deleteByActivation", t => DeleteAsync($"/api/v1/admin/tenants/{t}/logs/agents/test-agent/activation/test-activation")),

        ("messaging.topics", t => GetAsync($"/api/v1/admin/tenants/{t}/messaging/topics?agentName=test-agent&activationName=test-activation")),
        ("messaging.send", t => PostAsJsonAsync($"/api/v1/admin/tenants/{t}/messaging/send",
            new { agentName = "test-agent", activationName = "test-activation", participantId = "p", text = "hi" })),

        ("metrics.stats", t => GetAsync($"/api/v1/admin/tenants/{t}/metrics/stats?agentName=test-agent&startDate=2024-01-01&endDate=2024-01-02")),
        ("metrics.deleteByActivation", t => DeleteAsync($"/api/v1/admin/tenants/{t}/metrics/agents/test-agent/activation/test-activation")),

        ("schedules.list", t => GetAsync($"/api/v1/admin/tenants/{t}/agents/test-agent/schedules")),
        // Not schedules.deleteAll: that route checks agent-level write permission on a real agent
        // before it ever reaches schedule logic, so a fake agent name 403s there for a reason
        // unrelated to the capability matrix. schedules.delete (by id) 404s on a fake id instead,
        // which is what proves the filter ran without tripping that unrelated check.
        ("schedules.delete", t => DeleteAsync($"/api/v1/admin/tenants/{t}/agents/test-agent/schedules/by-id?scheduleId=unknown-schedule")),

        ("stats.get", t => GetAsync($"/api/v1/admin/tenants/{t}/stats")),

        ("tasks.list", t => GetAsync($"/api/v1/admin/tenants/{t}/tasks")),
        ("tasks.updateDraft", t => PutAsJsonAsync($"/api/v1/admin/tenants/{t}/tasks/draft?taskId=unknown", new { updatedDraft = new { } })),

        ("workflows.list", t => GetAsync($"/api/v1/admin/tenants/{t}/workflows/list")),
        ("workflows.cancel", t => PostAsJsonAsync($"/api/v1/admin/tenants/{t}/workflows/cancel?workflowId=unknown&force=false", new { })),

        ("agentDeployments.list", t => GetAsync($"/api/v1/admin/tenants/{t}/agentDeployments")),
        ("agentDeployments.delete", t => DeleteAsync($"/api/v1/admin/tenants/{t}/agentDeployments/unknown-agent")),

        // Carried no role check at all before migration (scoped by AuthorizedTenantIds instead).
        ("tenant.theme.get", t => GetAsync($"/api/v1/admin/tenants/{t}/theme")),
        ("tenant.theme.set", t => PutAsJsonAsync($"/api/v1/admin/tenants/{t}/theme", new { theme = "lingon" })),
        ("tenant.logo.get", t => GetAsync($"/api/v1/admin/tenants/{t}/logo")),
        ("tenant.logo.set", t => PutAsJsonAsync($"/api/v1/admin/tenants/{t}/logo", new { url = "https://example.com/logo.png" })),

        // Migrated off TenantAdminOrSysAdminOnlyFilter onto the capability matrix (formerly
        // Category 2): one representative route per group, since every route in a group shares one
        // default. userId is set to the caller's own id where the route also enforces a separate
        // "act on behalf of" check (CanActOnBehalfOf), unrelated to the capability matrix.
        ("agentCertificates.list", t => GetAsync($"/api/v1/admin/tenants/{t}/agent-certificates?userId={_adminUserId}")),
        ("adminApiKeys.list", t => GetAsync($"/api/v1/admin/tenants/{t}/admin-apikeys?userId={_adminUserId}")),
        ("integrationMetadata.list", _ => GetAsync("/api/v1/admin/integrations/metadata/types")),
        ("webhooks.list", t => GetAsync($"/api/v1/admin/tenants/{t}/webhooks")),
        ("integrations.list", t => GetAsync($"/api/v1/admin/tenants/{t}/integrations")),
        ("secrets.list", _ => GetAsync("/api/v1/admin/secrets")),
        ("auditLogs.list", t => GetAsync($"/api/v1/admin/tenants/{t}/audit-logs")),

        // Also migrated off TenantAdminOrSysAdminOnlyFilter, but as NonDelegable (fixed
        // TenantAdmin-or-SysAdmin, not runtime-editable) rather than an ordinary delegable action —
        // still reachable by TenantAdmin against an untouched matrix like every other entry here, and
        // SeedEveryActionToNobodyAsync() already skips NonDelegable actions, so they are unaffected
        // by that seeding either.
        ("agentAccess.access", t => GetAsync($"/api/v1/admin/tenants/{t}/agent-access?user=unknown-user")),
        ("ownership.get", t => GetAsync($"/api/v1/admin/tenants/{t}/agents/unknown-agent/ownership")),
        ("templates.access", _ => GetAsync("/api/v1/admin/agentTemplates")),
    ];

    /// <summary>
    /// Every route that was SysAdmin-only before migration (now default <c>[]</c>) — a mis-wire here
    /// widens a privilege boundary, so every route is exercised rather than a sample.
    ///
    /// <c>tenants.delete</c> is deliberately last: it is the one call in this list that removes the
    /// shared test tenant every other entry targets, so anything after it would see 404s from
    /// <c>TenantNotFoundException</c> rather than genuinely reaching its handler (still not 403 —
    /// the assertion would still hold — but the check would be weaker).
    /// </summary>
    private List<(string Description, Func<string, Task<HttpResponseMessage>> Call)> SysAdminOnlyRoutes() =>
    [
        ("tenants.list", _ => GetAsync("/api/v1/admin/tenants")),
        ("tenants.get", t => GetAsync($"/api/v1/admin/tenants/{t}")),
        ("tenants.create", _ => PostAsJsonAsync("/api/v1/admin/tenants",
            new { tenantId = $"probe-{Guid.NewGuid()}", name = "Probe" })),
        ("tenants.update", t => PatchAsJsonAsync($"/api/v1/admin/tenants/{t}", new { name = "Renamed" })),
        ("tenants.metadata.list", t => GetAsync($"/api/v1/admin/tenants/{t}/metadata")),
        ("tenants.metadata.get", t => GetAsync($"/api/v1/admin/tenants/{t}/metadata/unknown-key")),
        ("tenants.metadata.upsert", t => PutAsJsonAsync($"/api/v1/admin/tenants/{t}/metadata/probe-key",
            new { value = "v", type = "PlainText" })),
        ("tenants.metadata.delete", t => DeleteAsync($"/api/v1/admin/tenants/{t}/metadata/unknown-key")),

        ("tenant.temporalConfig.get", t => GetAsync($"/api/v1/admin/tenants/{t}/temporal-config")),
        ("tenant.temporalConfig.set (PUT)", t => PutAsJsonAsync($"/api/v1/admin/tenants/{t}/temporal-config",
            new { tenantId = t, serverUrl = "temporal.example.com:7233", @namespace = "default" })),
        ("tenant.temporalConfig.set (POST)", t => PostAsJsonAsync($"/api/v1/admin/tenants/{t}/temporal-config",
            new { tenantId = t, serverUrl = "temporal.example.com:7233", @namespace = "default" })),
        // RevertTenantTemporalConfig's handler signature still requires a body (a pre-existing
        // quirk unrelated to this migration: the fields go unused), so an empty body would 400
        // during model binding before any endpoint filter runs, masking the capability check.
        ("tenant.temporalConfig.revert", t => PostAsJsonAsync($"/api/v1/admin/tenants/{t}/temporal-config/revert",
            new { tenantId = t, serverUrl = "temporal.example.com:7233", @namespace = "default" })),
        ("tenant.temporalConfig.testConnection", t => PostAsJsonAsync($"/api/v1/admin/tenants/{t}/temporal-config/test-connection",
            new { tenantId = t, serverUrl = "temporal.example.com:7233", @namespace = "default" })),

        ("tenant.oidcConfig.get", t => GetAsync($"/api/v1/admin/tenants/{t}/oidc-config")),
        ("tenant.oidcConfig.upsert (POST)", t => PostAsJsonAsync($"/api/v1/admin/tenants/{t}/oidc-config", new { })),
        ("tenant.oidcConfig.upsert (PUT)", t => PutAsJsonAsync($"/api/v1/admin/tenants/{t}/oidc-config", new { })),
        ("tenant.oidcConfig.delete", t => DeleteAsync($"/api/v1/admin/tenants/{t}/oidc-config")),
        ("tenant.oidcConfig.template", t => GetAsync($"/api/v1/admin/tenants/{t}/oidc-config/template")),

        ("workerDeployments.list", t => GetAsync($"/api/v1/admin/tenants/{t}/worker-deployments")),
        ("workerDeployments.get", t => GetAsync($"/api/v1/admin/tenants/{t}/worker-deployments/unknown-deployment")),
        ("workerDeployments.setCurrentVersion", t => PostAsJsonAsync(
            $"/api/v1/admin/tenants/{t}/worker-deployments/unknown-deployment/set-current-version",
            new { buildId = "1.0.0" })),
        ("workerDeployments.setRampingVersion", t => PostAsJsonAsync(
            $"/api/v1/admin/tenants/{t}/worker-deployments/unknown-deployment/set-ramping-version",
            new { buildId = "1.0.0", percentage = 10 })),

        ("participants.getByEmail", _ => GetAsync("/api/v1/admin/participants/unknown@example.com")),
        ("participants.getByUserId", _ => GetAsync("/api/v1/admin/participants/by-user-id/unknown-user")),

        ("agentDeployments.promoteToTemplate", t => PostAsJsonAsync(
            $"/api/v1/admin/tenants/{t}/agentDeployments/unknown-agent/promote-to-template", new { })),

        // Last: deletes the shared test tenant (see doc comment above).
        ("tenants.delete", t => DeleteAsync($"/api/v1/admin/tenants/{t}")),
    ];

    [Fact]
    public async Task TenantAdmin_ReachesEveryPreviouslyOpenRoute_AgainstAnUntouchedMatrix()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);

        foreach (var (description, call) in OpenRoutes())
        {
            var response = await call(tenantId);
            Assert.False(response.StatusCode == HttpStatusCode.Forbidden,
                $"{description} unexpectedly forbidden for TenantAdmin against an untouched matrix (got {response.StatusCode}).");
        }
    }

    [Fact]
    public async Task TenantAdmin_IsRefusedEverySysAdminOnlyMigratedRoute_AgainstAnUntouchedMatrix()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId, SystemRoles.TenantAdmin);

        foreach (var (description, call) in SysAdminOnlyRoutes())
        {
            var response = await call(tenantId);
            Assert.True(response.StatusCode == HttpStatusCode.Forbidden,
                $"{description} unexpectedly not forbidden for TenantAdmin (got {response.StatusCode}) — this default should be SysAdmin-only.");
        }
    }

    [Fact]
    public async Task SysAdmin_ReachesEveryMigratedRoute_AfterEveryActionIsSeededToNobody()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await CreateTestTenantAsync(tenantId);
        await ConfigureAdminApiClientAsync(tenantId);

        await SeedEveryActionToNobodyAsync();

        var allRoutes = OpenRoutes().Concat(SysAdminOnlyRoutes());
        foreach (var (description, call) in allRoutes)
        {
            var response = await call(tenantId);
            Assert.False(response.StatusCode == HttpStatusCode.Forbidden,
                $"{description} unexpectedly forbidden for SysAdmin even after every action was seeded to nobody.");
        }
    }
}
