using Basin.Capabilities;
using Basin.Plasma.Protocol;
using Pixman;
using Wayland.Server;

namespace Basin.Plasma;

public sealed class ScreencastManager : IDisposable
{
    public const int Version = 6;

    private readonly WlGlobal _global;
    private readonly IScreencastPublisher? _publisher;
    private readonly IVirtualOutputFactory? _virtualOutputs;
    private readonly IToplevelModel? _toplevels;
    private readonly IScreenCapture? _capture;
    private readonly IOutputSet? _outputs;
    private readonly List<ScreencastStream> _streams = [];
    private readonly ToplevelWatch _watch;
    private ulong _nextStreamId;

    public ScreencastManager(
        WlServerDisplay display,
        IScreencastPublisher? publisher,
        IVirtualOutputFactory? virtualOutputs,
        IToplevelModel? toplevels,
        IScreenCapture? capture,
        IOutputSet? outputs)
    {
        ArgumentNullException.ThrowIfNull(display);
        _publisher = publisher;
        _virtualOutputs = virtualOutputs;
        _toplevels = toplevels;
        _capture = capture;
        _outputs = outputs;
        _global = display.CreateGlobal(ZkdeScreencastUnstableV1.Interface, Version, OnBind);
        _watch = new ToplevelWatch(this);
        _toplevels?.AddObserver(_watch);
        if (_outputs is { } set)
        {
            set.Changed += OnOutputsChanged;
        }
    }

