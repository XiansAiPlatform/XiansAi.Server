using System.IO.Compression;
using System.Text;
using Moq;
using Shared.Data.Models;
using Shared.Services.Privacy;
using Xunit;

namespace Tests.UnitTests.Shared.Privacy;

public class PrivacyExporterTests
{
    [Fact]
    public async Task StreamsReadmeFirstAndContributorsInOrderWithoutSynchronousOutputWrites()
    {
        using var output = new AsyncOnlyOutput();
        var later = Contributor(200, (writer, ct) => writer.WriteJsonAsync("later.json", new { Value = 2 }, ct));
        var earlier = Contributor(100, (writer, ct) => writer.WriteJsonAsync("earlier.json", new { Value = 1 }, ct));
        using var readme = new MemoryStream(Encoding.UTF8.GetBytes("export guide"));

        await new PrivacyExporter([later.Object, earlier.Object]).ExportAsync(new PrivacySubject(), new PrivacyScope(), output, readme, CancellationToken.None);

        using var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        Assert.Equal(["README.txt", "earlier.json", "later.json"], archive.Entries.Select(entry => entry.FullName));
        using var reader = new StreamReader(archive.Entries[0].Open());
        Assert.Equal("export guide", await reader.ReadToEndAsync());
        earlier.VerifyAll();
        later.VerifyAll();
    }

    [Fact]
    public async Task ContributorFailurePropagatesAndLeavesIncompleteZip()
    {
        using var output = new AsyncOnlyOutput();
        using var readme = new MemoryStream(Encoding.UTF8.GetBytes("guide"));
        var failing = Contributor(1, (_, _) => throw new InvalidOperationException("export failed"));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new PrivacyExporter([failing.Object]).ExportAsync(new PrivacySubject(), new PrivacyScope(), output, readme, CancellationToken.None));

        Assert.Equal("export failed", error.Message);
        Assert.Throws<InvalidDataException>(() => new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read));
    }

    [Fact]
    public async Task CancellationStopsBeforeContributorRuns()
    {
        using var output = new AsyncOnlyOutput();
        using var readme = new MemoryStream(Encoding.UTF8.GetBytes("guide"));
        using var cancellation = new CancellationTokenSource();
        var contributor = Contributor(1, (_, _) => Task.CompletedTask);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new PrivacyExporter([contributor.Object]).ExportAsync(new PrivacySubject(), new PrivacyScope(), output, readme, cancellation.Token));
        contributor.Verify(item => item.ExportAsync(It.IsAny<PrivacySubject>(), It.IsAny<PrivacyScope>(), It.IsAny<IExportWriter>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StreamsGeneratedHundredMegabyteFileWithoutWholeFileBuffer()
    {
        const long size = 100L * 1024 * 1024;
        using var output = new AsyncOnlyOutput();
        using var readme = new MemoryStream(Encoding.UTF8.GetBytes("guide"));
        using var generated = new GeneratedStream(size);
        var contributor = Contributor(1, (writer, ct) => writer.WriteFileAsync("large.bin", generated, ct));
        GC.Collect();
        var memoryBefore = GC.GetTotalMemory(true);

        await new PrivacyExporter([contributor.Object]).ExportAsync(new PrivacySubject(), new PrivacyScope(), output, readme, CancellationToken.None);

        var memoryAfter = GC.GetTotalMemory(true);
        Assert.Equal(size, generated.BytesRead);
        Assert.True(generated.LargestReadRequest <= 1024 * 1024);
        Assert.True(memoryAfter - memoryBefore < 20L * 1024 * 1024);
        using var archive = new ZipArchive(new MemoryStream(output.ToArray()), ZipArchiveMode.Read);
        Assert.Equal(size, archive.GetEntry("large.bin")!.Length);
    }

    private static Mock<IPersonalDataContributor> Contributor(int order, Func<IExportWriter, CancellationToken, Task> export)
    {
        var mock = new Mock<IPersonalDataContributor>();
        mock.SetupGet(item => item.Order).Returns(order);
        mock.Setup(item => item.ExportAsync(It.IsAny<PrivacySubject>(), It.IsAny<PrivacyScope>(), It.IsAny<IExportWriter>(), It.IsAny<CancellationToken>()))
            .Returns<PrivacySubject, PrivacyScope, IExportWriter, CancellationToken>((_, _, writer, ct) => export(writer, ct));
        return mock;
    }

    private sealed class AsyncOnlyOutput : MemoryStream
    {
        public override bool CanSeek => false;
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous write");
        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Synchronous write");
        public override void WriteByte(byte value) => throw new InvalidOperationException("Synchronous write");
        public override void Flush() => throw new InvalidOperationException("Synchronous flush");
        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytes = buffer.ToArray();
            base.Write(bytes, 0, bytes.Length);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class GeneratedStream(long length) : Stream
    {
        public long BytesRead { get; private set; }
        public int LargestReadRequest { get; private set; }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position { get => BytesRead; set => throw new NotSupportedException(); }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LargestReadRequest = Math.Max(LargestReadRequest, buffer.Length);
            var count = (int)Math.Min(buffer.Length, length - BytesRead);
            buffer.Span[..count].Clear();
            BytesRead += count;
            return ValueTask.FromResult(count);
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous read");
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
