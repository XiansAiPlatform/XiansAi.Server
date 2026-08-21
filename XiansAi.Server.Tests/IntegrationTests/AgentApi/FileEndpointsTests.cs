using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Shared.Services;
using Tests.TestUtils;

namespace Tests.IntegrationTests.AgentApi;

public class FileEndpointsTests : IntegrationTestBase, IClassFixture<MongoDbFixture>
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public FileEndpointsTests(MongoDbFixture mongoFixture) : base(mongoFixture)
    {
    }

    [Fact]
    public async Task UploadFiles_ThenDownload_ReturnsSameBytesNameAndContentType()
    {
        var bytes = Encoding.UTF8.GetBytes("hello from agent");
        var uploadResponse = await _client.PostAsJsonAsync("/api/agent/files", new
        {
            participantId = "user@example.com",
            files = new[]
            {
                new
                {
                    content = Convert.ToBase64String(bytes),
                    fileName = "hello.txt",
                    contentType = "text/plain",
                    fileSize = bytes.Length
                }
            }
        });

        Assert.Equal(HttpStatusCode.OK, uploadResponse.StatusCode);
        var uploaded = await uploadResponse.Content.ReadFromJsonAsync<UploadedFilesResponse>(JsonOptions);
        Assert.NotNull(uploaded);
        Assert.Single(uploaded.Files);
        Assert.False(string.IsNullOrWhiteSpace(uploaded.Files[0].FileId));
        Assert.Equal("hello.txt", uploaded.Files[0].FileName);
        Assert.Equal("text/plain", uploaded.Files[0].ContentType);
        Assert.Equal(bytes.Length, uploaded.Files[0].FileSize);

        var downloadResponse = await _client.GetAsync($"/api/agent/files/{uploaded.Files[0].FileId}");
        Assert.Equal(HttpStatusCode.OK, downloadResponse.StatusCode);
        Assert.Equal("text/plain", downloadResponse.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", downloadResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Contains("hello.txt", downloadResponse.Content.Headers.ContentDisposition?.FileNameStar
            ?? downloadResponse.Content.Headers.ContentDisposition?.FileName
            ?? string.Empty);

        var downloaded = await downloadResponse.Content.ReadAsByteArrayAsync();
        Assert.Equal(bytes, downloaded);
    }

    [Fact]
    public async Task UploadFiles_EmptyList_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/agent/files", new
        {
            participantId = "user@example.com",
            files = Array.Empty<object>()
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("files must be a non-empty array", body);
    }

    [Fact]
    public async Task UploadFiles_MissingParticipantId_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/agent/files", new
        {
            files = new[]
            {
                new
                {
                    content = Convert.ToBase64String(Encoding.UTF8.GetBytes("orphan")),
                    fileName = "orphan.txt",
                    contentType = "text/plain"
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("participantId is required", body);
    }

    [Fact]
    public async Task UploadFiles_InvalidBase64_ReturnsBadRequest()
    {
        var response = await _client.PostAsJsonAsync("/api/agent/files", new
        {
            participantId = "user@example.com",
            files = new[]
            {
                new
                {
                    content = "not-valid-base64!!!",
                    fileName = "bad.bin",
                    contentType = "application/octet-stream"
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadFiles_OversizeFile_ReturnsBadRequest()
    {
        // EstimateBase64Bytes uses length*3/4; a 13,981,016-char payload estimates just over 10MB.
        var oversizedBase64 = new string('A', 13_981_016);
        var response = await _client.PostAsJsonAsync("/api/agent/files", new
        {
            participantId = "user@example.com",
            files = new[]
            {
                new
                {
                    content = oversizedBase64,
                    fileName = "huge.bin",
                    contentType = "application/octet-stream"
                }
            }
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("exceeds the 10MB per-file limit", body);
    }

    [Fact]
    public async Task DownloadFile_WrongTenant_ReturnsNotFound()
    {
        using var scope = _factory.Services.CreateScope();
        var storage = scope.ServiceProvider.GetRequiredService<IMessageFileStorage>();
        var stored = await storage.UploadAsync(
            "other-tenant",
            "other-user@example.com",
            "secret.txt",
            "text/plain",
            Encoding.UTF8.GetBytes("cross-tenant secret"));

        var response = await _client.GetAsync($"/api/agent/files/{stored.FileId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private sealed class UploadedFilesResponse
    {
        public List<UploadedFileRef> Files { get; set; } = new();
    }

    private sealed class UploadedFileRef
    {
        public string FileId { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
    }
}
