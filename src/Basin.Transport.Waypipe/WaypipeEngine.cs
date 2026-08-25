using System.Buffers.Binary;
using K4os.Compression.LZ4;
using Wayland.Server.Shm;
using ZstdSharp;
using static Basin.Transport.Waypipe.WaypipeLog;

namespace Basin.Transport.Waypipe;

public sealed class WaypipeEngine : IDisposable
{
    private readonly Dictionary<int, SharedMemoryRegion> _regions = [];
    private readonly Dictionary<int, WaypipeImage> _images = [];
    private readonly Dictionary<int, Basin.Capabilities.IVideoDecodeSession> _decodeSessions = [];
    private readonly HashSet<int> _unclaimed = [];
    private readonly Dictionary<int, WaypipePipe> _pipes = [];
    private readonly Queue<int> _pending = new();
    private readonly WaypipeClientTransport _transport;
    private readonly WaypipeLimits _limits;
    private readonly WaypipeChannelOptions _options;
    private byte[] _scratch = new byte[64 * 1024];
    private Decompressor? _zstd;
    private long _regionBytes;
    private bool _disposed;

    public WaypipeEngine(
        WaypipeClientTransport transport,
        WaypipeCompression compression = WaypipeCompression.Lz4,
        WaypipeLimits? limits = null,
        WaypipeChannelOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(transport);
        _transport = transport;
        Compression = compression;
        _limits = limits ?? new WaypipeLimits();
        _options = options ?? new WaypipeChannelOptions();
    }

    public WaypipeCompression Compression { get; }

    public Basin.Capabilities.VideoCodec? ExpectedVideoCodec { get; set; }

    public bool Closed { get; private set; }

    public int LiveRemoteIds
    {
        get
        {
            Sweep();
            return _regions.Count + _images.Count + _pipes.Count;
        }
    }

    public event Action<WaypipeMessageType, int, ReadOnlyMemory<byte>>? Send;

