using System.Net;
using System.Text;
using System.Text.Json;
using Tests.TestUtils;
using Xians.Lib.Agents.Core;
using Xians.Lib.Agents.Messaging;
using Xunit;

namespace Tests.IntegrationTests.AdminApi;

/// <summary>
/// System file-messaging agent authored with Xians.Lib. Admin POST /messaging/send/file stores
/// bytes in GridFS and signals fileId references only. OnFileUpload hydrates those bytes and
/// ReplyWithFileAsync sends a file back. Chat SendFileAsync is the agent-originated direction.
/// History carries fileId refs without content. Admin download is tenant-scoped.
/// </summary>
[Collection(AdminApiTemporalCollection.Name)]
public class AdminApiTemporalFileMessagingAgentLifecycleTests : AdminApiTemporalIntegrationTestBase
{
    public AdminApiTemporalFileMessagingAgentLifecycleTests(MongoDbFixture mongoDbFixture, TemporalFixture temporalFixture)
        : base(mongoDbFixture, temporalFixture)
    {
    }

    [Fact]
    public async Task FileMessagingAgent_UserUploadAndAgentSend_RoundTripAndIsolate()
    {
        var ownerTenant = $"test-tenant-{Guid.NewGuid()}";
        var otherTenant = $"test-tenant-{Guid.NewGuid()}";
        await ConfigureAdminApiClientAsync(ownerTenant);
        await CreateTestTenantAsync(ownerTenant);
        await CreateTestTenantAsync(otherTenant);
        BindTenantContext(ownerTenant, _adminUserId!);

        var agentName = $"Files {Guid.NewGuid():N}";
        const string activationName = "front-desk";
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var invoiceName = $"invoice-{suffix}.txt";
        var invoiceBytes = Encoding.UTF8.GetBytes($"invoice-body-{suffix}");
        var reportMarker = $"report-{suffix}";
        var participantId = $"owner-{suffix}@example.com";

        await using var host = await LibAgentWorkflowHost.StartAsync(
            _factory.Server,
            Temporal.TargetHost,
            Temporal.Namespace,
            ownerTenant,
            _adminUserId!);

        var agent = RegisterFileAgent(host, agentName);
        await host.StartWorkersAsync(agent);
        await WaitForTemplateAsync(agentName);
        await DeployLibTemplateAsync(ownerTenant, agentName);
        var activationId = await ActivateLibAgentAsync(ownerTenant, agentName, activationName);

        BindTenantContext(ownerTenant, _adminUserId!);
        var sendFile = await PostAsJsonAsync($"/api/v1/admin/tenants/{ownerTenant}/messaging/send/file", new
        {
            agentName,
            activationName,
            participantId,
            text = "please process",
            files = new[]
            {
                new
                {
                    content = Convert.ToBase64String(invoiceBytes),
                    fileName = invoiceName,
                    contentType = "text/plain",
                    fileSize = invoiceBytes.Length
                }
            }
        });
        Assert.Equal(HttpStatusCode.OK, sendFile.StatusCode);

        var receivedCaption = $"received {invoiceName} {invoiceBytes.Length}";
        var (received, uploadHistory) = await WaitForHistoryContainsAsync(
            ownerTenant, agentName, activationName, participantId, receivedCaption);
        Assert.True(received, $"OnFileUpload reply did not appear. History: {uploadHistory}");
        Assert.DoesNotContain(Convert.ToBase64String(invoiceBytes), uploadHistory);

        var inbound = Assert.Single(FileRefs(uploadHistory, "Incoming"), file => file.FileName == invoiceName);
        var echoed = Assert.Single(FileRefs(uploadHistory, "Outgoing"), file => file.FileName == $"echo-{invoiceName}");
        Assert.True(string.IsNullOrEmpty(inbound.Content));
        Assert.True(string.IsNullOrEmpty(echoed.Content));

        Assert.Equal(invoiceBytes, await DownloadFileAsync(ownerTenant, inbound.FileId, invoiceName));
        Assert.Equal(invoiceBytes, await DownloadFileAsync(ownerTenant, echoed.FileId, $"echo-{invoiceName}"));

        await AssertAgentRepliesWithAsync(
            ownerTenant, agentName, activationName, $"generated {reportMarker}",
            userText: $"generate {reportMarker}",
            participantId: participantId);

        var (generated, generateHistory) = await WaitForHistoryContainsAsync(
            ownerTenant, agentName, activationName, participantId, $"generated {reportMarker}");
        Assert.True(generated, $"Generated file reply did not appear. History: {generateHistory}");
        var report = Assert.Single(FileRefs(generateHistory, "Outgoing"), file => file.FileName == "report.txt");
        Assert.True(string.IsNullOrEmpty(report.Content));
        Assert.Equal(Encoding.UTF8.GetBytes(reportMarker), await DownloadFileAsync(ownerTenant, report.FileId, "report.txt"));

        BindTenantContext(otherTenant, _adminUserId!);
        var crossTenant = await GetAsync($"/api/v1/admin/tenants/{otherTenant}/messaging/files/{inbound.FileId}");
        Assert.Equal(HttpStatusCode.NotFound, crossTenant.StatusCode);

        BindTenantContext(ownerTenant, _adminUserId!);
        await RemoveLibActivationAsync(ownerTenant, activationId);
        await RemoveLibDeploymentAsync(ownerTenant, agentName);
        await RemoveLibTemplateAsync(agentName);
    }

    private static XiansAgent RegisterFileAgent(LibAgentWorkflowHost host, string agentName)
    {
        var agent = host.RegisterTemplate(agentName, "Receives user file uploads and sends files back");
        var supervisor = agent.Workflows.DefineSupervisor();
        supervisor.OnUserChatMessage(async context =>
        {
            var text = context.Message.Text ?? string.Empty;
            if (text.StartsWith("generate ", StringComparison.Ordinal))
            {
                var marker = text["generate ".Length..];
                await context.SendFileAsync(
                    Encoding.UTF8.GetBytes(marker),
                    "report.txt",
                    "text/plain",
                    text: $"generated {marker}");
                return;
            }

            await context.ReplyAsync("unknown-command");
        });
        supervisor.OnFileUpload(async context =>
        {
            var files = context.Message.Files;
            if (files.Count == 0)
            {
                await context.ReplyAsync("no-files");
                return;
            }

            var file = files[0];
            if (!file.TryGetBytes(out var bytes) || bytes == null)
            {
                await context.ReplyAsync($"invalid-file {file.FileName}");
                return;
            }

            await context.ReplyWithFileAsync(
                $"received {file.FileName} {bytes.Length}",
                UploadedFile.FromBytes(bytes, $"echo-{file.FileName}", file.ContentType));
        });

        return agent;
    }

    private async Task<byte[]> DownloadFileAsync(string tenantId, string fileId, string expectedFileName)
    {
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
            if (!DirectionIs(item, direction) ||
                !item.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!TryGetFiles(data, out var files))
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
                    StringProp(file, "fileName"),
                    StringProp(file, "fileId"),
                    StringProp(file, "content")));
            }
        }

        return refs;
    }

    private static bool DirectionIs(JsonElement item, string direction)
    {
        return item.TryGetProperty("direction", out var value) &&
               string.Equals(value.GetString(), direction, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetFiles(JsonElement data, out JsonElement files)
    {
        if (data.TryGetProperty("files", out files) && files.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        if (data.TryGetProperty("Files", out files) && files.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        files = default;
        return false;
    }

    private static string StringProp(JsonElement obj, string name)
    {
        if (obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
        {
            return value.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private sealed record HistoryFileRef(string FileName, string FileId, string Content);
}
