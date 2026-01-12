namespace Cachify.AspNetCore;

/// <summary>
/// Stream wrapper that mirrors writes to an inner stream while buffering up to a size limit.
/// </summary>
internal sealed class ResponseBufferingStream : Stream
{
    private readonly Stream _inner;
    private readonly long _limit;
    private MemoryStream? _buffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResponseBufferingStream"/> class.
    /// </summary>
    /// <param name="inner">The inner response stream.</param>
    /// <param name="limit">The maximum buffer size in bytes.</param>
    public ResponseBufferingStream(Stream inner, long limit)
    {
        _inner = inner;
        _limit = limit;
        _buffer = limit > 0 ? new MemoryStream() : null;
    }

    /// <summary>
    /// Gets a value indicating whether buffering is currently enabled.
    /// </summary>
    public bool BufferingEnabled => _buffer is not null;

    /// <summary>
    /// Gets a value indicating whether the buffer overflowed the size limit.
    /// </summary>
    public bool HasOverflowed { get; private set; }

    /// <summary>
    /// Gets the buffered response bytes if buffering is still enabled.
    /// </summary>
    public byte[]? GetBufferedBytes() => _buffer?.ToArray();

    /// <summary>
    /// Gets a value indicating whether the stream supports reading.
    /// </summary>
    public override bool CanRead => false;

    /// <summary>
    /// Gets a value indicating whether the stream supports seeking.
    /// </summary>
    public override bool CanSeek => false;

    /// <summary>
    /// Gets a value indicating whether the stream supports writing.
    /// </summary>
    public override bool CanWrite => _inner.CanWrite;

    /// <summary>
    /// Gets the length of the inner stream.
    /// </summary>
    public override long Length => _inner.Length;

    /// <summary>
    /// Gets or sets the position of the stream (not supported).
    /// </summary>
    public override long Position
    {
        get => _inner.Position;
        set => throw new NotSupportedException();
    }

    /// <summary>
    /// Flushes buffered data to the inner stream.
    /// </summary>
    public override void Flush() => _inner.Flush();

    /// <summary>
    /// Flushes buffered data to the inner stream asynchronously.
    /// </summary>
    public override Task FlushAsync(CancellationToken cancellationToken) => _inner.FlushAsync(cancellationToken);

    /// <summary>
    /// Reading is not supported for this stream.
    /// </summary>
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <summary>
    /// Seeking is not supported for this stream.
    /// </summary>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <summary>
    /// Setting the length is not supported for this stream.
    /// </summary>
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <summary>
    /// Writes data to the inner stream and buffers it when possible.
    /// </summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        BufferIfPossible(new ReadOnlySpan<byte>(buffer, offset, count));
        _inner.Write(buffer, offset, count);
    }

    /// <summary>
    /// Writes data to the inner stream asynchronously and buffers it when possible.
    /// </summary>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        BufferIfPossible(new ReadOnlySpan<byte>(buffer, offset, count));
        await _inner.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Writes data to the inner stream asynchronously and buffers it when possible.
    /// </summary>
    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        BufferIfPossible(buffer.Span);
        await _inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Buffers data when within the configured size limit, otherwise disables buffering.
    /// </summary>
    private void BufferIfPossible(ReadOnlySpan<byte> buffer)
    {
        if (_buffer is null)
        {
            return;
        }

        if (_buffer.Length + buffer.Length <= _limit)
        {
            _buffer.Write(buffer);
            return;
        }

        HasOverflowed = true;
        _buffer.Dispose();
        _buffer = null;
    }

    /// <summary>
    /// Disposes the buffer when the stream is disposed.
    /// </summary>
    /// <param name="disposing">Whether the method is called from <see cref="Dispose()"/>.</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
        }

        base.Dispose(disposing);
    }
}
