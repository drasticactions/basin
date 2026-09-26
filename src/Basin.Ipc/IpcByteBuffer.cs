using System.Buffers;

namespace Basin.Ipc;

internal sealed class IpcByteBuffer : IBufferWriter<byte>
{
    private byte[] _data;
    private int _length;

    public IpcByteBuffer(int capacity = 256) => _data = new byte[capacity];

    public int Length => _length;

    public int Capacity => _data.Length;

    public ReadOnlySpan<byte> Written => _data.AsSpan(0, _length);

    public Span<byte> WrittenSpan => _data.AsSpan(0, _length);

    public void Clear() => _length = 0;

    public void Truncate(int length) => _length = length;

    public void Advance(int count) => _length += count;

    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _data.AsMemory(_length);
    }

    public Span<byte> GetSpan(int sizeHint = 0)
    {
        Ensure(sizeHint);
        return _data.AsSpan(_length);
    }

    public void Append(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(GetSpan(bytes.Length));
        _length += bytes.Length;
    }

    public void Consume(int count)
    {
        if (count >= _length)
        {
            _length = 0;
            return;
        }

        _data.AsSpan(count, _length - count).CopyTo(_data);
        _length -= count;
    }

    public void Release(int keepCapacity)
    {
        if (_length == 0 && _data.Length > keepCapacity)
        {
            _data = new byte[keepCapacity];
        }
    }

    private void Ensure(int sizeHint)
    {
        var needed = _length + Math.Max(sizeHint, 1);
        if (needed <= _data.Length)
        {
            return;
        }

        var size = _data.Length;
        while (size < needed)
        {
            size *= 2;
        }

        Array.Resize(ref _data, size);
    }
}
