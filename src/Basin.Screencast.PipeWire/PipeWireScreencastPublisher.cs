using System.Runtime.InteropServices;
using Basin.Capabilities;
using Basin.Diagnostics;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;
using static Basin.Screencast.PipeWire.PipeWireLog;

namespace Basin.Screencast.PipeWire;

/// <summary>
/// Publishes capture sources as PipeWire video source nodes. It runs the PipeWire loop as an fd on the compositor loop, and sends a frame when the source has damage.
/// </summary>
public sealed unsafe class PipeWireScreencastPublisher : IScreencastPublisher, ICaptureDamageObserver, IDisposable
{
    private const int CursorMaxSize = 256;
    private const int RetryMilliseconds = 16;

    private readonly ICompositorEventLoop _loop;
    private readonly IScreenCapture _capture;
    private readonly OutputLayout _layout;
    private readonly IAllocator? _dmabufAllocator;
    private readonly PipeWireLoop _pwLoop;
    private readonly PipeWireContext _context;
    private readonly PipeWireCore _core;
    private readonly IEventSource _fdSource;
    private readonly IEventSource _retry;
    private readonly Dictionary<ulong, Entry> _streams = [];
    private bool _flushScheduled;
    private uint _cursorSerial = 1;
    private bool _disposed;

    private PipeWireScreencastPublisher(
        ICompositorEventLoop loop,
        IScreenCapture capture,
        OutputLayout layout,
        IAllocator? dmabufAllocator,
        PipeWireLoop pwLoop,
        PipeWireContext context,
        PipeWireCore core)
    {
        _loop = loop;
        _capture = capture;
        _layout = layout;
        _dmabufAllocator = dmabufAllocator;
        _pwLoop = pwLoop;
        _context = context;
        _core = core;
        _fdSource = loop.AddFd(pwLoop.Fd, FdReadiness.Readable, (_, _) => Pump(0));
        _retry = loop.AddTimer(ScheduleFlush);
        capture.AddDamageObserver(this);
        BasinCounters.Track();
    }

    /// <summary>
    /// Loads PipeWire, connects to the daemon, and starts to observe damage on <paramref name="capture"/>.
    /// </summary>
    /// <param name="loop">The compositor loop that drives the PipeWire loop.</param>
    /// <param name="capture">The capture that fills each stream buffer.</param>
    /// <param name="layout">The layout that maps a region source onto outputs.</param>
    /// <param name="dmabufAllocator">The allocator for dmabuf stream buffers, or <see langword="null"/> to offer shared memory buffers only.</param>
    /// <param name="clientName">The PipeWire application and node name. The default is <c>basin</c>.</param>
    /// <returns>The publisher that the caller must dispose, or <see langword="null"/> when the library is missing or the connection fails.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="loop"/>, <paramref name="capture"/> or <paramref name="layout"/> is <see langword="null"/>.</exception>
    public static PipeWireScreencastPublisher? TryCreate(
        ICompositorEventLoop loop,
        IScreenCapture capture,
        OutputLayout layout,
        IAllocator? dmabufAllocator = null,
        string? clientName = null)
    {
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(capture);
        ArgumentNullException.ThrowIfNull(layout);
        if (!PipeWireLibraryProbe.IsAvailable(out var whyNot))
        {
            Log.Info($"screencast unavailable: {whyNot}");
            return null;
        }

        PipeWireLoop? pwLoop = null;
        PipeWireContext? context = null;
        try
        {
            PipeWireLibrary.Init();
            pwLoop = PipeWireLoop.Create();
            context = new PipeWireContext(pwLoop);
            using var properties = PipeWireProperties.From(
                "application.name", clientName ?? "basin",
                "node.name", clientName ?? "basin");
            var core = context.Connect(properties);
            return new PipeWireScreencastPublisher(
                loop, capture, layout, dmabufAllocator, pwLoop, context, core);
        }
        catch (Exception error) when (error is DllNotFoundException or PipeWireException or EntryPointNotFoundException)
        {
            context?.Dispose();
            pwLoop?.Dispose();
            Log.Warn($"screencast unavailable: {error.Message}");
            return null;
        }
    }

    /// <summary>
    /// Gets the number of streams that are published and not closed.
    /// </summary>
    public int StreamCount => _streams.Count;

    /// <summary>
    /// Reads the state that the PipeWire consumer negotiated for a stream.
    /// </summary>
    /// <param name="streamId">The id from <see cref="ScreencastRequest.StreamId"/>.</param>
    /// <param name="dmabuf"><see langword="true"/> when the consumer chose dmabuf buffers.</param>
    /// <param name="nodeId">The PipeWire node id of the stream, or 0 when the stream has no node.</param>
    /// <returns><see langword="true"/> when the stream exists, otherwise <see langword="false"/>.</returns>
    public bool TryGetNegotiated(ulong streamId, out bool dmabuf, out uint nodeId)
    {
        if (_streams.TryGetValue(streamId, out var entry))
        {
            dmabuf = entry.UsesDmabuf;
            nodeId = entry.Stream?.NodeId ?? 0;
            return true;
        }

        dmabuf = false;
        nodeId = 0;
        return false;
    }

