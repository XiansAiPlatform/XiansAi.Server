using System.Net;
using System.Text.Json;
using Temporalio.Client;
using Xians.Lib.Agents.Core;
using Temporalio.Exceptions;
using Temporalio.Testing;
using Tests.TestUtils;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// A partner tenant with its own Temporal server. Agents that partner creates run there,
/// including when another tenant deploys the template and activates an instance.
/// A tenant with no override keeps using the platform cluster.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalTenantTemporalClusterAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalTenantTemporalClusterAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task PartnerTemporal_CreatorAndDeployedAgents_StayOnCreatorCluster()
    {
        await using var partnerCluster = await TemporalFixture.StartAdditionalServerAsync();
        var partnerHost = TargetHost(partnerCluster);
        var partnerNamespace = NamespaceOf(partnerCluster);

        var partnerTenant = $"test-tenant-{Guid.NewGuid()}";
        var customerTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(partnerTenant);
        await CreateTestTenantAsync(partnerTenant);
        await CreateTestTenantAsync(customerTenant);
        await AssignTemporalAsync(partnerTenant, partnerHost, partnerNamespace);

        var templateName = $"Partner Echo {Guid.NewGuid():N}";
        const string partnerActivation = "front-desk";
        const string customerActivation = "storefront";

        var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server, partnerHost, partnerNamespace, partnerTenant, _adminUserId!);
        try
        {
            var template = RegisterEcho(host, templateName, template: true);
            await host.StartWorkersAsync(template);
            await WaitForTemplateAsync(templateName);

            await DeployLibTemplateAsync(partnerTenant, templateName);
            var partnerActivationId = await ActivateLibAgentAsync(partnerTenant, templateName, partnerActivation);
            await AssertAgentRepliesWithAsync(
                partnerTenant, templateName, partnerActivation, "Echo: partner home",
                userText: "partner home");
            await AssertOnCreatorClusterOnlyAsync(
                partnerCluster, $"{partnerTenant}:{templateName}:Supervisor Workflow:{partnerActivation}");

            await DeployLibTemplateAsync(customerTenant, templateName);
            var customerActivationId = await ActivateLibAgentAsync(customerTenant, templateName, customerActivation);
            await AssertAgentRepliesWithAsync(
                customerTenant, templateName, customerActivation, "Echo: customer deploy",
                userText: "customer deploy");
            await AssertOnCreatorClusterOnlyAsync(
                partnerCluster, $"{customerTenant}:{templateName}:Supervisor Workflow:{customerActivation}");

            await RemoveLibActivationAsync(partnerTenant, partnerActivationId);
            await RemoveLibActivationAsync(customerTenant, customerActivationId);
            await RemoveLibDeploymentAsync(partnerTenant, templateName);
            await RemoveLibDeploymentAsync(customerTenant, templateName);
            await RemoveLibTemplateAsync(templateName);
        }
        finally
        {
            await host.DisposeAsync();
        }

        await AssertCustomerOwnedAgentStaysOnPlatformAsync(customerTenant, partnerCluster);
    }

    private async Task AssignTemporalAsync(string tenantId, string serverUrl, string @namespace)
    {
        var upsert = await PutAsJsonAsync($"/api/v1/admin/tenants/{tenantId}/temporal-config", new
        {
            tenantId,
            serverUrl,
            @namespace
        });
        Assert.True(
            upsert.StatusCode == HttpStatusCode.OK,
            await upsert.Content.ReadAsStringAsync());

        var loaded = await GetAsync($"/api/v1/admin/tenants/{tenantId}/temporal-config");
        var loadedBody = await loaded.Content.ReadAsStringAsync();
        Assert.True(loaded.StatusCode == HttpStatusCode.OK, loadedBody);
        using var json = JsonDocument.Parse(loadedBody);
        Assert.Equal(serverUrl, json.RootElement.GetProperty("serverUrl").GetString());
        Assert.Equal(@namespace, json.RootElement.GetProperty("namespace").GetString());
    }

    private static XiansAgent RegisterEcho(
        LibAgentWorkflowHost host, string agentName, bool template)
    {
        var agent = template
            ? host.RegisterTemplate(agentName, "Echo that must run on the creator Temporal cluster")
            : host.RegisterTenant(agentName, "Tenant echo that stays on the platform Temporal cluster");
        agent.Workflows.DefineSupervisor().OnUserChatMessage(async context =>
        {
            var message = context.Message.Text ?? string.Empty;
            await context.ReplyAsync($"Echo: {message}");
        });
        return agent;
    }

    private async Task AssertCustomerOwnedAgentStaysOnPlatformAsync(
        string customerTenant,
        WorkflowEnvironment partnerCluster)
    {
        var agentName = $"Customer Echo {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var customerUserId = $"customer-user-{Guid.NewGuid()}";
        await CreateTestUserWithRoleAsync(customerUserId, customerTenant, SystemRoles.TenantAdmin);
        BindTenantContext(customerTenant, customerUserId);

        var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server, Temporal.TargetHost, Temporal.Namespace, customerTenant, customerUserId);
        try
        {
            var agent = RegisterEcho(host, agentName, template: false);
            BindTenantContext(customerTenant, customerUserId);
            await host.StartWorkersAsync(agent);
            await WaitForTenantAgentAsync(customerTenant, agentName);
            var activationId = await ActivateLibAgentAsync(customerTenant, agentName, activationName);
            await AssertAgentRepliesWithAsync(
                customerTenant, agentName, activationName, "Echo: customer home",
                userText: "customer home");

            var workflowId = $"{customerTenant}:{agentName}:Supervisor Workflow:{activationName}";
            Assert.True(
                await WorkflowExistsAsync(Temporal.Environment.Client, workflowId),
                $"Customer-owned workflow '{workflowId}' should run on the platform Temporal server.");
            Assert.False(
                await WorkflowExistsAsync(partnerCluster.Client, workflowId),
                $"Customer-owned workflow '{workflowId}' should not appear on the partner Temporal server.");

            await RemoveLibActivationAsync(customerTenant, activationId);
            await RemoveLibDeploymentAsync(customerTenant, agentName);
        }
        finally
        {
            await host.DisposeAsync();
        }
    }

    private async Task AssertOnCreatorClusterOnlyAsync(WorkflowEnvironment partnerCluster, string workflowId)
    {
        Assert.True(
            await WorkflowExistsAsync(partnerCluster.Client, workflowId),
            $"Workflow '{workflowId}' should run on the agent creator's Temporal server.");
        Assert.False(
            await WorkflowExistsAsync(Temporal.Environment.Client, workflowId),
            $"Workflow '{workflowId}' should not appear on the platform Temporal server.");
    }

    private static async Task<bool> WorkflowExistsAsync(ITemporalClient client, string workflowId)
    {
        try
        {
            await client.GetWorkflowHandle(workflowId).DescribeAsync();
            return true;
        }
        catch (RpcException ex) when (ex.Code == RpcException.StatusCode.NotFound)
        {
            return false;
        }
    }

    private static string TargetHost(WorkflowEnvironment environment) =>
        environment.Client.Connection.Options.TargetHost
        ?? throw new InvalidOperationException("Temporal local server has no target host.");

    private static string NamespaceOf(WorkflowEnvironment environment) =>
        string.IsNullOrWhiteSpace(environment.Client.Options.Namespace)
            ? "default"
            : environment.Client.Options.Namespace;
}
