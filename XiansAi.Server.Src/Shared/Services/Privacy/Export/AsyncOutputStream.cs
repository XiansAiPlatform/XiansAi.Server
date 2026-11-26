namespace Shared.Services.Privacy.Export;

/// <summary>
/// Lets a ZIP archive write to an output that rejects synchronous writes, such as an HTTP response.
/// </summary>
/// <remarks>
/// ZipArchive still writes a few bytes synchronously when an entry closes. Those bytes are buffered here and sent
/// with the next async write. Async writes go straight through. Does not dispose the wrapped stream.
/// </remarks>
internal sealed class AsyncOutputStream : Stream
{
    /// <summary>Most bytes that can be buffered from synchronous writes.</summary>
    public const int MaxPendingBytes = 64 * 1024;

    private readonly Stream _output;
    private byte[] _pending = new byte[4096];
    private int _pendingCount;
    private long _position;

    public AsyncOutputStream(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!output.CanWrite)
        {
            throw new ArgumentException("The output stream must be writable.", nameof(output));
        }

        _output = output;
    }

    /// <summary>Buffered bytes not yet sent.</summary>
    public int PendingBytes => _pendingCount;

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException("The export output is not seekable.");

    /// <summary>Bytes written so far.</summary>
    public override long Position
    {
        get => _position;
        set => throw new NotSupportedException("The export output is not seekable.");
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    /// <summary>Buffers the bytes. Throws if the buffer would exceed <see cref="MaxPendingBytes"/>.</summary>
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
        {
            return;
        }

        var required = _pendingCount + buffer.Length;
        if (required > MaxPendingBytes)
        {
            throw new InvalidOperationException(
                $"More than {MaxPendingBytes} bytes were written synchronously to the export output. " +
                "Entry data must be written asynchronously.");
        }

        if (required > _pending.Length)
        {
            Array.Resize(ref _pending, Math.Min(MaxPendingBytes, Math.Max(required, _pending.Length * 2)));
        }

        buffer.CopyTo(_pending.AsSpan(_pendingCount));
        _pendingCount = required;
        _position += buffer.Length;
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await SendPendingAsync(cancellationToken);
        if (buffer.IsEmpty)
        {
            return;
        }

        await _output.WriteAsync(buffer, cancellationToken);
        _position += buffer.Length;
    }

    /// <summary>Does nothing. Use <see cref="FlushAsync(CancellationToken)"/>.</summary>
    public override void Flush()
    {
    }

    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await SendPendingAsync(cancellationToken);
        await _output.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException("The export output is write-only.");

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException("The export output is not seekable.");

    public override void SetLength(long value) =>
        throw new NotSupportedException("The export output is not seekable.");

    private async ValueTask SendPendingAsync(CancellationToken cancellationToken)
    {
        if (_pendingCount == 0)
        {
            return;
        }

        var count = _pendingCount;
        _pendingCount = 0;
        await _output.WriteAsync(_pending.AsMemory(0, count), cancellationToken);
    }
}