    /// <summary>
    /// Creates a PipeWire video source node for a capture source. The call blocks the compositor thread for up to 2 seconds until PipeWire assigns the node id.
    /// </summary>
    /// <param name="request">The stream id, the source and the cursor mode.</param>
    /// <param name="info">The node id and serial of the new stream, or the failure reason when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the node exists, otherwise <see langword="false"/>. A stream id that is already published fails.</returns>
    /// <exception cref="ObjectDisposedException">The publisher is disposed.</exception>
    public bool TryPublish(in ScreencastRequest request, out ScreencastStreamInfo info)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_streams.ContainsKey(request.StreamId))
        {
            info = new ScreencastStreamInfo { NodeId = 0, FailureReason = "the stream id is already published" };
            return false;
        }

        if (!_capture.TryDescribe(request.Source, out var format))
        {
            info = new ScreencastStreamInfo { NodeId = 0, FailureReason = "the source cannot be captured" };
            return false;
        }

        if (!TryVideoFormat(format.Format, out var videoFormat))
        {
            info = new ScreencastStreamInfo { NodeId = 0, FailureReason = $"{format.Format} has no PipeWire video format" };
            return false;
        }

        var modifiers = DmabufModifiersFor(format.Format);
        var offerDmabuf = modifiers.Length > 0;
        var entry = new Entry(request.StreamId, request.Source, request.Cursor, format, videoFormat, offerDmabuf)
        {
            OfferYuv = OffersYuv(format),
        };
        var name = $"basin-screencast-{request.StreamId}";
        using var properties = PipeWireProperties.From(
            "media.class", "Video/Source",
            "media.name", name,
            "node.name", name,
            "node.description", DescribeSource(request.Source));
        try
        {
            entry.Stream = new PipeWireStream(_core, name, properties);
        }
        catch (PipeWireException error)
        {
            info = new ScreencastStreamInfo { NodeId = 0, FailureReason = error.Message };
            return false;
        }

        entry.Stream.Process = _ => OnProcess(entry);
        entry.Stream.ParamChanged += (_, e) => OnParamChanged(entry, e);
        entry.Stream.StateChanged += (_, e) => OnStateChanged(entry, e);
        entry.Stream.BufferAdded += (_, e) => OnBufferAdded(entry, e.Buffer);
        entry.Stream.BufferRemoved += (_, e) => OnBufferRemoved(entry, e.Buffer);

        var size = ((uint)format.Width, (uint)format.Height);
        var refresh = ((uint)RefreshOf(request.Source), 1000u);
        var flags = pw_stream_flags.PW_STREAM_FLAG_DRIVER | pw_stream_flags.PW_STREAM_FLAG_ALLOC_BUFFERS;
        byte[][] formats = offerDmabuf
            ? SpaVideoFormats.BuildDmaBufAndFallback([videoFormat], modifiers, size, defaultFramerate: refresh, minFramerate: (0, 1), maxFramerate: refresh)
            : [SpaVideoFormats.BuildRawChoice([videoFormat], size, defaultFramerate: refresh, minFramerate: (0, 1), maxFramerate: refresh)];
        if (entry.OfferYuv)
        {
            formats =
            [
                .. formats,
                SpaVideoFormats.BuildDmaBuf(
                    [spa_video_format.SPA_VIDEO_FORMAT_NV12], [DrmFormatSet.ModifierLinear], size,
                    defaultFramerate: refresh, minFramerate: (0, 1), maxFramerate: refresh, dontFixate: false),
            ];
        }
        try
        {
            entry.Stream.Connect(spa_direction.SPA_DIRECTION_OUTPUT, uint.MaxValue, flags, formats);
        }
        catch (PipeWireException error)
        {
            entry.Dispose();
            info = new ScreencastStreamInfo { NodeId = 0, FailureReason = error.Message };
            return false;
        }

        var deadline = Environment.TickCount64 + 2000;
        while (entry.Stream.NodeId == uint.MaxValue && Environment.TickCount64 < deadline)
        {
            Pump(20);
        }

        if (entry.Stream.NodeId == uint.MaxValue)
        {
            entry.Dispose();
            info = new ScreencastStreamInfo { NodeId = 0, FailureReason = "PipeWire did not create a node" };
            return false;
        }

        _streams[request.StreamId] = entry;
        BasinCounters.Track();
        info = new ScreencastStreamInfo
        {
            NodeId = entry.Stream.NodeId,
            ObjectSerial = entry.Stream.ObjectSerial,
            DmabufOffered = offerDmabuf,
        };
        Log.Info($"stream {request.StreamId}: node {info.NodeId} serial {info.ObjectSerial}, dmabuf {(offerDmabuf ? "offered" : "not offered")}");
        return true;
    }

    /// <inheritdoc/>
    public void Close(ulong streamId)
    {
        if (_streams.Remove(streamId, out var entry))
        {
            entry.Dispose();
            BasinCounters.Untrack();
            Log.Info($"stream {streamId} closed");
        }
    }

    /// <inheritdoc/>
    public void OnSourceDamaged(IOutput output, Box damage)
    {
        var any = false;
        foreach (var entry in _streams.Values)
        {
            if (!Intersects(entry.Source, output))
            {
                continue;
            }

            entry.MarkDamaged(damage);
            any = true;
        }

        if (any)
        {
            ScheduleFlush();
        }
    }

    /// <inheritdoc/>
    public void OnCursorChanged()
    {
        _cursorSerial++;
        var any = false;
        foreach (var entry in _streams.Values)
        {
            if ((entry.Cursor & (ScreencastCursorMode.Embedded | ScreencastCursorMode.Metadata)) != 0)
            {
                entry.Dirty = true;
                any = true;
            }
        }

        if (any)
        {
            ScheduleFlush();
        }
    }

    /// <summary>
    /// Destroys every stream, stops observing the capture, and disconnects from PipeWire.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _capture.RemoveDamageObserver(this);
        foreach (var entry in _streams.Values)
        {
            entry.Dispose();
            BasinCounters.Untrack();
        }

        _streams.Clear();
        _fdSource.Remove();
        _retry.Remove();
        _core.Dispose();
        _context.Dispose();
        _pwLoop.Dispose();
        BasinCounters.Untrack();
    }

    private static bool TryVideoFormat(DrmFormat format, out spa_video_format video)
    {
        switch (format)
        {
            case DrmFormat.Xrgb8888:
                video = spa_video_format.SPA_VIDEO_FORMAT_BGRx;
                return true;
            case DrmFormat.Argb8888:
                video = spa_video_format.SPA_VIDEO_FORMAT_BGRA;
                return true;
            case DrmFormat.Xbgr8888:
                video = spa_video_format.SPA_VIDEO_FORMAT_RGBx;
                return true;
            case DrmFormat.Abgr8888:
                video = spa_video_format.SPA_VIDEO_FORMAT_RGBA;
                return true;
            default:
                video = default;
                return false;
        }
    }

    private ulong[] DmabufModifiersFor(DrmFormat format)
    {
        if (_dmabufAllocator is null)
        {
            return [];
        }

        var modifiers = new List<ulong>();
        foreach (var modifier in _dmabufAllocator.Formats.ModifiersOf(format))
        {
            if (modifier != DrmFormatSet.ModifierInvalid)
            {
                modifiers.Add(modifier);
            }
        }

        return modifiers.ToArray();
    }

    private string DescribeSource(in CaptureSource source) => source.Kind switch
    {
        CaptureSourceKind.Output when source.OutputTarget is { } output => output.Name,
        CaptureSourceKind.Toplevel => $"window {source.ToplevelId}",
        CaptureSourceKind.Region => $"region {source.LayoutBox.Width}x{source.LayoutBox.Height}",
        _ => "screencast",
    };

    private bool Intersects(in CaptureSource source, IOutput output) => source.Kind switch
    {
        CaptureSourceKind.Output => ReferenceEquals(source.OutputTarget, output),
        CaptureSourceKind.Region => !_layout.BoxOf(output).Intersect(source.LayoutBox).IsEmpty,
        _ => true,
    };

    private int RefreshOf(in CaptureSource source)
    {
        if (source.Kind == CaptureSourceKind.Output && source.OutputTarget is { } output)
        {
            return Math.Max(1, output.CurrentMode.RefreshMilliHz);
        }

        var refresh = 0;
        foreach (var (candidate, _) in _layout.Outputs)
        {
            if (source.Kind != CaptureSourceKind.Region ||
                !_layout.BoxOf(candidate).Intersect(source.LayoutBox).IsEmpty)
            {
                refresh = Math.Max(refresh, candidate.CurrentMode.RefreshMilliHz);
            }
        }

        return refresh > 0 ? refresh : 60_000;
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled || _disposed)
        {
            return;
        }

        _flushScheduled = true;
        _loop.AddIdle(() =>
        {
            _flushScheduled = false;
            if (_disposed)
            {
                return;
            }

            foreach (var entry in _streams.Values)
            {
                if (entry.Dirty && entry.Stream is { } stream &&
                    stream.State == pw_stream_state.PW_STREAM_STATE_STREAMING)
                {
                    entry.Dirty = false;
                    var triggered = stream.TriggerProcess();
                    Log.Debug($"stream {entry.Id}: trigger {(triggered ? "accepted" : "refused")}, driving {stream.IsDriving}");
                }
                else if (entry.Dirty)
                {
                    Log.Debug($"stream {entry.Id}: dirty while {entry.Stream?.State}");
                }
            }
        });
    }

    private void Pump(int timeoutMilliseconds)
    {
        _pwLoop.Enter();
        try
        {
            _pwLoop.Iterate(timeoutMilliseconds);
        }
        finally
        {
            _pwLoop.Leave();
        }
    }

    private void OnParamChanged(Entry entry, PipeWireParamEventArgs e)
    {
        if (e.ParamType != spa_param_type.SPA_PARAM_Format || e.Param.IsNull || entry.Stream is not { } stream)
        {
            return;
        }

        if (entry.OfferYuv && SpaVideoFormats.TryParseRaw(e.Param, out var raw) &&
            raw.format == spa_video_format.SPA_VIDEO_FORMAT_NV12)
        {
            entry.UsesYuv = true;
            entry.UsesDmabuf = true;
            entry.NegotiatedModifier = DrmFormatSet.ModifierLinear;
            var yuvMetas = new List<byte[]>
            {
                SpaBufferParams.Build(
                    buffers: 3,
                    blocks: 2,
                    size: entry.Format.Width * entry.Format.Height,
                    stride: entry.Format.Width,
                    dataTypes: SpaBufferParams.DataTypeMask(spa_data_type.SPA_DATA_DmaBuf)),
                SpaMetaParams.BuildHeader(),
                SpaMetaParams.BuildVideoDamage(),
            };
            if ((entry.Cursor & ScreencastCursorMode.Metadata) != 0)
            {
                yuvMetas.Add(SpaMetaParams.BuildCursor(CursorMaxSize, CursorMaxSize));
            }

            stream.UpdateParams(yuvMetas.ToArray());
            Log.Debug($"stream {entry.Id}: format fixated, NV12 DmaBuf");
            return;
        }

        entry.UsesYuv = false;
        var size = ((uint)entry.Format.Width, (uint)entry.Format.Height);
        var refresh = ((uint)RefreshOf(entry.Source), 1000u);
        if (entry.OfferDmabuf && SpaVideoFormats.NeedsModifierFixation(e.Param))
        {
            var modifiers = CommonModifiers(DmabufModifiersFor(entry.Format.Format), ConsumerModifiers(e.Param));
            var chosen = ChooseModifier(entry, modifiers);
            Log.Debug($"stream {entry.Id}: the consumer asks for a modifier from {modifiers.Length} shared; chose {(chosen is { } picked ? picked.ToString("x") : "none")}");
            if (chosen is { } modifier)
            {
                entry.NegotiatedModifier = modifier;
                stream.UpdateParams(
                    SpaVideoFormats.BuildRaw(entry.VideoFormat, size, refresh, modifier),
                    SpaVideoFormats.BuildRawChoice([entry.VideoFormat], size, defaultFramerate: refresh, minFramerate: (0, 1), maxFramerate: refresh));
            }
            else
            {
                stream.UpdateParams(
                    SpaVideoFormats.BuildRawChoice([entry.VideoFormat], size, defaultFramerate: refresh, minFramerate: (0, 1), maxFramerate: refresh));
            }

            return;
        }

        var hasModifier = SpaVideoFormats.TryGetModifier(e.Param, out var fixated);
        var dmabuf = entry.OfferDmabuf && hasModifier && ChooseModifier(entry, [fixated]) is not null;
        Log.Debug($"stream {entry.Id}: format {(hasModifier ? "carries modifier " + fixated.ToString("x") : "carries no modifier")}, dmabuf offered {entry.OfferDmabuf}, dmabuf {dmabuf}");
        entry.UsesDmabuf = dmabuf;
        if (dmabuf)
        {
            _ = SpaVideoFormats.TryGetModifier(e.Param, out var fixatedModifier);
            entry.NegotiatedModifier = fixatedModifier;
        }

        var dataTypes = dmabuf
            ? SpaBufferParams.DataTypeMask(spa_data_type.SPA_DATA_DmaBuf)
            : SpaBufferParams.DataTypeMask(spa_data_type.SPA_DATA_MemFd);
        var metas = new List<byte[]>
        {
            SpaBufferParams.Build(
                buffers: 3,
                blocks: 1,
                size: entry.Format.Stride * entry.Format.Height,
                stride: entry.Format.Stride,
                dataTypes: dataTypes),
            SpaMetaParams.BuildHeader(),
            SpaMetaParams.BuildVideoDamage(),
        };
        if ((entry.Cursor & ScreencastCursorMode.Metadata) != 0)
        {
            metas.Add(SpaMetaParams.BuildCursor(CursorMaxSize, CursorMaxSize));
        }

        stream.UpdateParams(metas.ToArray());
        Log.Debug($"stream {entry.Id}: format fixated, {(dmabuf ? "DmaBuf" : "MemFd")}");
    }

    private static ulong[] ConsumerModifiers(SpaPod param)
    {
        if (param.IsNull || param.Type != Pipewire.SPA_TYPE_Object)
        {
            return [];
        }

        var obj = param.AsObject();
        if (!obj.TryGetValue((uint)spa_format.SPA_FORMAT_VIDEO_modifier, out var value) || value.IsNull)
        {
            return [];
        }

        if (value.Type != Pipewire.SPA_TYPE_Choice)
        {
            return value.TryGetLong(out var single) ? [unchecked((ulong)single)] : [];
        }

        var choices = value.AsChoice().AsSpan<long>();
        var result = new List<ulong>(choices.Length);
        foreach (var choice in choices)
        {
            var modifier = unchecked((ulong)choice);
            if (!result.Contains(modifier))
            {
                result.Add(modifier);
            }
        }

        return result.ToArray();
    }

    private static ulong[] CommonModifiers(ulong[] offered, ulong[] wanted)
    {
        if (wanted.Length == 0)
        {
            return offered;
        }

        var common = new List<ulong>(offered.Length);
        foreach (var modifier in wanted)
        {
            if (Array.IndexOf(offered, modifier) >= 0)
            {
                common.Add(modifier);
            }
        }

        return common.ToArray();
    }

    private ulong? ChooseModifier(Entry entry, ReadOnlySpan<ulong> candidates)
    {
        if (_dmabufAllocator is null)
        {
            return null;
        }

        foreach (var candidate in candidates)
        {
            var probe = _dmabufAllocator.Allocate(entry.Format.Width, entry.Format.Height, entry.Format.Format, [candidate], BufferUse.Render);
            if (probe is null)
            {
                Log.Debug($"stream {entry.Id}: modifier {candidate:x} does not allocate for rendering");
                continue;
            }

            probe.Destroy();
            return candidate;
        }

        return null;
    }

    private void OnStateChanged(Entry entry, PipeWireStreamStateEventArgs e)
    {
        if (e.NewState == pw_stream_state.PW_STREAM_STATE_STREAMING)
        {
            entry.Dirty = true;
            entry.MarkDamaged(new Box(0, 0, entry.Format.Width, entry.Format.Height));
            ScheduleFlush();
        }
        else if (e.NewState == pw_stream_state.PW_STREAM_STATE_ERROR)
        {
            Log.Warn($"stream {entry.Id}: {e.Error ?? "error"}");
        }
    }

    private bool OffersYuv(in CaptureFormat format)
    {
        if (!_capture.CanCaptureYuv || _dmabufAllocator is not { } allocator)
        {
            return false;
        }

        var probe = allocator.Allocate(format.Width, format.Height, DrmFormat.Nv12, [DrmFormatSet.ModifierLinear], BufferUse.Render);
        probe?.Destroy();
        return probe is not null;
    }

    private void AddYuvBuffer(Entry entry, PipeWireBuffer buffer, Slot slot)
    {
        var data = buffer.Buffer->datas;
        if (buffer.DataCount < 2 || _dmabufAllocator is null ||
            _dmabufAllocator.Allocate(entry.Format.Width, entry.Format.Height, DrmFormat.Nv12, [DrmFormatSet.ModifierLinear], BufferUse.Render)
                is not { } target)
        {
            Log.Warn($"stream {entry.Id}: NV12 allocation failed, the buffer stays empty");
            return;
        }

        if (!target.TryGetDmabuf(out var attributes))
        {
            target.Destroy();
            return;
        }

        slot.Target = target;
        var heights = (entry.Format.Height, (entry.Format.Height + 1) / 2);
        for (var plane = 0; plane < 2; plane++)
        {
            var rows = plane == 0 ? heights.Item1 : heights.Item2;
            data[plane].type = (uint)spa_data_type.SPA_DATA_DmaBuf;
            data[plane].flags = Pipewire.SPA_DATA_FLAG_READABLE;
            data[plane].fd = attributes.Fds[plane];
            data[plane].mapoffset = 0;
            data[plane].maxsize = (uint)(attributes.Offsets[plane] + (attributes.Strides[plane] * rows));
            data[plane].data = null;
            if (data[plane].chunk is not null)
            {
                data[plane].chunk->offset = attributes.Offsets[plane];
                data[plane].chunk->stride = (int)attributes.Strides[plane];
                data[plane].chunk->size = (uint)(attributes.Strides[plane] * rows);
            }
        }

        slot.LumaChunk = (attributes.Offsets[0], (uint)(attributes.Strides[0] * heights.Item1), (int)attributes.Strides[0]);

        entry.Slots[(nint)buffer.Handle] = slot;
    }

    private void OnBufferAdded(Entry entry, PipeWireBuffer buffer)
    {
        if (buffer.IsNull || buffer.DataCount < 1)
        {
            return;
        }

        var data = buffer.Buffer->datas;
        var slot = new Slot();
        if (entry.UsesYuv)
        {
            AddYuvBuffer(entry, buffer, slot);
            return;
        }

        var stride = entry.Format.Stride;
        var size = stride * entry.Format.Height;
        if (entry.UsesDmabuf && (data[0].type & (1u << (int)spa_data_type.SPA_DATA_DmaBuf)) != 0 && _dmabufAllocator is not null)
        {
            var target = _dmabufAllocator.Allocate(
                entry.Format.Width, entry.Format.Height, entry.Format.Format, [entry.NegotiatedModifier], BufferUse.Render);
            if (target is not null && target.TryGetDmabuf(out var attributes))
            {
                slot.Target = target;
                data[0].type = (uint)spa_data_type.SPA_DATA_DmaBuf;
                data[0].flags = Pipewire.SPA_DATA_FLAG_READABLE;
                data[0].fd = attributes.Fds[0];
                data[0].mapoffset = 0;
                data[0].maxsize = (uint)(attributes.Strides[0] * entry.Format.Height + attributes.Offsets[0]);
                data[0].data = null;
                if (data[0].chunk is not null)
                {
                    data[0].chunk->offset = (uint)attributes.Offsets[0];
                    data[0].chunk->stride = (int)attributes.Strides[0];
                    data[0].chunk->size = (uint)(attributes.Strides[0] * entry.Format.Height);
                }

                entry.Slots[(nint)buffer.Handle] = slot;
                return;
            }

            target?.Destroy();
            Log.Warn($"stream {entry.Id}: dmabuf allocation failed, the buffer stays empty");
        }

        if ((data[0].type & (1u << (int)spa_data_type.SPA_DATA_MemFd)) == 0)
        {
            Log.Warn($"stream {entry.Id}: the consumer accepts neither DmaBuf nor MemFd");
            return;
        }

        var fd = memfd_create("basin-screencast", MfdCloexec | MfdAllowSealing);
        if (fd < 0 || ftruncate(fd, size) != 0)
        {
            Log.Warn($"stream {entry.Id}: memfd allocation failed");
            if (fd >= 0)
            {
                _ = close(fd);
            }

            return;
        }

        var map = mmap(null, (nuint)size, ProtRead | ProtWrite, MapShared, fd, 0);
        if (map == (void*)-1)
        {
            Log.Warn($"stream {entry.Id}: memfd map failed");
            _ = close(fd);
            return;
        }

        slot.MemFd = fd;
        slot.Map = (nint)map;
        slot.MapSize = (nuint)size;
        data[0].type = (uint)spa_data_type.SPA_DATA_MemFd;
        data[0].flags = Pipewire.SPA_DATA_FLAG_READABLE;
        data[0].fd = fd;
        data[0].mapoffset = 0;
        data[0].maxsize = (uint)size;
        data[0].data = map;
        if (data[0].chunk is not null)
        {
            data[0].chunk->offset = 0;
            data[0].chunk->stride = stride;
            data[0].chunk->size = 0;
        }

        entry.Slots[(nint)buffer.Handle] = slot;
    }

    private static void OnBufferRemoved(Entry entry, PipeWireBuffer buffer)
    {
        if (entry.Slots.Remove((nint)buffer.Handle, out var slot))
        {
            slot.Dispose();
        }
    }

    private void OnProcess(Entry entry)
    {
        if (entry.Stream is not { } stream)
        {
            return;
        }

        var buffer = stream.DequeueBuffer();
        if (buffer.IsNull)
        {
            return;
        }

        AllocationScope.Begin(forgiving: true);
        try
        {
            var data = buffer[0];
            var found = entry.Slots.TryGetValue((nint)buffer.Handle, out var slot);
            var rendered = found && RenderSlot(entry, slot!, data);
            if (!rendered)
            {
                data.SetChunk(0, 0, entry.Format.Stride);
                entry.Dirty = true;
                _retry.UpdateTimer(RetryMilliseconds);
            }

            WriteHeader(entry, buffer);
            WriteDamage(entry, buffer, rendered);
            WriteCursor(entry, buffer);
        }
        catch (Exception error)
        {
            AllocationScope.Pause();
            Log.Warn($"stream {entry.Id}: frame dropped: {error.Message}");
            AllocationScope.Resume();
            buffer[0].SetChunk(0, 0, entry.Format.Stride);
        }
        finally
        {
            AllocationScope.End();
            stream.QueueBuffer(buffer);
        }
    }

    private bool RenderSlot(Entry entry, Slot slot, PipeWireBufferData data)
    {
        if (slot.Warm)
        {
            return Render(entry, slot, data);
        }

        AllocationScope.Pause();
        try
        {
            slot.Warm = Render(entry, slot, data);
            return slot.Warm;
        }
        finally
        {
            AllocationScope.Resume();
        }
    }

    private bool Render(Entry entry, Slot slot, PipeWireBufferData data)
    {
        if (slot.Target is { } dmabuf)
        {
            if (!_capture.Capture(entry.Source, default, dmabuf))
            {
                AllocationScope.Pause();
                Log.Debug($"stream {entry.Id}: the capture into the dmabuf slot failed");
                AllocationScope.Resume();
                return false;
            }

            if (entry.UsesYuv)
            {
                data.SetChunk(slot.LumaChunk.Offset, slot.LumaChunk.Size, slot.LumaChunk.Stride);
            }
            else
            {
                var chunkStride = data.Stride <= 0 ? entry.Format.Stride : data.Stride;
                data.SetChunk(data.Offset, (uint)(chunkStride * entry.Format.Height), chunkStride);
            }

            return true;
        }

        if (!data.HasMemory)
        {
            return false;
        }

        var target = entry.Staging ??= new MemoryBuffer(entry.Format.Width, entry.Format.Height, entry.Format.Format);
        if (!_capture.Capture(entry.Source, default, target) ||
            !target.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
        }

        try
        {
            var destination = data.Memory;
            var stride = Math.Min(entry.Format.Stride, data.Stride <= 0 ? entry.Format.Stride : data.Stride);
            var rowBytes = Math.Min(entry.Format.Width * 4, stride);
            for (var y = 0; y < entry.Format.Height; y++)
            {
                var sourceRow = new ReadOnlySpan<byte>((byte*)view.Data + (y * view.Stride), rowBytes);
                sourceRow.CopyTo(destination.Slice(y * stride, rowBytes));
            }

            data.SetChunk(0, (uint)(stride * entry.Format.Height), stride);
        }
        finally
        {
            target.EndDataAccess();
        }

        return true;
    }

    private static void WriteHeader(Entry entry, PipeWireBuffer buffer)
    {
        if (!buffer.TryGetHeader(out var header))
        {
            return;
        }

        header.Flags = 0;
        header.Offset = 0;
        header.Pts = MonotonicClock.Nanos;
        header.DtsOffset = 0;
        header.Seq = entry.Sequence++;
    }

    private static void WriteDamage(Entry entry, PipeWireBuffer buffer, bool rendered)
    {
        if (!buffer.TryGetVideoDamage(out var damage) || damage.Capacity == 0)
        {
            return;
        }

        var box = rendered ? entry.TakeDamage() : default;
        if (box.IsEmpty)
        {
            box = new Box(0, 0, entry.Format.Width, entry.Format.Height);
        }

        damage[0].Set(box.X, box.Y, (uint)box.Width, (uint)box.Height);
        damage.Terminate(1);
    }

    private void WriteCursor(Entry entry, PipeWireBuffer buffer)
    {
        if ((entry.Cursor & ScreencastCursorMode.Metadata) == 0 || !buffer.TryGetCursor(out var cursor))
        {
            return;
        }

        if (entry.Source.Kind != CaptureSourceKind.Output || entry.Source.OutputTarget is not { } output ||
            !_capture.TryCursorState(output, out var state) || !state.IsVisible)
        {
            cursor.Id = 0;
            cursor.BitmapOffset = 0;
            return;
        }

        cursor.Id = 1;
        cursor.Flags = 0;
        cursor.Position = new spa_point { x = state.X, y = state.Y };
        cursor.Hotspot = new spa_point { x = state.HotspotX, y = state.HotspotY };
        if (entry.WrittenCursorSerial == _cursorSerial ||
            state.Width <= 0 || state.Height <= 0 || state.Width > CursorMaxSize || state.Height > CursorMaxSize)
        {
            cursor.BitmapOffset = 0;
            return;
        }

        var image = entry.CursorImage;
        if (image is null || image.Width != state.Width || image.Height != state.Height)
        {
            image?.Destroy();
            AllocationScope.Pause();
            image = new MemoryBuffer(state.Width, state.Height, DrmFormat.Argb8888);
            AllocationScope.Resume();
            entry.CursorImage = image;
        }

        if (!_capture.Capture(CaptureSource.Cursor(output), default, image) ||
            !image.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            cursor.BitmapOffset = 0;
            return;
        }

        try
        {
            cursor.BitmapOffset = (uint)sizeof(spa_meta_cursor);
            var bitmap = cursor.Bitmap;
            if (bitmap.IsNull)
            {
                cursor.BitmapOffset = 0;
                return;
            }

            var stride = state.Width * 4;
            bitmap.Format = spa_video_format.SPA_VIDEO_FORMAT_BGRA;
            bitmap.Size = new spa_rectangle { width = (uint)state.Width, height = (uint)state.Height };
            bitmap.Stride = stride;
            bitmap.Offset = (uint)sizeof(spa_meta_bitmap);
            var pixels = bitmap.Pixels;
            if (pixels.Length < stride * state.Height)
            {
                cursor.BitmapOffset = 0;
                return;
            }

            for (var y = 0; y < state.Height; y++)
            {
                new ReadOnlySpan<byte>((byte*)view.Data + (y * view.Stride), stride).CopyTo(pixels.Slice(y * stride, stride));
            }

            entry.WrittenCursorSerial = _cursorSerial;
        }
        finally
        {
            image.EndDataAccess();
        }
    }

    private sealed class Slot : IDisposable
    {
        public BufferBase? Target { get; set; }

        public int MemFd { get; set; } = -1;

        public nint Map { get; set; }

        public nuint MapSize { get; set; }

        public (uint Offset, uint Size, int Stride) LumaChunk { get; set; }

        public bool Warm { get; set; }

        public void Dispose()
        {
            Target?.Destroy();
            Target = null;
            if (Map != 0)
            {
                _ = munmap((void*)Map, MapSize);
                Map = 0;
            }

            if (MemFd >= 0)
            {
                _ = close(MemFd);
                MemFd = -1;
            }
        }
    }

    private sealed class Entry(
        ulong id,
        CaptureSource source,
        ScreencastCursorMode cursor,
        CaptureFormat format,
        spa_video_format videoFormat,
        bool offerDmabuf) : IDisposable
    {
        private Box _damage;

        public ulong Id { get; } = id;

        public CaptureSource Source { get; } = source;

        public ScreencastCursorMode Cursor { get; } = cursor;

        public CaptureFormat Format { get; } = format;

        public spa_video_format VideoFormat { get; } = videoFormat;

        public bool OfferDmabuf { get; } = offerDmabuf;

        public bool UsesDmabuf { get; set; }

        public bool OfferYuv { get; set; }

        public bool UsesYuv { get; set; }

        public ulong NegotiatedModifier { get; set; }

        public PipeWireStream? Stream { get; set; }

        public MemoryBuffer? Staging { get; set; }

        public MemoryBuffer? CursorImage { get; set; }

        public Dictionary<nint, Slot> Slots { get; } = [];

        public bool Dirty { get; set; }

        public ulong Sequence { get; set; }

        public uint WrittenCursorSerial { get; set; }

        public void MarkDamaged(in Box damage)
        {
            Dirty = true;
            var whole = new Box(0, 0, Format.Width, Format.Height);
            var clipped = damage.IsEmpty ? whole : damage.Intersect(whole);
            if (clipped.IsEmpty)
            {
                clipped = whole;
            }

            _damage = _damage.IsEmpty ? clipped : Union(_damage, clipped);
        }

        public Box TakeDamage()
        {
            var taken = _damage;
            _damage = default;
            return taken;
        }

        private static Box Union(in Box a, in Box b)
        {
            var x = Math.Min(a.X, b.X);
            var y = Math.Min(a.Y, b.Y);
            var right = Math.Max(a.Right, b.Right);
            var bottom = Math.Max(a.Bottom, b.Bottom);
            return new Box(x, y, right - x, bottom - y);
        }

        public void Dispose()
        {
            if (Stream is { } stream)
            {
                try
                {
                    stream.Disconnect();
                }
                catch (PipeWireException)
                {
                }

                stream.Dispose();
                Stream = null;
            }

            foreach (var slot in Slots.Values)
            {
                slot.Dispose();
            }

            Slots.Clear();
            Staging?.Destroy();
            Staging = null;
            CursorImage?.Destroy();
            CursorImage = null;
        }
    }

    private const uint MfdCloexec = 1;
    private const uint MfdAllowSealing = 2;
    private const int ProtRead = 1;
    private const int ProtWrite = 2;
    private const int MapShared = 1;

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern void* mmap(void* address, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(void* address, nuint length);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);
}
