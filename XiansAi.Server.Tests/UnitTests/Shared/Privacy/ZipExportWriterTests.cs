using System.IO.Compression;
using System.Text.Json;
using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

public class ZipExportWriterTests
{
    [Fact]
    public async Task WritesJsonAndFileToZip()
    {
        using var output = new MemoryStream();
        using var file = new MemoryStream([1, 2, 3]);
        await using (var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, true, null, CancellationToken.None))
        {
            var writer = new ZipExportWriter(archive);
            await writer.WriteJsonAsync("data/person.json", new { Name = "Test" }, CancellationToken.None);
            await writer.WriteFileAsync("data/file.bin", file, CancellationToken.None);
        }

        output.Position = 0;
        using var result = new ZipArchive(output, ZipArchiveMode.Read, true);
        Assert.Equal(["data/person.json", "data/file.bin"], result.Entries.Select(entry => entry.FullName));
        await using var json = result.GetEntry("data/person.json")!.Open();
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: CancellationToken.None);
        Assert.Equal("Test", document.RootElement.GetProperty("Name").GetString());
    }

    [Theory]
    [InlineData("../secret")]
    [InlineData("/absolute")]
    [InlineData("back\\slash")]
    [InlineData("empty//segment")]
    [InlineData("   ")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("...")]
    [InlineData("foo/./bar")]
    [InlineData("foo bar")]
    [InlineData("file!.txt")]
    [InlineData("CON")]
    [InlineData("nul.txt")]
    [InlineData("folder/LPT1.bin")]
    public async Task RejectsUnsafePath(string path)
    {
        using var output = new MemoryStream();
        await using var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, true, null, CancellationToken.None);
        var writer = new ZipExportWriter(archive);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteJsonAsync(path, new { }, CancellationToken.None));
    }

    [Fact]
    public async Task RejectsDuplicatePath()
    {
        using var output = new MemoryStream();
        await using var archive = await ZipArchive.CreateAsync(output, ZipArchiveMode.Create, true, null, CancellationToken.None);
        var writer = new ZipExportWriter(archive);
        await writer.WriteJsonAsync("person.json", new { }, CancellationToken.None);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteJsonAsync("person.json", new { }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.WriteJsonAsync("PERSON.json", new { }, CancellationToken.None));
    }
}
