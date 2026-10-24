using System.Buffers;
using System.Text.Json;

namespace Basin.Greetd;

/// <summary>
/// Writes one greetd frame at a time into a pooled buffer. A client, a test server and a third-party server use the same codec.
/// The writer clears its bytes when it clears or grows the buffer, so a password does not stay in pooled memory.
/// </summary>
public sealed class GreetdFrameWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    /// <summary>
    /// Initializes a new instance of the <see cref="GreetdFrameWriter"/> class.
    /// </summary>
    /// <param name="initialCapacity">The first buffer size in bytes, header included. The buffer grows when a frame needs more.</param>
    public GreetdFrameWriter(int initialCapacity = 1024)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, GreetdFrame.HeaderLength + 64));
        _written = GreetdFrame.HeaderLength;
    }

    /// <summary>
    /// Gets the last frame, header and payload. It is valid until the next write, <see cref="Clear"/> or <see cref="Dispose"/>.
    /// </summary>
    public ReadOnlySpan<byte> Frame => _buffer.AsSpan(0, _written);

    /// <summary>
    /// Gets the JSON payload of the last frame, with no header. It is valid until the next write, <see cref="Clear"/> or <see cref="Dispose"/>.
    /// </summary>
    public ReadOnlySpan<byte> Payload => _buffer.AsSpan(GreetdFrame.HeaderLength, _written - GreetdFrame.HeaderLength);

    /// <inheritdoc/>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_written + count > _buffer.Length)
        {
            throw new InvalidOperationException("The writer advanced past the end of its buffer.");
        }

        _written += count;
    }

    /// <inheritdoc/>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written);
    }

    /// <inheritdoc/>
    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written);
    }

    /// <summary>
    /// Replaces the frame with <paramref name="request"/> as JSON and writes its header.
    /// </summary>
    /// <param name="request">The request to write.</param>
    /// <exception cref="InvalidOperationException">The payload is over <see cref="GreetdFrame.MaxLength"/>. The writer is then clear.</exception>
    public void Write(GreetdRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Clear();
        using (var writer = new Utf8JsonWriter(this))
        {
            JsonSerializer.Serialize(writer, request, GreetdJsonContext.Default.GreetdRequest);
        }

        Seal();
    }

    /// <summary>
    /// Replaces the frame with <paramref name="response"/> as JSON and writes its header.
    /// </summary>
    /// <param name="response">The response to write.</param>
    /// <exception cref="InvalidOperationException">The payload is over <see cref="GreetdFrame.MaxLength"/>. The writer is then clear.</exception>
    public void Write(GreetdResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        Clear();
        using (var writer = new Utf8JsonWriter(this))
        {
            JsonSerializer.Serialize(writer, response, GreetdJsonContext.Default.GreetdResponse);
        }

        Seal();
    }

    /// <summary>
    /// Replaces the frame with a <c>post_auth_message_response</c> that carries <paramref name="utf8Response"/>. The text never becomes a managed string.
    /// </summary>
    /// <param name="utf8Response">The answer as UTF-8 bytes. The caller keeps ownership and clears it.</param>
    /// <exception cref="InvalidOperationException">The payload is over <see cref="GreetdFrame.MaxLength"/>. The writer is then clear.</exception>
    public void WriteAuthResponse(ReadOnlySpan<byte> utf8Response)
    {
        Clear();
        using (var writer = new Utf8JsonWriter(this))
        {
            writer.WriteStartObject();
            writer.WriteString("type"u8, "post_auth_message_response"u8);
            writer.WriteString("response"u8, utf8Response);
            writer.WriteEndObject();
        }

        Seal();
    }

    /// <summary>
    /// Sets the written bytes to zero and drops the frame. Call it after a send so that an answer does not stay in the buffer.
    /// </summary>
    public void Clear()
    {
        _buffer.AsSpan(0, _written).Clear();
        _written = GreetdFrame.HeaderLength;
    }

    /// <summary>
    /// Clears the buffer and returns it to the pool. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_buffer.Length == 0)
        {
            return;
        }

        _buffer.AsSpan().Clear();
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = [];
        _written = 0;
    }

    private void Seal()
    {
        var length = _written - GreetdFrame.HeaderLength;
        if (length > GreetdFrame.MaxLength)
        {
            Clear();
            throw new InvalidOperationException($"The request is too large for one greetd frame. A frame can hold at most {GreetdFrame.MaxLength} bytes.");
        }

        GreetdFrame.WriteHeader(_buffer, length);
    }

    private void Ensure(int sizeHint)
    {
        var needed = _written + Math.Max(sizeHint, 1);
        if (needed <= _buffer.Length)
        {
            return;
        }

        var grown = ArrayPool<byte>.Shared.Rent(Math.Max(needed, _buffer.Length * 2));
        _buffer.AsSpan(0, _written).CopyTo(grown);
        _buffer.AsSpan().Clear();
        ArrayPool<byte>.Shared.Return(_buffer);
        _buffer = grown;
    }
}
