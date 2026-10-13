using System.Buffers;
using System.Text.Json;

namespace Basin.Greetd;

public sealed class GreetdFrameWriter : IBufferWriter<byte>, IDisposable
{
    private byte[] _buffer;
    private int _written;

    public GreetdFrameWriter(int initialCapacity = 1024)
    {
        _buffer = ArrayPool<byte>.Shared.Rent(Math.Max(initialCapacity, GreetdFrame.HeaderLength + 64));
        _written = GreetdFrame.HeaderLength;
    }

    public ReadOnlySpan<byte> Frame => _buffer.AsSpan(0, _written);

    public ReadOnlySpan<byte> Payload => _buffer.AsSpan(GreetdFrame.HeaderLength, _written - GreetdFrame.HeaderLength);

    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (_written + count > _buffer.Length)
        {
            throw new InvalidOperationException("advanced past the buffer");
        }

        _written += count;
    }

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsMemory(_written);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _buffer.AsSpan(_written);
    }

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

    public void Clear()
    {
        _buffer.AsSpan(0, _written).Clear();
        _written = GreetdFrame.HeaderLength;
    }

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
            throw new InvalidOperationException($"a greetd frame is limited to {GreetdFrame.MaxLength} bytes");
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
