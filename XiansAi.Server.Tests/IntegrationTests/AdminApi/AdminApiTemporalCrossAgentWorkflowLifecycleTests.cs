using System.Net;
using System.Text.Json;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// Two system agents on one Lib host. Invoice chat starts Fraud Scan/Review through
/// XiansContext.Workflows type strings. Cross-agent children do not inherit Invoice's
/// activation postfix unless activationName is passed. Explicit activation is validated
/// before start (not-found / deactivated). IDs follow
/// {tenant}:{fraud}:{name}[:{activation}][:{uniqueKey}].
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalCrossAgentWorkflowLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalCrossAgentWorkflowLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task CrossAgentWorkflow_InvoiceCallsFraud_ActivationTargetAndValidation()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var invoiceName = $"Invoice {Guid.NewGuid():N}";
        var fraudName = $"Fraud {Guid.NewGuid():N}";
        const string invoiceActivation = "front-desk";
        const string fraudEu = "fraud-eu";
        const string fraudUs = "fraud-us";
        var scanId = $"inv-{Guid.NewGuid():N}"[..12];
        var scanEuId = $"eu-{Guid.NewGuid():N}"[..12];
        var scanNoActId = $"{ownerTenant}:{fraudName}:Scan:{scanId}";
        var scanInheritedId = $"{ownerTenant}:{fraudName}:Scan:{invoiceActivation}:{scanId}";
        var scanEuWorkflowId = $"{ownerTenant}:{fraudName}:Scan:{fraudEu}:{scanEuId}";
        var reviewId = $"{ownerTenant}:{fraudName}:Review:{fraudEu}";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var invoice = RegisterInvoiceAgent(host, invoiceName, fraudName, fraudEu, fraudUs);
        var fraud = RegisterFraudAgent(host, fraudName);
        await host.StartWorkersAsync(invoice, fraud);
        await WaitForTemplateAsync(invoiceName);
        await WaitForTemplateAsync(fraudName);
        await DeployLibTemplateAsync(ownerTenant, invoiceName);
        await DeployLibTemplateAsync(ownerTenant, fraudName);

        var invoiceActivationId = await ActivateLibAgentAsync(ownerTenant, invoiceName, invoiceActivation);
        var fraudEuId = await ActivateLibAgentAsync(ownerTenant, fraudName, fraudEu);
        var fraudUsId = await ActivateLibAgentAsync(ownerTenant, fraudName, fraudUs);
        BindTenantContext(ownerTenant, _adminUserId!);
        var deactivateUs = await PostAsJsonAsync(
            $"/api/v1/admin/tenants/{ownerTenant}/agentActivations/{fraudUsId}/deactivate",
            new { });
        Assert.Equal(HttpStatusCode.OK, deactivateUs.StatusCode);

        await AssertAgentRepliesWithAsync(
            ownerTenant, invoiceName, invoiceActivation, $"clean:{scanId}",
            userText: $"scan {scanId}");
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, fraudName, scanNoActId),
            $"Fraud Scan {scanNoActId} was not started without an activation postfix.");
        await AssertWorkflowStatusAsync(ownerTenant, scanNoActId, "Completed");
        Assert.False(
            await WorkflowExistsAsync(ownerTenant, scanInheritedId),
            $"Cross-agent Scan inherited Invoice activation as {scanInheritedId}.");
        Assert.Equal(0, await CountListedByIdAsync(ownerTenant, invoiceName, scanNoActId, "completed"));

        await AssertAgentRepliesWithAsync(
            ownerTenant, invoiceName, invoiceActivation, $"clean:{scanEuId}",
            userText: $"scan-eu {scanEuId}");
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, fraudName, scanEuWorkflowId),
            $"Fraud Scan {scanEuWorkflowId} was not started under {fraudEu}.");
        await AssertWorkflowStatusAsync(ownerTenant, scanEuWorkflowId, "Completed");

        await AssertAgentRepliesWithAsync(
            ownerTenant, invoiceName, invoiceActivation, "holding",
            userText: "hold");
        Assert.True(
            await WaitForWorkflowInListAsync(ownerTenant, fraudName, reviewId),
            $"Fraud Review {reviewId} did not start under {fraudEu}.");
        await AssertWorkflowStatusAsync(ownerTenant, reviewId, "Running");

        await AssertAgentRepliesWithAsync(
            ownerTenant, invoiceName, invoiceActivation, "signaled:granted",
            userText: "approve granted");
        await AssertWorkflowStatusAsync(ownerTenant, reviewId, "Completed");

        await AssertAgentRepliesWithAsync(
            ownerTenant, invoiceName, invoiceActivation, "not-found",
            userText: "missing");
        await AssertAgentRepliesWithAsync(
            ownerTenant, invoiceName, invoiceActivation, "deactivated",
            userText: "dead");

        BindTenantContext(otherTenant, _adminUserId!);
        var otherGet = await GetAsync(
            $"/api/v1/admin/tenants/{otherTenant}/workflows?workflowId={Uri.EscapeDataString(scanNoActId)}");
        Assert.Equal(HttpStatusCode.NotFound, otherGet.StatusCode);

        BindTenantContext(ownerTenant, _adminUserId!);
        await RemoveLibActivationAsync(ownerTenant, invoiceActivationId);
        await RemoveLibActivationAsync(ownerTenant, fraudEuId);
        await RemoveLibActivationAsync(ownerTenant, fraudUsId);
        await RemoveLibDeploymentAsync(ownerTenant, invoiceName);
        await RemoveLibDeploymentAsync(ownerTenant, fraudName);
        await RemoveLibTemplateAsync(invoiceName);
        await RemoveLibTemplateAsync(fraudName);
    }

    private static XiansAgent RegisterInvoiceAgent(
        LibAgentWorkflowHost host,
        string invoiceName,
        string fraudName,
        string fraudEu,
        string fraudUs)
    {
        var agent = host.RegisterTemplate(invoiceName, "Starts Fraud Scan and Review from chat");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(context =>
            HandleInvoiceChatAsync(context, fraudName, fraudEu, fraudUs));
        return agent;
    }

    private static XiansAgent RegisterFraudAgent(LibAgentWorkflowHost host, string fraudName)
    {
        var agent = host.RegisterTemplate(fraudName, "Scan and Review workflows called by Invoice");
        var notActivable = new WorkflowOptions { Activable = false };
        agent.Workflows.DefineCustom<CrossAgentScanWorkflow>(notActivable, typeName: $"{fraudName}:Scan");
        agent.Workflows.DefineCustom<CrossAgentReviewWorkflow>(notActivable, typeName: $"{fraudName}:Review");
        return agent;
    }

    private static async Task HandleInvoiceChatAsync(
        UserMessageContext context,
        string fraudName,
        string fraudEu,
        string fraudUs)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        var scanType = $"{fraudName}:Scan";
        var reviewType = $"{fraudName}:Review";
        try
        {
            var space = text.IndexOf(' ');
            var command = space < 0 ? text : text[..space];
            var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

            if (command == "scan")
            {
                var result = await XiansContext.Workflows.ExecuteAsync<string>(
                    scanType, new object[] { argument }, uniqueKey: argument);
                await context.ReplyAsync(result);
                return;
            }

            if (command == "scan-eu")
            {
                var result = await XiansContext.Workflows.ExecuteAsync<string>(
                    scanType, new object[] { argument }, uniqueKey: argument, activationName: fraudEu);
                await context.ReplyAsync(result);
                return;
            }

            if (command == "hold")
            {
                await XiansContext.Workflows.StartAsync(
                    reviewType, new object[] { "held" }, activationName: fraudEu);
                await context.ReplyAsync("holding");
                return;
            }

            if (command == "approve")
            {
                await XiansContext.Workflows.SignalAsync(
                    reviewType,
                    "ApproveAsync",
                    new object[] { argument },
                    activationName: fraudEu);
                await context.ReplyAsync($"signaled:{argument}");
                return;
            }

            if (command == "missing")
            {
                await XiansContext.Workflows.ExecuteAsync<string>(
                    scanType, new object[] { "x" }, uniqueKey: "missing", activationName: "no-such-desk");
                await context.ReplyAsync("started");
                return;
            }

            if (command == "dead")
            {
                await XiansContext.Workflows.ExecuteAsync<string>(
                    scanType, new object[] { "x" }, uniqueKey: "dead", activationName: fraudUs);
                await context.ReplyAsync("started");
                return;
            }

            await context.ReplyAsync("unknown");
        }
        catch (ActivationNotFoundException)
        {
            await context.ReplyAsync("not-found");
        }
        catch (ActivationDeactivatedException)
        {
            await context.ReplyAsync("deactivated");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task<bool> WorkflowExistsAsync(string tenantId, string workflowId)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var response = await GetAsync(
            $"/api/v1/admin/tenants/{tenantId}/workflows?workflowId={Uri.EscapeDataString(workflowId)}");
        return response.StatusCode == HttpStatusCode.OK;
    }

    private async Task AssertWorkflowStatusAsync(string tenantId, string workflowId, string expectedStatus)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri = $"/api/v1/admin/tenants/{tenantId}/workflows?workflowId={Uri.EscapeDataString(workflowId)}";
        var lastBody = string.Empty;
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var response = await GetAsync(uri);
            lastBody = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                using var json = JsonDocument.Parse(lastBody);
                var status = json.RootElement.GetProperty("status").GetString();
                if (string.Equals(status, expectedStatus, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }

        Assert.Fail($"Workflow {workflowId} did not reach status {expectedStatus}. Last: {lastBody}");
    }

    private async Task<int> CountListedByIdAsync(
        string tenantId,
        string agentName,
        string workflowId,
        string status)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var uri =
            $"/api/v1/admin/tenants/{tenantId}/workflows/list?agent={Uri.EscapeDataString(agentName)}" +
            $"&status={Uri.EscapeDataString(status)}";
        var response = await GetAsync(uri);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var count = 0;
        foreach (var item in json.RootElement.GetProperty("workflows").EnumerateArray())
        {
            if (string.Equals(item.GetProperty("workflowId").GetString(), workflowId, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }
}

[Workflow("placeholder:Scan")]
public class CrossAgentScanWorkflow
{
    [WorkflowRun]
    public Task<string> RunAsync(string invoiceId)
    {
        return Task.FromResult($"clean:{invoiceId}");
    }
}

[Workflow("placeholder:Review")]
public class CrossAgentReviewWorkflow
{
    private bool _approved;
    private string _decision = string.Empty;

    [WorkflowRun]
    public async Task<string> RunAsync(string invoiceId)
    {
        _ = invoiceId;
        await Workflow.WaitConditionAsync(() => _approved);
        return _decision;
    }

    [WorkflowSignal("ApproveAsync")]
    public Task ApproveAsync(string decision)
    {
        _decision = decision;
        _approved = true;
        return Task.CompletedTask;
    }
}