    public void Apply(WaypipeMessageType type, ReadOnlySpan<byte> body)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        switch (type)
        {
            case WaypipeMessageType.Version:
                Require(body.Length >= 4, type, "a version word");
                break;

            case WaypipeMessageType.Protocol:
                InjectProtocol(body);
                break;

            case WaypipeMessageType.InjectRIDs:
                Require(body.Length % 4 == 0, type, "a whole number of remote ids");
                for (var offset = 0; offset + 4 <= body.Length; offset += 4)
                {
                    Queue(BinaryPrimitives.ReadInt32LittleEndian(body[offset..]));
                }

                break;

            case WaypipeMessageType.OpenFile:
                Require(body.Length >= 8, type, "a remote id and a size");
                OpenFile(
                    BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadInt32LittleEndian(body[4..]));
                break;

            case WaypipeMessageType.OpenDmabuf:
                if (!_options.CarriesDmabuf)
                {
                    throw new WaypipeException(
                        "this channel refuses OpenDmabuf: it was not asked for dmabuf (--gpu), so the global was withheld");
                }

                Require(body.Length >= 8 + 64, type, "a remote id, a size and a slice");
                OpenDmabuf(
                    BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadUInt32LittleEndian(body[4..]),
                    body[8..72]);
                break;

            case WaypipeMessageType.OpenDmaVidDstV2:
                if (!_options.AcceptsVideo || _options.VideoDecoder is null)
                {
                    throw new WaypipeException(
                        $"this channel refuses {type}: it was not asked for video (--video) or has no decoder");
                }

                Require(body.Length >= 12 + 64, type, "a remote id, a size, the codec flags and a slice");
                OpenVideo(
                    BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadUInt32LittleEndian(body[8..]),
                    body[12..76]);
                break;

            case WaypipeMessageType.SendDmaVidPacket:
                if (!_options.AcceptsVideo || _options.VideoDecoder is null)
                {
                    throw new WaypipeException(
                        $"this channel refuses {type}: it was not asked for video (--video) or has no decoder");
                }

                Require(body.Length >= 4, type, "a remote id");
                DecodePacket(BinaryPrimitives.ReadInt32LittleEndian(body), body[4..]);
                break;

            case WaypipeMessageType.OpenDmaVidSrcV2:
                throw new WaypipeException(
                    $"this channel refuses {type}: it asks this side to encode, and the display side of a channel never owns a dmabuf");

            case WaypipeMessageType.OpenDmaVidSrc:
            case WaypipeMessageType.OpenDmaVidDst:
                throw new WaypipeException(
                    $"this channel refuses {type}: waypipe-c legacy video that waypipe 0.11 never sends");

            case WaypipeMessageType.ExtendFile:
                Require(body.Length >= 8, type, "a remote id and a size");
                ExtendFile(
                    BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadInt32LittleEndian(body[4..]));
                break;

            case WaypipeMessageType.BufferFill:
                Require(body.Length >= 12, type, "a remote id and a span");
                Fill(
                    BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadInt32LittleEndian(body[4..]),
                    BinaryPrimitives.ReadInt32LittleEndian(body[8..]),
                    body[12..]);
                break;

            case WaypipeMessageType.BufferDiff:
                Require(body.Length >= 12, type, "a remote id, a diff size and a trailing count");
                Diff(
                    BinaryPrimitives.ReadInt32LittleEndian(body),
                    BinaryPrimitives.ReadInt32LittleEndian(body[4..]),
                    BinaryPrimitives.ReadInt32LittleEndian(body[8..]),
                    body[12..]);
                break;

            case WaypipeMessageType.OpenIRPipe:
            case WaypipeMessageType.OpenIWPipe:
            case WaypipeMessageType.OpenRWPipe:
                Require(body.Length >= 4, type, "a remote id");
                OpenPipe(BinaryPrimitives.ReadInt32LittleEndian(body), type);
                break;

            case WaypipeMessageType.PipeTransfer:
                Require(body.Length >= 4, type, "a remote id");
                Transfer(BinaryPrimitives.ReadInt32LittleEndian(body), body[4..]);
                break;

            case WaypipeMessageType.PipeShutdownR:
            case WaypipeMessageType.PipeShutdownW:
                Require(body.Length >= 4, type, "a remote id");
                Shutdown(BinaryPrimitives.ReadInt32LittleEndian(body), type);
                break;

            case WaypipeMessageType.AckNblocks:
                break;

            case WaypipeMessageType.Close:
                Closed = true;
                break;

            default:
                throw new WaypipeException(
                    $"this transport refuses {type}: video travels only on a channel asked for it (--video), "
                    + "and timelines and session resume are never carried");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var pipe in _pipes.Values)
        {
            pipe.FailForward();
        }

        foreach (var remoteId in _unclaimed)
        {
            if (_regions.TryGetValue(remoteId, out var region))
            {
                region.Release();
            }
            else if (_images.TryGetValue(remoteId, out var image))
            {
                image.Release();
            }
        }

        foreach (var session in _decodeSessions.Values)
        {
            session.Dispose();
        }

