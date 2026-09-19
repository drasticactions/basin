using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Basin.Capabilities;
using Basin.Diagnostics;
using static Basin.Video.WebCodecs.WebCodecsLog;

namespace Basin.Video.WebCodecs;

[SupportedOSPlatform("browser")]
internal sealed class WebCodecsDecodeSession : IAsyncVideoDecodeSession
{
    private const int MaxPending = 8;

    private static readonly Dictionary<int, WebCodecsDecodeSession> Sessions = [];
    private static readonly Action<int, bool> OnOutput = Landed;

    private readonly VideoCodec _codec;
    private readonly Queue<(nint Destination, int Stride)> _pending = new(MaxPending);
    private readonly int _id;
    private readonly int _width;
    private readonly int _height;
    private readonly DrmFormat _format;
    private readonly nint _staging;
    private bool _sawKey;
    private bool _disposed;

    internal WebCodecsDecodeSession(VideoCodec codec, string codecString, int width, int height, DrmFormat format)
    {
        var bgra = format switch
        {
            DrmFormat.Xrgb8888 or DrmFormat.Argb8888 => true,
            DrmFormat.Xbgr8888 or DrmFormat.Abgr8888 => false,
            DrmFormat.Xrgb2101010 or DrmFormat.Xbgr2101010 => true,
            _ => throw new NotSupportedException($"WebCodecs frames are not packed into {format}"),
        };
        _codec = codec;
        _width = width;
        _height = height;
        _format = format;
        if (format is DrmFormat.Xrgb2101010 or DrmFormat.Xbgr2101010)
        {
            unsafe
            {
                _staging = (nint)NativeMemory.Alloc((nuint)((long)width * height * 4));
            }
        }

        _id = WebCodecsInterop.Create(codecString, width, height, bgra, OnOutput);
        Sessions[_id] = this;
        BasinCounters.Track();
    }

    public event Action<nint, bool>? Completed;

    public bool Decode(ReadOnlySpan<byte> packet, nint destination, int stride)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_pending.Count >= MaxPending)
        {
            Log.Warn($"the WebCodecs decoder has {MaxPending} frames in flight; a packet was refused");
            return false;
        }

        var key = !_sawKey || KeyFrames.IsKey(_codec, packet);
        bool accepted;
        unsafe
        {
            fixed (byte* bytes = packet)
            {
                accepted = _staging != 0
                    ? WebCodecsInterop.Decode(_id, (nint)bytes, packet.Length, key, _staging, _width * 4)
                    : WebCodecsInterop.Decode(_id, (nint)bytes, packet.Length, key, destination, stride);
            }
        }

        if (!accepted)
        {
            return false;
        }

        _sawKey = true;
        _pending.Enqueue((destination, stride));
        return true;
    }

    private static void Landed(int id, bool produced)
    {
        try
        {
            if (!Sessions.TryGetValue(id, out var session) || session._pending.Count == 0)
            {
                return;
            }

            var (destination, stride) = session._pending.Dequeue();
            if (produced && session._staging != 0)
            {
                session.PackTenBit(destination, stride);
            }

            session.Completed?.Invoke(destination, produced);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            Log.Warn($"a frame completion threw: {error.Message}");
        }
    }

    private unsafe void PackTenBit(nint destination, int stride)
    {
        var swap = _format == DrmFormat.Xbgr2101010;
        for (var row = 0; row < _height; row++)
        {
            var from = (byte*)_staging + ((long)row * _width * 4);
            var to = (uint*)((byte*)destination + ((long)row * stride));
            for (var x = 0; x < _width; x++)
            {
                var b = (uint)from[(x * 4) + 0];
                var g = (uint)from[(x * 4) + 1];
                var r = (uint)from[(x * 4) + 2];
                to[x] = swap
                    ? (3u << 30) | (Widen(b) << 20) | (Widen(g) << 10) | Widen(r)
                    : (3u << 30) | (Widen(r) << 20) | (Widen(g) << 10) | Widen(b);
            }
        }
    }

    private static uint Widen(uint eightBit) => (eightBit << 2) | (eightBit >> 6);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Sessions.Remove(_id);
        WebCodecsInterop.Close(_id);
        _pending.Clear();
        if (_staging != 0)
        {
            unsafe
            {
                NativeMemory.Free((void*)_staging);
            }
        }

        BasinCounters.Untrack();
    }
}
