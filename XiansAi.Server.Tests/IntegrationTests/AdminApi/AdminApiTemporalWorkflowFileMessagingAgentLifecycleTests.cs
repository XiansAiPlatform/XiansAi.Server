using System.Net;
using System.Text;
using System.Text.Json;
using Temporalio.Workflows;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xians.Lib.Agents.Workflows.Models;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// File send from workflow code: <c>XiansContext.Messaging.SendFileAsSupervisorAsync</c> runs as a
/// Temporal activity, stores bytes in GridFS, and posts a File message with fileId refs only.
/// Handler send stays on the Files cycle. See
/// https://xiansaiplatform.github.io/XiansAi.Docs/concepts/messaging-fileupload/
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalWorkflowFileMessagingAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalWorkflowFileMessagingAgentLifecycleTests(
        MongoDbFixture mongoDbFixture,
        TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task WorkflowFileMessagingAgent_SendFileAsSupervisor_RoundTrip()
    {
        var tenantId = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(tenantId);
        await CreateTestTenantAsync(tenantId);
        BindTenantContext(tenantId, _adminUserId!);

        var agentName = $"WfFiles {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var marker = $"report-{suffix}";
        var participantId = $"owner-{suffix}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            tenantId,
            _adminUserId!);

        var agent = RegisterWorkflowFileAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(tenantId, agentName);
        var activationId = await ActivateLibAgentAsync(tenantId, agentName, activationName);

        await AssertAgentRepliesWithAsync(
            tenantId, agentName, activationName, $"started:{marker}",
            userText: $"report {marker}",
            participantId: participantId);

        var (generated, history) = await WaitForHistoryContainsAsync(
            tenantId, agentName, activationName, participantId, $"generated {marker}");
        Assert.True(generated, $"Workflow file caption did not appear. History: {history}");
        Assert.DoesNotContain(Convert.ToBase64String(Encoding.UTF8.GetBytes(marker)), history);

        var report = Assert.Single(FileRefs(history, "Outgoing"), file => file.FileName == "workflow-report.txt");
        Assert.True(string.IsNullOrEmpty(report.Content));
        Assert.Equal(
            Encoding.UTF8.GetBytes(marker),
            await DownloadFileAsync(tenantId, report.FileId, "workflow-report.txt"));

        await RemoveLibActivationAsync(tenantId, activationId);
        await RemoveLibDeploymentAsync(tenantId, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterWorkflowFileAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Sends a file from custom workflow code");
        agent.Workflows.DefineCustom<WorkflowFileReportWorkflow>(
            new WorkflowOptions { Activable = false },
            typeName: $"{agentName}:File Report");

        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(HandleWorkflowFileChatAsync);
        return agent;
    }

    private static async Task HandleWorkflowFileChatAsync(UserMessageContext context)
    {
        var text = context.Message.Text?.Trim() ?? string.Empty;
        try
        {
            if (!text.StartsWith("report ", StringComparison.Ordinal))
            {
                await context.ReplyAsync("unknown-command");
                return;
            }

            var marker = text["report ".Length..];
            var agentName = XiansContext.SafeAgentName ?? XiansContext.CurrentAgent.Name;
            await XiansContext.Workflows.ExecuteAsync<string>(
                $"{agentName}:File Report",
                [context.Message.ParticipantId, marker],
                uniqueKey: marker);
            await context.ReplyAsync($"started:{marker}");
        }
        catch (Exception ex)
        {
            await context.ReplyAsync($"error:{ex.GetType().Name}:{ex.Message}");
        }
    }

    private async Task<byte[]> DownloadFileAsync(string tenantId, string fileId, string expectedFileName)
    {
        BindTenantContext(tenantId, _adminUserId!);
        var response = await GetAsync($"/api/v1/admin/tenants/{tenantId}/messaging/files/{fileId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? string.Empty;
        Assert.Contains(expectedFileName, fileName.Trim('"'));
        return await response.Content.ReadAsByteArrayAsync();
    }

    private static List<HistoryFileRef> FileRefs(string historyJson, string direction)
    {
        using var json = JsonDocument.Parse(historyJson);
        var refs = new List<HistoryFileRef>();
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("direction", out var directionValue) ||
                !string.Equals(directionValue.GetString(), direction, StringComparison.OrdinalIgnoreCase) ||
                !item.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!data.TryGetProperty("files", out var files) && !data.TryGetProperty("Files", out files))
            {
                continue;
            }

            if (files.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var file in files.EnumerateArray())
            {
                if (file.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                refs.Add(new HistoryFileRef(
                    ReadString(file, "fileName"),
                    ReadString(file, "fileId"),
                    ReadString(file, "content")));
            }
        }

        return refs;
    }

    private static string ReadString(JsonElement obj, string name)
    {
        return obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private sealed record HistoryFileRef(string FileName, string FileId, string Content);
}

[Workflow("placeholder:File Report")]
public class WorkflowFileReportWorkflow
{
    [WorkflowRun]
    public async Task<string> RunAsync(string participantId, string marker)
    {
        var bytes = Encoding.UTF8.GetBytes(marker);
        await XiansContext.Messaging.SendFileAsSupervisorAsync(
            [UploadedFile.FromBytes(bytes, "workflow-report.txt", "text/plain")],
            text: $"generated {marker}",
            participantId: participantId);
        return $"generated {marker}";
    }
}