        _unclaimed.Clear();
        _regions.Clear();
        _images.Clear();
        _decodeSessions.Clear();
        _pipes.Clear();
        _pending.Clear();
        _regionBytes = 0;
        _zstd?.Dispose();
        _zstd = null;
    }

    private void InjectProtocol(ReadOnlySpan<byte> body)
    {
        if (body.IsEmpty)
        {
            return;
        }

        var bytes = new byte[body.Length];
        body.CopyTo(bytes);
        var slots = new List<int>();

        var offset = 0;
        while (offset < bytes.Length)
        {
            if (bytes.Length - offset < 8)
            {
                throw new WaypipeException("a protocol message is shorter than its own header");
            }

            var header2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
            var length = WaypipeWire.MessageLength(header2);
            if (length < 8 || length % 4 != 0 || offset + length > bytes.Length)
            {
                throw new WaypipeException(
                    $"a protocol message declares {length} bytes inside a {bytes.Length - offset} byte remainder");
            }

            var tagged = WaypipeWire.TaggedFdCount(header2);
            for (var i = 0; i < tagged; i++)
            {
                if (_pending.Count == 0)
                {
                    throw new WaypipeException(
                        "a protocol message names more descriptors than the channel injected remote ids for");
                }

                slots.Add(Mint(_pending.Dequeue()));
            }

            BinaryPrimitives.WriteUInt32LittleEndian(
                bytes.AsSpan(offset + 4), WaypipeWire.StripFdTag(header2));
            offset += length;
        }

        _transport.Deliver(bytes, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(slots));
    }

    private int Mint(int remoteId)
    {
        if (_images.TryGetValue(remoteId, out var image) && !image.IsReleased)
        {
            var imageSlot = _transport.Slots.Mint(image);
            if (_unclaimed.Remove(remoteId))
            {
                image.Release();
            }

            return imageSlot;
        }

        if (TryResolve(remoteId, out var region))
        {
            var slot = _transport.Slots.Mint(region);
            if (_unclaimed.Remove(remoteId))
            {
                region.Release();
            }

            return slot;
        }

        if (_pipes.TryGetValue(remoteId, out var pipe))
        {
            return _transport.Slots.Mint(pipe);
        }

        throw new WaypipeException($"remote id {remoteId} was injected before anything created it");
    }

    private void Queue(int remoteId)
    {
        if (_pending.Count > _limits.MaxRemoteIds)
        {
            throw new WaypipeException("the channel queued more remote ids than one message can carry");
        }

        _pending.Enqueue(remoteId);
    }

    private void OpenFile(int remoteId, int size)
    {
        if (size <= 0 || size > _limits.MaxRegionBytes)
        {
            throw new WaypipeException($"remote id {remoteId} names a {size} byte file");
        }

        Sweep();
        if (_regions.ContainsKey(remoteId) || _images.ContainsKey(remoteId) || _pipes.ContainsKey(remoteId))
        {
            throw new WaypipeException($"remote id {remoteId} already exists");
        }

        if (_regions.Count + _images.Count + _pipes.Count >= _limits.MaxRemoteIds ||
            _regionBytes + size > _limits.MaxTotalRegionBytes)
        {
            throw new WaypipeException("the channel has asked for more shared memory than it is allowed");
        }

        var region = new SharedMemoryRegion(size);
        region.AddRef();
        _regions[remoteId] = region;
        _unclaimed.Add(remoteId);
        _regionBytes += size;
    }

    private void OpenDmabuf(int remoteId, uint declaredSize, ReadOnlySpan<byte> slice) =>
        CreateImage(remoteId, slice, declaredSize);

    private void OpenVideo(int remoteId, uint flags, ReadOnlySpan<byte> slice)
    {
        var codec = (VideoFormat)(flags & 0xff) switch
        {
            VideoFormat.H264 => Basin.Capabilities.VideoCodec.H264,
            VideoFormat.Vp9 => Basin.Capabilities.VideoCodec.Vp9,
            VideoFormat.Av1 => Basin.Capabilities.VideoCodec.Av1,
            _ => throw new WaypipeException($"remote id {remoteId} names video format {flags & 0xff}, which names no codec"),
        };

        if (ExpectedVideoCodec != codec)
        {
            throw new WaypipeException(
                $"the connection header promised {ExpectedVideoCodec?.ToString() ?? "no video"} and remote id {remoteId} opens {codec}");
        }

        var image = CreateImage(remoteId, slice, declaredSize: null);
        _decodeSessions[remoteId] = _options.VideoDecoder!.Open(codec, image.Width, image.Height, image.Format);
        Log.Debug(
            $"remote id {remoteId} is a {codec} stream into a {image.Width}x{image.Height} host region");
    }

    private void DecodePacket(int remoteId, ReadOnlySpan<byte> packet)
    {
        if (!_decodeSessions.TryGetValue(remoteId, out var session))
        {
            throw new WaypipeException($"remote id {remoteId} names no video stream this channel opened");
        }

        if (!_images.TryGetValue(remoteId, out var image) || image.IsReleased)
        {
            session.Dispose();
            _decodeSessions.Remove(remoteId);
            Forget(remoteId);
            return;
        }

        if (!session.Decode(packet, image.Pixels, image.Stride))
        {
            Log.Warn($"a video packet for remote id {remoteId} produced no frame");
        }
    }

    private WaypipeImage CreateImage(int remoteId, ReadOnlySpan<byte> slice, uint? declaredSize)
    {
        var width = BinaryPrimitives.ReadInt32LittleEndian(slice);
        var height = BinaryPrimitives.ReadInt32LittleEndian(slice[4..]);
        var fourcc = BinaryPrimitives.ReadUInt32LittleEndian(slice[8..]);
        var planes = BinaryPrimitives.ReadInt32LittleEndian(slice[12..]);
        var stride = BinaryPrimitives.ReadUInt32LittleEndian(slice[32..]);

        if (planes != 1)
        {
            throw new WaypipeException(
                $"remote id {remoteId} declares {planes} planes; a waypipe slice is single-plane and linear");
        }

        var format = (DrmFormat)fourcc;
        if (!TryBytesPerPixel(format, out var bytesPerPixel))
        {
            throw new WaypipeException(
                $"remote id {remoteId} names fourcc 0x{fourcc:x8}, which this channel cannot back with a linear region");
        }

        if (width <= 0 || height <= 0)
        {
            throw new WaypipeException($"remote id {remoteId} declares a {width}x{height} image");
        }

        if (stride < (long)width * bytesPerPixel || stride > int.MaxValue / 2)
        {
            throw new WaypipeException(
                $"remote id {remoteId} declares stride {stride} for {width} pixels of {bytesPerPixel} bytes");
        }

        var size = (long)height * stride;
        if (size > _limits.MaxRegionBytes)
        {
            throw new WaypipeException($"remote id {remoteId} names a {size} byte image");
        }

        if (declaredSize is { } declared && size != declared)
        {
            throw new WaypipeException(
                $"remote id {remoteId} declares {declared} bytes and its slice measures {size}");
        }

        Sweep();
        if (_regions.ContainsKey(remoteId) || _images.ContainsKey(remoteId) || _pipes.ContainsKey(remoteId))
        {
            throw new WaypipeException($"remote id {remoteId} already exists");
        }

        if (_regions.Count + _images.Count + _pipes.Count >= _limits.MaxRemoteIds ||
            _regionBytes + size > _limits.MaxTotalRegionBytes)
        {
            throw new WaypipeException("the channel has asked for more shared memory than it is allowed");
        }

        var region = new SharedMemoryRegion((int)size);
        region.AddRef();
        var image = new WaypipeImage(region, width, height, format, (int)stride);
        _images[remoteId] = image;
        _unclaimed.Add(remoteId);
        _regionBytes += size;
        return image;
    }

    private static bool TryBytesPerPixel(DrmFormat format, out int bytesPerPixel)
    {
        switch (format)
        {
            case DrmFormat.Xrgb8888:
            case DrmFormat.Argb8888:
            case DrmFormat.Xbgr8888:
            case DrmFormat.Abgr8888:
            case DrmFormat.Xrgb2101010:
            case DrmFormat.Argb2101010:
            case DrmFormat.Xbgr2101010:
            case DrmFormat.Abgr2101010:
                bytesPerPixel = 4;
                return true;
            case DrmFormat.Xbgr16161616f:
            case DrmFormat.Abgr16161616f:
                bytesPerPixel = 8;
                return true;
            case DrmFormat.Rgb565:
                bytesPerPixel = 2;
                return true;
            default:
                bytesPerPixel = 0;
                return false;
        }
    }

    private void ExtendFile(int remoteId, int size)
    {
        var region = RegionOf(remoteId);
        if (size <= region.Size)
        {
            return;
        }

        if (size > _limits.MaxRegionBytes || _regionBytes + (size - region.Size) > _limits.MaxTotalRegionBytes)
        {
            throw new WaypipeException($"growing remote id {remoteId} to {size} bytes is over the channel's budget");
        }

        _regionBytes += size - region.Size;
        region.Grow(size);
    }

    private void Fill(int remoteId, int start, int end, ReadOnlySpan<byte> payload)
    {
        var region = RegionOf(remoteId);
        if (end <= start || start < 0 || end > region.Size)
        {
            throw new WaypipeException(
                $"a fill for remote id {remoteId} names [{start},{end}) of a {region.Size} byte region");
        }

        var span = region.Span.Slice(start, end - start);
        Decompress(payload, span);
    }

    private void Diff(int remoteId, int diffSize, int trailing, ReadOnlySpan<byte> payload)
    {
        var region = RegionOf(remoteId);
        if (diffSize < 0 || trailing < 0 || diffSize % 4 != 0)
        {
            throw new WaypipeException(
                $"a diff for remote id {remoteId} declares {diffSize} bytes of blocks and {trailing} trailing");
        }

        var total = diffSize + trailing;
        if (total > region.Size + 8 || total > _limits.MaxRegionBytes)
        {
            throw new WaypipeException($"a diff for remote id {remoteId} is longer than the region it applies to");
        }

        var decoded = Rent(total);
        Decompress(payload, decoded.AsSpan(0, total));
        Apply(region.Span, decoded.AsSpan(0, total), diffSize, trailing, remoteId);
    }

    private static void Apply(
        Span<byte> region, ReadOnlySpan<byte> diff, int diffSize, int trailing, int remoteId)
    {
        var position = 0;
        while (position < diffSize)
        {
            if (position + 8 > diffSize)
            {
                throw new WaypipeException($"a diff for remote id {remoteId} ends inside an interval header");
            }

            var start = (long)BinaryPrimitives.ReadUInt32LittleEndian(diff[position..]) * 4;
            var end = (long)BinaryPrimitives.ReadUInt32LittleEndian(diff[(position + 4)..]) * 4;
            position += 8;

            if (end <= start || end > region.Length || position + (end - start) > diffSize)
            {
                throw new WaypipeException(
                    $"a diff for remote id {remoteId} names [{start},{end}) of a {region.Length} byte region");
            }

            diff.Slice(position, (int)(end - start)).CopyTo(region[(int)start..]);
            position += (int)(end - start);
        }

        if (trailing > 0)
        {
            if (trailing > region.Length)
            {
                throw new WaypipeException($"a diff for remote id {remoteId} has more trailing bytes than the region holds");
            }

            diff.Slice(diffSize, trailing).CopyTo(region[(region.Length - trailing)..]);
        }
    }

    internal WaypipePipe CreateOwnedPipe(int remoteId, WaypipeMessageType kind)
    {
        if (_regions.ContainsKey(remoteId) || _images.ContainsKey(remoteId) || _pipes.ContainsKey(remoteId))
        {
            throw new WaypipeException($"remote id {remoteId} already exists");
        }

        var pipe = new WaypipePipe(remoteId, kind, _limits.MaxPipeBytes);
        _pipes[remoteId] = pipe;
        return pipe;
    }

    private void OpenPipe(int remoteId, WaypipeMessageType type)
    {
        Sweep();
        if (_regions.ContainsKey(remoteId) || _images.ContainsKey(remoteId) || _pipes.ContainsKey(remoteId))
        {
            throw new WaypipeException($"remote id {remoteId} already exists");
        }

        if (_regions.Count + _images.Count + _pipes.Count >= _limits.MaxRemoteIds)
        {
            throw new WaypipeException("the channel has opened more remote ids than it is allowed");
        }

        var pipe = new WaypipePipe(remoteId, type, _limits.MaxPipeBytes);
        if (type != WaypipeMessageType.OpenIWPipe)
        {
            AttachWriter(pipe);
        }

        _pipes[remoteId] = pipe;
    }

    internal void AttachWriter(WaypipePipe pipe) =>
        pipe.Attach(
            (target, bytes) => RaiseSend(WaypipeMessageType.PipeTransfer, target.RemoteId, bytes),
            target => RaiseSend(WaypipeMessageType.PipeShutdownW, target.RemoteId, default));

    private void Transfer(int remoteId, ReadOnlySpan<byte> payload)
    {
        if (_pipes.TryGetValue(remoteId, out var pipe))
        {
            pipe.Receive(payload);
        }
    }

    private void Shutdown(int remoteId, WaypipeMessageType type)
    {
        if (!_pipes.TryGetValue(remoteId, out var pipe))
        {
            return;
        }

        pipe.Shutdown(type);
        if (type == WaypipeMessageType.PipeShutdownW && !pipe.CanWrite)
        {
            RaiseSend(WaypipeMessageType.PipeShutdownR, remoteId, default);
            pipe.Shutdown(WaypipeMessageType.PipeShutdownR);
        }

        if (pipe.IsFinished)
        {
            _pipes.Remove(remoteId);
        }
    }

    private SharedMemoryRegion RegionOf(int remoteId) =>
        TryResolve(remoteId, out var region)
            ? region
            : throw new WaypipeException($"remote id {remoteId} names no file this channel opened");

    private bool TryResolve(int remoteId, out SharedMemoryRegion region)
    {
        if (_regions.TryGetValue(remoteId, out var found) && !found.IsReleased)
        {
            region = found;
            return true;
        }

        if (_images.TryGetValue(remoteId, out var image) && !image.IsReleased)
        {
            region = image.Region;
            return true;
        }

        Forget(remoteId);
        region = null!;
        return false;
    }

    private void Forget(int remoteId)
    {
        if (_regions.Remove(remoteId, out var region))
        {
            _regionBytes -= region.Size;
            _unclaimed.Remove(remoteId);
        }

        if (_images.Remove(remoteId, out var image))
        {
            _regionBytes -= image.Region.Size;
            _unclaimed.Remove(remoteId);
            if (_decodeSessions.Remove(remoteId, out var session))
            {
                session.Dispose();
            }
        }
    }

    private void Sweep()
    {
        List<int>? dead = null;
        foreach (var (remoteId, region) in _regions)
        {
            if (region.IsReleased)
            {
                (dead ??= []).Add(remoteId);
            }
        }

        foreach (var (remoteId, image) in _images)
        {
            if (image.IsReleased)
            {
                (dead ??= []).Add(remoteId);
            }
        }

        if (dead is null)
        {
            return;
        }

        foreach (var remoteId in dead)
        {
            Forget(remoteId);
        }
    }

    private void Decompress(ReadOnlySpan<byte> payload, Span<byte> destination)
    {
        switch (Compression)
        {
            case WaypipeCompression.None:
                if (payload.Length != destination.Length)
                {
                    throw new WaypipeException(
                        $"an uncompressed payload of {payload.Length} bytes fills {destination.Length}");
                }

                payload.CopyTo(destination);
                return;

            case WaypipeCompression.Lz4:
                int written;
                try
                {
                    written = LZ4Codec.Decode(payload, destination);
                }
                catch (Exception ex)
                {
                    throw new WaypipeException("an LZ4 block did not decode", ex);
                }

                if (written != destination.Length)
                {
                    throw new WaypipeException(
                        $"an LZ4 block decoded to {written} bytes where {destination.Length} were promised");
                }

                return;

            case WaypipeCompression.Zstd:
                int unwrapped;
                try
                {
                    unwrapped = (_zstd ??= new Decompressor()).Unwrap(payload, destination);
                }
                catch (Exception ex) when (ex is ZstdException or ArgumentException or InvalidOperationException)
                {
                    throw new WaypipeException("a zstd frame did not decode", ex);
                }

                if (unwrapped != destination.Length)
                {
                    throw new WaypipeException(
                        $"a zstd frame decoded to {unwrapped} bytes where {destination.Length} were promised");
                }

                return;

            default:
                throw new WaypipeException($"this transport does not decompress {Compression}");
        }
    }

    private byte[] Rent(int size)
    {
        if (_scratch.Length < size)
        {
            _scratch = new byte[Math.Max(size, _scratch.Length * 2)];
        }

        return _scratch;
    }

    private static void Require(bool condition, WaypipeMessageType type, string what)
    {
        if (!condition)
        {
            throw new WaypipeException($"a {type} message is too short to carry {what}");
        }
    }

    internal void RaiseSend(WaypipeMessageType type, int remoteId, ReadOnlyMemory<byte> payload) =>
        Send?.Invoke(type, remoteId, payload);
}
