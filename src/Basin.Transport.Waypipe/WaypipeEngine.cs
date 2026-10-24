using System.Buffers.Binary;
using K4os.Compression.LZ4;
using Wayland.Server.Shm;
using ZstdSharp;
using static Basin.Transport.Waypipe.WaypipeLog;

namespace Basin.Transport.Waypipe;

/// <summary>
/// Applies decoded waypipe messages: it rebuilds remote buffers, images and pipes, and delivers protocol bytes to a <see cref="WaypipeClientTransport"/>. It is not thread safe.
/// </summary>
public sealed class WaypipeEngine : IDisposable
{
    private readonly Dictionary<int, SharedMemoryRegion> _regions = [];
    private readonly Dictionary<int, WaypipeImage> _images = [];
    private readonly Dictionary<int, Basin.Capabilities.IVideoDecodeSession> _decodeSessions = [];
    private readonly Dictionary<int, Action<nint, bool>> _completions = [];
    private readonly Queue<(WaypipeMessageType Type, byte[] Body)> _deferred = new();
    private readonly HashSet<int> _unclaimed = [];
    private readonly Dictionary<int, WaypipePipe> _pipes = [];
    private readonly Queue<int> _pending = new();
    private readonly WaypipeClientTransport _transport;
    private readonly WaypipeLimits _limits;
    private readonly WaypipeChannelOptions _options;
    private byte[] _scratch = new byte[64 * 1024];
    private Decompressor? _zstd;
    private long _regionBytes;
    private WaypipeImage? _awaitingImage;
    private int _awaitingRemoteId = -1;
    private bool _draining;
    private bool _disposed;
    private bool _videoFaultWarned;

    /// <summary>
    /// Initializes a new instance of the <see cref="WaypipeEngine"/> class.
    /// </summary>
    /// <param name="transport">The transport that gets the protocol bytes and descriptor slots.</param>
    /// <param name="compression">The compression of buffer contents from the peer.</param>
    /// <param name="limits">The limits on the peer, or <see langword="null"/> for the defaults.</param>
    /// <param name="options">The content that the engine accepts, or <see langword="null"/> for the defaults.</param>
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

    /// <summary>
    /// Gets the compression that the engine expects for buffer contents.
    /// </summary>
    public WaypipeCompression Compression { get; }

    /// <summary>
    /// Gets or sets the video codec that the connection header announced. A video stream with another codec ends the channel. It is <see langword="null"/> for no video.
    /// </summary>
    public Basin.Capabilities.VideoCodec? ExpectedVideoCodec { get; set; }

    /// <summary>
    /// Gets a value indicating whether the peer sent a close message.
    /// </summary>
    public bool Closed { get; private set; }

    /// <summary>
    /// Gets the number of buffers, images and pipes that are still live. The get first drops the ones that the compositor released.
    /// </summary>
    public int LiveRemoteIds
    {
        get
        {
            Sweep();
            return _regions.Count + _images.Count + _pipes.Count;
        }
    }

    internal long RegionBytes => _regionBytes;

    /// <summary>
    /// Occurs when the engine must send a message to the peer, for example pipe bytes. The arguments are the type, the remote id and the payload.
    /// </summary>
    public event Action<WaypipeMessageType, int, ReadOnlyMemory<byte>>? Send;

    /// <summary>
    /// Occurs when a message that was deferred behind an asynchronous video decode fails. Without a handler, the error goes to the caller of the completion.
    /// </summary>
    public event Action<Exception>? Failed;

    /// <summary>
    /// Gets a value indicating whether the engine waits for an asynchronous video decode. Messages that arrive during the wait are queued.
    /// </summary>
    public bool AwaitingFrame => _awaitingImage is not null;