    public void Dispose()
    {
        if (_outputs is { } set)
        {
            set.Changed -= OnOutputsChanged;
        }

        _toplevels?.RemoveObserver(_watch);
        _global.Dispose();
    }

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new ZkdeScreencastUnstableV1Resource(client, version, id);
        manager.StreamOutput += (_, e) =>
        {
            var stream = NewStream(client, manager.Version, e.Stream, e.Pointer);
            StartOutput(stream, OutputGlobal.FromResource(e.Output)?.Output);
        };
        manager.StreamWindow += (_, e) =>
        {
            var stream = NewStream(client, manager.Version, e.Stream, e.Pointer);
            StartWindow(stream, e.WindowUuid);
        };
        manager.StreamRegion += (_, e) =>
        {
            var stream = NewStream(client, manager.Version, e.Stream, e.Pointer);
            StartRegion(stream, new Box(e.X, e.Y, (int)e.Width, (int)e.Height), e.Scale.ToDouble());
        };
        manager.StreamVirtualOutput += (_, e) =>
        {
            var stream = NewStream(client, manager.Version, e.Stream, e.Pointer);
            StartVirtualOutput(stream, e.Name, string.Empty, e.Width, e.Height, e.Scale.ToDouble());
        };
        manager.StreamVirtualOutputWithDescription += (_, e) =>
        {
            var stream = NewStream(client, manager.Version, e.Stream, e.Pointer);
            StartVirtualOutput(stream, e.Name, e.Description, e.Width, e.Height, e.Scale.ToDouble());
        };
    }

    private void StartVirtualOutput(ScreencastStream stream, string name, string description,
        int width, int height, double scale)
    {
        if (_publisher is null)
        {
            Fail(stream, "screencasting is not available in this session");
            return;
        }

        if (_virtualOutputs is null)
        {
            Fail(stream, "virtual outputs are not available");
            return;
        }

        if (!_virtualOutputs.TryCreate(name, description, width, height, scale, out var output))
        {
            Fail(stream, "the virtual output could not be created");
            return;
        }

        stream.VirtualOutput = output;
        Publish(stream, CaptureSource.Output(output, OverlayCursor(stream)), output);
    }

    private void StartRegion(ScreencastStream stream, in Box layoutBox, double scale)
    {
        if (_publisher is null)
        {
            Fail(stream, "screencasting is not available in this session");
            return;
        }

        if (_capture is null)
        {
            Fail(stream, "capture is not available in this session");
            return;
        }

        var source = CaptureSource.Region(layoutBox, scale, OverlayCursor(stream));
        if (!_capture.Supports(source))
        {
            Fail(stream, "the region is not on any output");
            return;
        }

        Publish(stream, source, null);
    }

    private void OnOutputsChanged()
    {
        for (var i = _streams.Count - 1; i >= 0; i--)
        {
            var stream = _streams[i];
            if (stream.State == ScreencastStream.StreamState.Live &&
                stream.Source.Kind == CaptureSourceKind.Region &&
                (_capture is null || !_capture.Supports(stream.Source)))
            {
                CloseStream(stream);
            }
        }
    }

    private void StartWindow(ScreencastStream stream, string uuid)
    {
        if (_publisher is null)
        {
            Fail(stream, "screencasting is not available in this session");
            return;
        }

        if (!TryResolveWindow(uuid, out var toplevelId))
        {
            Fail(stream, "unknown window");
            return;
        }

        Publish(stream, CaptureSource.Toplevel(toplevelId), null);
    }

    private bool TryResolveWindow(string uuid, out ulong toplevelId)
    {
        toplevelId = 0;
        return _toplevels is { } model &&
            uuid.StartsWith("basin-", StringComparison.Ordinal) &&
            ulong.TryParse(uuid.AsSpan("basin-".Length), out toplevelId) &&
            model.TryGet(toplevelId, out _);
    }

    private void OnToplevelRemoved(ulong toplevelId)
    {
        for (var i = _streams.Count - 1; i >= 0; i--)
        {
            var stream = _streams[i];
            if (stream.State == ScreencastStream.StreamState.Live &&
                stream.Source.Kind == CaptureSourceKind.Toplevel &&
                stream.Source.ToplevelId == toplevelId)
            {
                CloseStream(stream);
            }
        }
    }

    private ScreencastStream NewStream(WlClient client, uint version, uint id, uint pointer)
    {
        var resource = new ZkdeScreencastStreamUnstableV1Resource(client, version, id);
        var stream = new ScreencastStream(resource, ++_nextStreamId, (ScreencastCursorMode)pointer);
        _streams.Add(stream);
        resource.Destroyed += (_, _) => OnStreamResourceDestroyed(stream);
        return stream;
    }

    private void StartOutput(ScreencastStream stream, IOutput? output)
    {
        if (_publisher is null)
        {
            Fail(stream, "screencasting is not available in this session");
            return;
        }

        if (output is null)
        {
            Fail(stream, "unknown output");
            return;
        }

        Publish(stream, CaptureSource.Output(output, OverlayCursor(stream)), output);
    }

    private bool OverlayCursor(ScreencastStream stream) =>
        (stream.Cursor & ScreencastCursorMode.Embedded) != 0;

    private void Publish(ScreencastStream stream, in CaptureSource source, IOutput? watched)
    {
        stream.Source = source;
        var request = new ScreencastRequest
        {
            StreamId = stream.Id,
            Source = source,
            Cursor = stream.Cursor,
        };

        if (!_publisher!.TryPublish(request, out var info))
        {
            Fail(stream, info.FailureReason ?? "the stream could not be started");
            return;
        }

        if (info.FailureReason is { } reason)
        {
            Fail(stream, reason);
            return;
        }

        stream.State = ScreencastStream.StreamState.Live;
        WatchOutput(stream, watched);
        if (info.ObjectSerial != 0 && stream.Resource.SupportsSendSerial)
        {
            stream.Resource.SendSerial((uint)(info.ObjectSerial >> 32), (uint)info.ObjectSerial);
        }

#pragma warning disable CS0618
        stream.Resource.SendCreated(info.NodeId);
#pragma warning restore CS0618
    }

    private void Fail(ScreencastStream stream, string message)
    {
        if (stream.State != ScreencastStream.StreamState.Pending)
        {
            return;
        }

        stream.State = ScreencastStream.StreamState.Failed;
        DestroyVirtual(stream);
        stream.Resource.SendFailed(message);
    }

    private void CloseStream(ScreencastStream stream)
    {
        if (stream.State != ScreencastStream.StreamState.Live)
        {
            return;
        }

        stream.State = ScreencastStream.StreamState.Closed;
        _publisher?.Close(stream.Id);
        UnwatchOutput(stream);
        DestroyVirtual(stream);
        if (!stream.Resource.IsDestroyed)
        {
            stream.Resource.SendClosed();
        }
    }

    private void OnStreamResourceDestroyed(ScreencastStream stream)
    {
        _streams.Remove(stream);
        UnwatchOutput(stream);
        if (stream.State == ScreencastStream.StreamState.Live)
        {
            stream.State = ScreencastStream.StreamState.Closed;
            _publisher?.Close(stream.Id);
        }

        DestroyVirtual(stream);
    }

    private void WatchOutput(ScreencastStream stream, IOutput? output)
    {
        if (output is null)
        {
            return;
        }

        Action handler = () => CloseStream(stream);
        stream.WatchedOutput = output;
        stream.OutputDestroyedHandler = handler;
        output.Destroyed += handler;
    }

    private void UnwatchOutput(ScreencastStream stream)
    {
        if (stream.WatchedOutput is { } output && stream.OutputDestroyedHandler is { } handler)
        {
            output.Destroyed -= handler;
        }

        stream.WatchedOutput = null;
        stream.OutputDestroyedHandler = null;
    }

    private void DestroyVirtual(ScreencastStream stream)
    {
        if (stream.VirtualOutput is { } output)
        {
            stream.VirtualOutput = null;
            _virtualOutputs?.Destroy(output);
        }
    }

    private sealed class ToplevelWatch(ScreencastManager owner) : IToplevelObserver
    {
        public void OnToplevelAdded(ulong toplevelId)
        {
        }

        public void OnToplevelChanged(ulong toplevelId)
        {
        }

        public void OnToplevelRemoved(ulong toplevelId) => owner.OnToplevelRemoved(toplevelId);
    }
}
