using System.Runtime.Versioning;
using Basin;
using Basin.Capabilities;
using Microsoft.Extensions.Logging;
using PipeWire;
using PipeWire.Native;
using PipeWire.Spa;

namespace PlasmaHost;

[SupportedOSPlatform("linux")]
internal sealed class PlasmaHostScreencast : IScreencastPublisher, ICaptureDamageObserver, IDisposable
{
    private readonly ICompositorEventLoop _loop;
    private readonly IScreenCapture _capture;
    private readonly OutputLayout _layout;
    private readonly ILogger _log;
    private readonly PipeWireLoop _pwLoop;
    private readonly PipeWireContext _context;
    private readonly PipeWireCore _core;
    private readonly IEventSource _fdSource;
    private readonly Dictionary<ulong, Entry> _streams = [];
    private bool _flushScheduled;

    private PlasmaHostScreencast(
        ICompositorEventLoop loop,
        IScreenCapture capture,
        OutputLayout layout,
        ILogger log,
        PipeWireLoop pwLoop,
        PipeWireContext context,
        PipeWireCore core)
    {
        _loop = loop;
        _capture = capture;
        _layout = layout;
        _log = log;
        _pwLoop = pwLoop;
        _context = context;
        _core = core;
        _fdSource = loop.AddFd(pwLoop.Fd, FdReadiness.Readable, (_, _) => Pump(0));
    }

    internal static PlasmaHostScreencast? TryCreate(
        ICompositorEventLoop loop, IScreenCapture capture, OutputLayout layout, ILogger log)
    {
        PipeWireLoop? pwLoop = null;
        PipeWireContext? context = null;
        try
        {
            PipeWireLibrary.Init();
            pwLoop = PipeWireLoop.Create();
            context = new PipeWireContext(pwLoop);
            var core = context.Connect();
            return new PlasmaHostScreencast(loop, capture, layout, log, pwLoop, context, core);
        }
        catch (Exception error) when (error is DllNotFoundException or PipeWireException)
        {
            context?.Dispose();
            pwLoop?.Dispose();
            log.LogWarning("screencast unavailable: {Reason}", error.Message);
            return null;
        }
    }

    public bool TryPublish(in ScreencastRequest request, out ScreencastStreamInfo info)
    {
        if (!_capture.TryDescribe(request.Source, out var format))
        {
            info = new ScreencastStreamInfo { NodeId = 0, FailureReason = "the source cannot be captured" };
            return false;
        }

        var entry = new Entry(request.Source, request.Cursor, format);
        using var properties = PipeWireProperties.From(
            "media.class", "Video/Source",
            "media.name", "basin-screencast",
            "node.name", "basin-screencast");
        entry.Stream = new PipeWireStream(_core, "basin-screencast", properties);
        entry.Stream.Process = _ => OnProcess(entry);
        entry.Stream.ParamChanged += (_, e) => OnParamChanged(entry, e);
        entry.Stream.StateChanged += (_, _) => OnStateChanged(entry);
        entry.Stream.Connect(
            spa_direction.SPA_DIRECTION_OUTPUT,
            uint.MaxValue,
            pw_stream_flags.PW_STREAM_FLAG_DRIVER | pw_stream_flags.PW_STREAM_FLAG_MAP_BUFFERS,
            SpaVideoFormats.BuildRaw(
                spa_video_format.SPA_VIDEO_FORMAT_BGRx,
                ((uint)format.Width, (uint)format.Height),
                ((uint)RefreshOf(request.Source), 1000u)));

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
        info = new ScreencastStreamInfo
        {
            NodeId = entry.Stream.NodeId,
            ObjectSerial = entry.Stream.ObjectSerial,
        };
        _log.LogInformation(
            "screencast stream {Id}: node {Node} serial {Serial}", request.StreamId, info.NodeId, info.ObjectSerial);
        return true;
    }

    public void Close(ulong streamId)
    {
        if (_streams.Remove(streamId, out var entry))
        {
            entry.Dispose();
        }
    }

    public void OnSourceDamaged(IOutput output, Box damage)
    {
        foreach (var entry in _streams.Values)
        {
            if (Intersects(entry.Source, output))
            {
                entry.Dirty = true;
            }
        }

        ScheduleFlush();
    }

    public void OnCursorChanged()
    {
        foreach (var entry in _streams.Values)
        {
            if ((entry.Cursor & ScreencastCursorMode.Embedded) != 0)
            {
                entry.Dirty = true;
            }
        }

        ScheduleFlush();
    }

    public void Dispose()
    {
        foreach (var entry in _streams.Values)
        {
            entry.Dispose();
        }

        _streams.Clear();
        _fdSource.Remove();
        _core.Dispose();
        _context.Dispose();
        _pwLoop.Dispose();
    }

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
            return output.CurrentMode.RefreshMilliHz;
        }

        var refresh = 60_000;
        foreach (var (candidate, _) in _layout.Outputs)
        {
            if (source.Kind != CaptureSourceKind.Region ||
                !_layout.BoxOf(candidate).Intersect(source.LayoutBox).IsEmpty)
            {
                refresh = Math.Max(refresh, candidate.CurrentMode.RefreshMilliHz);
            }
        }

        return refresh;
    }

    private void ScheduleFlush()
    {
        if (_flushScheduled)
        {
            return;
        }

        _flushScheduled = true;
        _loop.AddIdle(() =>
        {
            _flushScheduled = false;
            foreach (var entry in _streams.Values)
            {
                if (entry.Dirty && entry.Stream is { } stream &&
                    stream.State == pw_stream_state.PW_STREAM_STATE_STREAMING)
                {
                    entry.Dirty = false;
                    stream.TriggerProcess();
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

        stream.UpdateParams(SpaBufferParams.Build(
            buffers: 3,
            blocks: 1,
            size: entry.Format.Stride * entry.Format.Height,
            stride: entry.Format.Stride,
            dataTypes: SpaBufferParams.DataTypeMask(spa_data_type.SPA_DATA_MemFd)));
    }

    private void OnStateChanged(Entry entry)
    {
        if (entry.Stream is { State: pw_stream_state.PW_STREAM_STATE_STREAMING })
        {
            entry.Dirty = true;
            ScheduleFlush();
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

        try
        {
            var data = buffer[0];
            if (!data.HasMemory || !Render(entry, data))
            {
                data.SetChunk(0, 0, entry.Format.Stride);
            }
        }
        finally
        {
            stream.QueueBuffer(buffer);
        }
    }

    private unsafe bool Render(Entry entry, PipeWireBufferData data)
    {
        var target = entry.Target ??= new MemoryBuffer(entry.Format.Width, entry.Format.Height, DrmFormat.Xrgb8888);
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

    private sealed class Entry(CaptureSource source, ScreencastCursorMode cursor, CaptureFormat format) : IDisposable
    {
        public CaptureSource Source { get; } = source;

        public ScreencastCursorMode Cursor { get; } = cursor;

        public CaptureFormat Format { get; } = format;

        public PipeWireStream? Stream { get; set; }

        public MemoryBuffer? Target { get; set; }

        public bool Dirty { get; set; }

        public void Dispose()
        {
            Stream?.Dispose();
            Stream = null;
            Target?.Destroy();
            Target = null;
        }
    }
}