    /// <summary>
    /// Applies one message, or queues it while a video decode is pending.
    /// </summary>
    /// <param name="type">The message type from the frame header.</param>
    /// <param name="body">The message bytes after the frame header, without padding.</param>
    /// <exception cref="WaypipeException">The message is malformed, goes over a limit or is a type that the channel refuses.</exception>
    /// <exception cref="ObjectDisposedException">The engine is disposed.</exception>
    public void Apply(WaypipeMessageType type, ReadOnlySpan<byte> body)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_awaitingImage is not null)
        {
            _deferred.Enqueue((type, body.ToArray()));
            return;
        }

        ApplyCore(type, body);
    }

    private void ApplyCore(WaypipeMessageType type, ReadOnlySpan<byte> body)
    {
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
                        "The channel refuses OpenDmabuf because it was not asked for dmabuf. Start the session with --gpu to carry dmabufs.");
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
                        $"The channel refuses {type} because it has no video decoder. Start the session with --video to carry video.");
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
                        $"The channel refuses {type} because it has no video decoder. Start the session with --video to carry video.");
                }

                Require(body.Length >= 4, type, "a remote id");
                DecodePacket(BinaryPrimitives.ReadInt32LittleEndian(body), body[4..]);
                break;

            case WaypipeMessageType.OpenDmaVidSrcV2:
                throw new WaypipeException(
                    $"The channel refuses {type} because it asks the display side to encode. The display side never owns a dmabuf.");

            case WaypipeMessageType.OpenDmaVidSrc:
            case WaypipeMessageType.OpenDmaVidDst:
                throw new WaypipeException(
                    $"The channel refuses {type} because it is legacy waypipe-c video. waypipe 0.11 does not send it.");

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
                    $"The transport refuses {type}. Video needs a channel started with --video, and timelines and session resume are not supported.");
        }
    }

    /// <summary>
    /// Releases every buffer, image and decode session that the engine holds, and completes the forwarded pipes. A second call does nothing.
    /// </summary>
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

        foreach (var (remoteId, session) in _decodeSessions)
        {
            Unsubscribe(remoteId, session);
            session.Dispose();
        }

        if (_awaitingImage is not null)
        {
            ClearAwaiting();
        }

        _deferred.Clear();
        _completions.Clear();
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
                throw new WaypipeException("A protocol message is shorter than its own header.");
            }

            var header2 = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4));
            var length = WaypipeWire.MessageLength(header2);
            if (length < 8 || length % 4 != 0 || offset + length > bytes.Length)
            {
                throw new WaypipeException(
                    $"A protocol message declares {length} bytes, but only {bytes.Length - offset} bytes remain.");
            }

            var tagged = WaypipeWire.TaggedFdCount(header2);
            for (var i = 0; i < tagged; i++)
            {
                if (_pending.Count == 0)
                {
                    throw new WaypipeException(
                        "A protocol message names more descriptors than the channel has remote ids for.");
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

        throw new WaypipeException($"Remote id {remoteId} arrived before the peer created it.");
    }

    private void Queue(int remoteId)
    {
        if (_pending.Count > _limits.MaxRemoteIds)
        {
            throw new WaypipeException("The channel queued more remote ids than one message can carry.");
        }

        _pending.Enqueue(remoteId);
    }

    private void OpenFile(int remoteId, int size)
    {
        if (size <= 0 || size > _limits.MaxRegionBytes)
        {
            throw new WaypipeException($"Remote id {remoteId} names a file of {size} bytes, which is not a valid size.");
        }

        Sweep();
        if (_regions.ContainsKey(remoteId) || _images.ContainsKey(remoteId) || _pipes.ContainsKey(remoteId))
        {
            throw new WaypipeException($"Remote id {remoteId} already exists.");
        }

        if (_regions.Count + _images.Count + _pipes.Count >= _limits.MaxRemoteIds ||
            _regionBytes + size > _limits.MaxTotalRegionBytes)
        {
            throw new WaypipeException("The peer asked for more shared memory than the channel limit allows.");
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
            _ => throw new WaypipeException($"Remote id {remoteId} names video format {flags & 0xff}, which is not a known codec."),
        };

        if (ExpectedVideoCodec != codec)
        {
            throw new WaypipeException(
                $"Remote id {remoteId} opens {codec}, but the connection header promised {ExpectedVideoCodec?.ToString() ?? "no video"}.");
        }

        var image = CreateImage(remoteId, slice, declaredSize: null);
        var session = _options.VideoDecoder!.Open(codec, image.Width, image.Height, image.Format);
        _decodeSessions[remoteId] = session;
        if (session is Basin.Capabilities.IAsyncVideoDecodeSession asynchronous)
        {
            Action<nint, bool> completion = (destination, produced) => OnDecodeCompleted(remoteId, destination, produced);
            asynchronous.Completed += completion;
            _completions[remoteId] = completion;
        }

        Log.Debug(
            $"video: remote id {remoteId} is a {codec} stream into a {image.Width}x{image.Height} host region");
    }

    private void DecodePacket(int remoteId, ReadOnlySpan<byte> packet)
    {
        if (!_decodeSessions.TryGetValue(remoteId, out var session))
        {
            throw new WaypipeException($"Remote id {remoteId} names no video stream that this channel opened.");
        }

        if (!_images.TryGetValue(remoteId, out var image) || image.IsReleased)
        {
            Unsubscribe(remoteId, session);
            session.Dispose();
            _decodeSessions.Remove(remoteId);
            Forget(remoteId);
            return;
        }

        if (session is Basin.Capabilities.IAsyncVideoDecodeSession)
        {
            image.AddRef();
            _awaitingImage = image;
            _awaitingRemoteId = remoteId;
            bool accepted;
            try
            {
                accepted = session.Decode(packet, image.Pixels, image.Stride);
            }
            catch
            {
                if (_awaitingImage is not null)
                {
                    ClearAwaiting();
                }

                throw;
            }

            if (!accepted && _awaitingImage is not null)
            {
                if (FirstVideoFault())
                {
                    Log.Warn($"video: decoder refused a packet for remote id {remoteId}");
                }
                else
                {
                    Log.Debug($"video: decoder refused a packet for remote id {remoteId}");
                }

                ClearAwaiting();
            }

            return;
        }

        if (!session.Decode(packet, image.Pixels, image.Stride))
        {
            if (FirstVideoFault())
            {
                Log.Warn($"video: packet for remote id {remoteId} produced no frame");
            }
            else
            {
                Log.Debug($"video: packet for remote id {remoteId} produced no frame");
            }
        }
    }

    private void OnDecodeCompleted(int remoteId, nint destination, bool produced)
    {
        if (_disposed)
        {
            return;
        }

        if (_awaitingImage is null || _awaitingRemoteId != remoteId)
        {
            if (FirstVideoFault())
            {
                Log.Warn($"video: remote id {remoteId} completed an unexpected frame, frame ignored");
            }
            else
            {
                Log.Debug($"video: remote id {remoteId} completed an unexpected frame, frame ignored");
            }

            return;
        }

        if (destination != _awaitingImage.Pixels)
        {
            if (FirstVideoFault())
            {
                Log.Warn($"video: remote id {remoteId} completed a frame into a different region");
            }
            else
            {
                Log.Debug($"video: remote id {remoteId} completed a frame into a different region");
            }
        }

        if (!produced)
        {
            if (FirstVideoFault())
            {
                Log.Warn($"video: packet for remote id {remoteId} produced no frame");
            }
            else
            {
                Log.Debug($"video: packet for remote id {remoteId} produced no frame");
            }
        }

        ClearAwaiting();
        Drain();
    }

    private void ClearAwaiting()
    {
        var image = _awaitingImage!;
        _awaitingImage = null;
        _awaitingRemoteId = -1;
        image.Release();
    }

    private void Drain()
    {
        if (_draining)
        {
            return;
        }

        _draining = true;
        try
        {
            while (_awaitingImage is null && !_disposed && _deferred.TryDequeue(out var next))
            {
                ApplyCore(next.Type, next.Body);
            }
        }
        catch (Exception failure) when (Failed is not null && failure is not OutOfMemoryException)
        {
            _deferred.Clear();
            Failed.Invoke(failure);
        }
        finally
        {
            _draining = false;
        }
    }

    private void Unsubscribe(int remoteId, Basin.Capabilities.IVideoDecodeSession session)
    {
        if (session is Basin.Capabilities.IAsyncVideoDecodeSession asynchronous
            && _completions.Remove(remoteId, out var completion))
        {
            asynchronous.Completed -= completion;
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
                $"Remote id {remoteId} declares {planes} planes. A waypipe slice must have one linear plane.");
        }

        var format = (DrmFormat)fourcc;
        if (!TryBytesPerPixel(format, out var bytesPerPixel))
        {
            throw new WaypipeException(
                $"Remote id {remoteId} names fourcc 0x{fourcc:x8}, which the channel cannot hold in a linear region.");
        }

        if (width <= 0 || height <= 0)
        {
            throw new WaypipeException($"Remote id {remoteId} declares a {width}x{height} image, which is not a valid size.");
        }

        if (stride < (long)width * bytesPerPixel || stride > int.MaxValue / 2)
        {
            throw new WaypipeException(
                $"Remote id {remoteId} declares stride {stride}, which does not fit {width} pixels of {bytesPerPixel} bytes.");
        }

        var size = (long)height * stride;
        if (size > _limits.MaxRegionBytes)
        {
            throw new WaypipeException($"Remote id {remoteId} names an image of {size} bytes, which is not a valid size.");
        }

        if (declaredSize is { } declared && size != declared)
        {
            throw new WaypipeException(
                $"Remote id {remoteId} declares {declared} bytes, but its slice measures {size} bytes.");
        }

        Sweep();
        if (_regions.ContainsKey(remoteId) || _images.ContainsKey(remoteId) || _pipes.ContainsKey(remoteId))
        {
            throw new WaypipeException($"Remote id {remoteId} already exists.");
        }

        if (_regions.Count + _images.Count + _pipes.Count >= _limits.MaxRemoteIds ||
            _regionBytes + size > _limits.MaxTotalRegionBytes)
        {
            throw new WaypipeException("The peer asked for more shared memory than the channel limit allows.");
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
            throw new WaypipeException($"Remote id {remoteId} cannot grow to {size} bytes. That is over the budget of the channel.");
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
                $"A fill for remote id {remoteId} names [{start},{end}), outside a region of {region.Size} bytes.");
        }

        region.Touch(end);
        var span = region.Span.Slice(start, end - start);
        Decompress(payload, span);
    }

    private void Diff(int remoteId, int diffSize, int trailing, ReadOnlySpan<byte> payload)
    {
        var region = RegionOf(remoteId);
        if (diffSize < 0 || trailing < 0 || diffSize % 4 != 0)
        {
            throw new WaypipeException(
                $"A diff for remote id {remoteId} declares {diffSize} bytes of blocks and {trailing} trailing bytes, which do not agree.");
        }

        var declared = (long)diffSize + trailing;
        if (declared > region.Size + 8L || declared > _limits.MaxRegionBytes)
        {
            throw new WaypipeException($"A diff for remote id {remoteId} is longer than its region.");
        }

        var total = (int)declared;
        var decoded = Rent(total);
        Decompress(payload, decoded.AsSpan(0, total));
        Apply(region, decoded.AsSpan(0, total), diffSize, trailing, remoteId);
    }

    private static void Apply(
        SharedMemoryRegion mapped, ReadOnlySpan<byte> diff, int diffSize, int trailing, int remoteId)
    {
        var size = mapped.Size;
        var position = 0;
        while (position < diffSize)
        {
            if (position + 8 > diffSize)
            {
                throw new WaypipeException($"A diff for remote id {remoteId} ends inside an interval header.");
            }

            var start = (long)BinaryPrimitives.ReadUInt32LittleEndian(diff[position..]) * 4;
            var end = (long)BinaryPrimitives.ReadUInt32LittleEndian(diff[(position + 4)..]) * 4;
            position += 8;

            if (end <= start || end > size || position + (end - start) > diffSize)
            {
                throw new WaypipeException(
                    $"A diff for remote id {remoteId} names [{start},{end}), outside a region of {size} bytes.");
            }

            mapped.Touch((int)end);
            diff.Slice(position, (int)(end - start)).CopyTo(mapped.Span[(int)start..]);
            position += (int)(end - start);
        }

        if (trailing > 0)
        {
            if (trailing > size)
            {
                throw new WaypipeException($"A diff for remote id {remoteId} has more trailing bytes than its region holds.");
            }

            mapped.Touch(size);
            diff.Slice(diffSize, trailing).CopyTo(mapped.Span[(size - trailing)..]);
        }
    }

    internal WaypipePipe CreateOwnedPipe(int remoteId, WaypipeMessageType kind)
    {
        if (_regions.ContainsKey(remoteId) || _images.ContainsKey(remoteId) || _pipes.ContainsKey(remoteId))
        {
            throw new WaypipeException($"Remote id {remoteId} already exists.");
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
            throw new WaypipeException($"Remote id {remoteId} already exists.");
        }

        if (_regions.Count + _images.Count + _pipes.Count >= _limits.MaxRemoteIds)
        {
            throw new WaypipeException("The peer opened more remote ids than the channel limit allows.");
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
            : throw new WaypipeException($"Remote id {remoteId} names no file that this channel opened.");

    private bool FirstVideoFault()
    {
        if (_videoFaultWarned)
        {
            return false;
        }

        _videoFaultWarned = true;
        return true;
    }

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
                Unsubscribe(remoteId, session);
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
                        $"An uncompressed payload of {payload.Length} bytes does not match its destination of {destination.Length} bytes.");
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
                    throw new WaypipeException("An LZ4 block did not decode.", ex);
                }

                if (written != destination.Length)
                {
                    throw new WaypipeException(
                        $"An LZ4 block decoded to {written} bytes, but the frame promised {destination.Length} bytes.");
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
                    throw new WaypipeException("A zstd frame did not decode.", ex);
                }

                if (unwrapped != destination.Length)
                {
                    throw new WaypipeException(
                        $"A zstd frame decoded to {unwrapped} bytes, but the frame promised {destination.Length} bytes.");
                }

                return;

            default:
                throw new WaypipeException($"The transport cannot decompress {Compression}.");
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
            throw new WaypipeException($"A {type} message is too short to carry {what}.");
        }
    }

    internal void RaiseSend(WaypipeMessageType type, int remoteId, ReadOnlyMemory<byte> payload) =>
        Send?.Invoke(type, remoteId, payload);
}
