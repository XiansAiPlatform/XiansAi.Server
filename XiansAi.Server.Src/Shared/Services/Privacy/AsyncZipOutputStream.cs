using System.Buffers;

namespace Shared.Services.Privacy;

/// <summary>
/// Routes ZIP's synchronous writes through the response stream's async API.
/// ZIP entry disposal still blocks the current thread on those writes; callers should bound concurrent exports.
/// </summary>
internal sealed class AsyncZipOutputStream(Stream output) : Stream
{
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => output.CanWrite;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Write(byte[] buffer, int offset, int count) =>
        output.WriteAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        var rented = ArrayPool<byte>.Shared.Rent(buffer.Length);
        try
        {
            buffer.CopyTo(rented);
            output.WriteAsync(rented.AsMemory(0, buffer.Length)).AsTask().GetAwaiter().GetResult();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        output.WriteAsync(buffer, cancellationToken);

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        output.WriteAsync(buffer, offset, count, cancellationToken);

    public override void Flush() => output.FlushAsync().GetAwaiter().GetResult();
    public override Task FlushAsync(CancellationToken cancellationToken) => output.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
