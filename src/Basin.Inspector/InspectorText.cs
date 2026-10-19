using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace Basin.Inspector;

public sealed unsafe class InspectorText
{
    private readonly byte[] _buffer;
    private int _used;

    public InspectorText(int capacity = 64 * 1024)
    {
        _buffer = GC.AllocateArray<byte>(Math.Max(256, capacity), pinned: true);
    }

    public int Used => _used;

    public void Reset() => _used = 0;

    public ReadOnlySpan<byte> Format([InterpolatedStringHandlerArgument("")] scoped ref InspectorFormat format)
    {
        if (!Utf8.TryWrite(Remaining, ref format.Handler, out var written))
        {
            written = Truncated();
        }

        return Commit(written);
    }

    public ReadOnlySpan<byte> Encode(ReadOnlySpan<char> text)
    {
        var room = Remaining;
        if (!Encoding.UTF8.TryGetBytes(text, room, out var written))
        {
            var chars = Math.Max(0, (room.Length / 3) - 2);
            if (chars > 0 && char.IsHighSurrogate(text[chars - 1]))
            {
                chars--;
            }

            written = Encoding.UTF8.GetBytes(text[..chars], room);
            "..."u8.CopyTo(room[written..]);
            written += 3;
        }

        return Commit(written);
    }

    public ReadOnlySpan<byte> Copy(ReadOnlySpan<byte> utf8)
    {
        var room = Remaining;
        var length = Math.Min(utf8.Length, room.Length);
        utf8[..length].CopyTo(room);
        return Commit(length);
    }

    internal Span<byte> Remaining
    {
        get
        {
            if (_buffer.Length - _used < 64)
            {
                _used = 0;
            }

            return _buffer.AsSpan(_used, _buffer.Length - _used - 1);
        }
    }

    private int Truncated()
    {
        "..."u8.CopyTo(Remaining);
        return 3;
    }

    private ReadOnlySpan<byte> Commit(int written)
    {
        var start = _used;
        _buffer[start + written] = 0;
        _used = start + written + 1;
        return new ReadOnlySpan<byte>(_buffer, start, written);
    }
}
