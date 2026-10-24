using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Unicode;

namespace Basin.Inspector;

/// <summary>
/// One pinned UTF-8 buffer for the text of an overlay frame. Each returned span has a NUL byte after it, so ImGui can read it as a C string.
/// </summary>
/// <remarks>
/// When fewer than 64 bytes are free, the buffer starts again at the beginning. Spans from earlier in the frame then change.
/// </remarks>
public sealed unsafe class InspectorText
{
    private readonly byte[] _buffer;
    private int _used;

    /// <summary>
    /// Initializes a new instance of the <see cref="InspectorText"/> class.
    /// </summary>
    /// <param name="capacity">The buffer size in bytes. A value under 256 gives 256.</param>
    public InspectorText(int capacity = 64 * 1024)
    {
        _buffer = GC.AllocateArray<byte>(Math.Max(256, capacity), pinned: true);
    }

    /// <summary>
    /// Gets the number of bytes in use since the last <see cref="Reset"/>, NUL bytes included.
    /// </summary>
    public int Used => _used;

    /// <summary>
    /// Makes the whole buffer free. Every span that the buffer returned before is then invalid.
    /// </summary>
    public void Reset() => _used = 0;

    /// <summary>
    /// Writes an interpolated string as UTF-8. When the text does not fit, the result is only <c>...</c>.
    /// </summary>
    /// <param name="format">The handler that the compiler builds from the interpolated string.</param>
    /// <returns>The written bytes without the NUL, valid until the next <see cref="Reset"/>.</returns>
    public ReadOnlySpan<byte> Format([InterpolatedStringHandlerArgument("")] scoped ref InspectorFormat format)
    {
        if (!Utf8.TryWrite(Remaining, ref format.Handler, out var written))
        {
            written = Truncated();
        }

        return Commit(written);
    }

    /// <summary>
    /// Encodes UTF-16 text as UTF-8. When the text does not fit, it is cut on a character boundary and ends in <c>...</c>.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <returns>The encoded bytes without the NUL, valid until the next <see cref="Reset"/>.</returns>
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

    /// <summary>
    /// Copies UTF-8 text and adds a NUL byte after it. When the text does not fit, the copy is cut with no marker.
    /// </summary>
    /// <param name="utf8">The UTF-8 bytes to copy.</param>
    /// <returns>The copied bytes without the NUL, valid until the next <see cref="Reset"/>.</returns>
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
