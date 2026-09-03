using Basin.Capabilities;
using Basin.Desktop;
using Basin.Hypr.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Hypr;

public sealed class HyprlandToplevelExportManager : ICaptureDamageObserver, IToplevelObserver, IDisposable
{
    public const int Version = 2;

    private const DrmFormat ExportFormat = DrmFormat.Xrgb8888;

    private readonly WlGlobal _global;
    private readonly OutputLayout _layout;
    private readonly ClientBufferRegistry _buffers;
    private readonly IScreenCapture? _capture;
    private readonly IToplevelModel? _toplevels;
    private readonly ICaptureDmabufConstraints? _dmabuf;
    private readonly List<Frame> _waiting = [];

    public HyprlandToplevelExportManager(
        WlServerDisplay display,
        OutputLayout layout,
        ClientBufferRegistry buffers,
        IScreenCapture? capture,
        IToplevelModel? toplevels,
        ICaptureDmabufConstraints? dmabuf = null)
    {
        ArgumentNullException.ThrowIfNull(display);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(buffers);
        _layout = layout;
        _buffers = buffers;
        _capture = capture;
        _toplevels = toplevels;
        _dmabuf = dmabuf;
        _global = display.CreateGlobal(HyprlandToplevelExportManagerV1.Interface, Version, OnBind);
        _capture?.AddDamageObserver(this);
        _toplevels?.AddObserver(this);
    }

    public int WaitingFrames => _waiting.Count;

    public void Dispose()
    {
        _toplevels?.RemoveObserver(this);
        _capture?.RemoveDamageObserver(this);
        _global.Dispose();
    }

    public void OnSourceDamaged(IOutput output, Box damage)
    {
        var outputBox = _layout.BoxOf(output);
        var scale = output.Scale;
        var layoutDamage = new Box(
            outputBox.X + (int)Math.Floor(damage.X / scale),
            outputBox.Y + (int)Math.Floor(damage.Y / scale),
            (int)Math.Ceiling(damage.Width / scale),
            (int)Math.Ceiling(damage.Height / scale));

        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            var frame = _waiting[i];
            var hit = frame.Box.Intersect(layoutDamage);
            if (hit.IsEmpty)
            {
                continue;
            }

            _waiting.RemoveAt(i);
            frame.Complete(hit.Translated(-frame.Box.X, -frame.Box.Y));
        }
    }

    public void OnCursorChanged()
    {
        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            var frame = _waiting[i];
            if (frame.OverlayCursor)
            {
                _waiting.RemoveAt(i);
                frame.Complete(new Box(0, 0, frame.Box.Width, frame.Box.Height));
            }
        }
    }

    public void OnToplevelAdded(ulong toplevelId)
    {
    }

    public void OnToplevelChanged(ulong toplevelId)
    {
    }

    public void OnToplevelRemoved(ulong toplevelId)
    {
        for (var i = _waiting.Count - 1; i >= 0; i--)
        {
            var frame = _waiting[i];
            if (frame.ToplevelId == toplevelId)
            {
                _waiting.RemoveAt(i);
                frame.Fail();
            }
        }
    }

    private bool OffersDmabuf =>
        _capture is not null &&
        _dmabuf is { } constraints &&
        constraints.TryDevice(out _) &&
        constraints.Formats.Contains(ExportFormat);

    private bool Accepts(IBuffer target) =>
        !target.TryGetDmabuf(out var attributes) ||
        (_dmabuf is { } constraints && constraints.Formats.Contains(attributes.Format, attributes.Modifier));

    private void OnBind(WlClient client, uint version, uint id)
    {
        var manager = new HyprlandToplevelExportManagerV1Resource(client, version, id);
        manager.CaptureToplevel += (_, e) =>
        {
            var frame = new HyprlandToplevelExportFrameV1Resource(client, manager.Version, e.Frame);
            Start(frame, ResolveHandle(_toplevels, e.Handle), e.OverlayCursor != 0);
        };
        manager.CaptureToplevelWithWlrToplevelHandle += (_, e) =>
        {
            var frame = new HyprlandToplevelExportFrameV1Resource(client, manager.Version, e.Frame);
            Start(frame, ForeignToplevelManager.ToplevelOf(e.HandleHandle), e.OverlayCursor != 0);
        };
    }

    public static ulong ResolveHandle(IToplevelModel? toplevels, uint handle)
    {
        if (toplevels is null || handle == 0)
        {
            return 0;
        }

        if (toplevels.TryGet(handle, out _))
        {
            return handle;
        }

        var scratch = new ToplevelInfo[16];
        var count = toplevels.Enumerate(scratch);
        while (count < 0)
        {
            scratch = new ToplevelInfo[scratch.Length * 2];
            count = toplevels.Enumerate(scratch);
        }

        ulong resolved = 0;
        for (var i = 0; i < count; i++)
        {
            if ((uint)scratch[i].Id != handle)
            {
                continue;
            }

            if (resolved != 0)
            {
                return 0;
            }

            resolved = scratch[i].Id;
        }

        return resolved;
    }

    private void Start(HyprlandToplevelExportFrameV1Resource resource, ulong toplevelId, bool overlayCursor)
    {
        var source = CaptureSource.Toplevel(toplevelId, clientOnly: true, overlayCursor: overlayCursor);
        if (toplevelId == 0 || _capture is not { } capture || _toplevels is not { } toplevels ||
            !toplevels.TryGet(toplevelId, out var info) || !capture.Supports(source) ||
            !capture.TryDescribe(source, out var format) || format.Width <= 0 || format.Height <= 0)
        {
            resource.SendFailed();
            return;
        }

        _ = new Frame(this, resource, source, info.Geometry, format);
    }

    private sealed class Frame
    {
        private readonly HyprlandToplevelExportManager _owner;
        private readonly HyprlandToplevelExportFrameV1Resource _resource;
        private readonly CaptureSource _source;
        private readonly CaptureFormat _format;
        private IBuffer? _target;
        private bool _used;

        public Frame(
            HyprlandToplevelExportManager owner,
            HyprlandToplevelExportFrameV1Resource resource,
            in CaptureSource source,
            in Box box,
            in CaptureFormat format)
        {
            _owner = owner;
            _resource = resource;
            _source = source;
            _format = format;
            Box = box;

            resource.SendBuffer(WlShm.Format.Xrgb8888, (uint)format.Width, (uint)format.Height, (uint)format.Stride);
            if (owner.OffersDmabuf)
            {
                resource.SendLinuxDmabuf((uint)ExportFormat, (uint)format.Width, (uint)format.Height);
            }

            resource.SendBufferDone();

            resource.Copy += (_, e) => OnCopy(e.BufferHandle, e.IgnoreDamage != 0);
            resource.Destroyed += (_, _) => _owner._waiting.Remove(this);
        }

        public Box Box { get; }

        public ulong ToplevelId => _source.ToplevelId;

        public bool OverlayCursor => _source.OverlayCursor;

        private void OnCopy(nint bufferHandle, bool ignoreDamage)
        {
            if (_used)
            {
                _resource.PostError((uint)HyprlandToplevelExportFrameV1.Error.AlreadyUsed, "the frame has already been used to copy a buffer");
                return;
            }

            _used = true;
            var buffer = _owner._buffers.GetOrImport(bufferHandle);
            if (buffer is null || buffer.Width != _format.Width || buffer.Height != _format.Height ||
                !Matches(buffer) || !_owner.Accepts(buffer))
            {
                _resource.PostError((uint)HyprlandToplevelExportFrameV1.Error.InvalidBuffer, "buffer attributes do not match the announced parameters");
                return;
            }

            _target = buffer;
            if (ignoreDamage)
            {
                Complete(null);
            }
            else
            {
                _owner._waiting.Add(this);
            }
        }

        private bool Matches(IBuffer buffer)
        {
            if (buffer.TryGetDmabuf(out _))
            {
                return true;
            }

            return buffer.Format == ExportFormat;
        }

        public void Fail()
        {
            if (!_resource.IsDestroyed)
            {
                _resource.SendFailed();
            }
        }

        public void Complete(Box? damage)
        {
            if (_resource.IsDestroyed)
            {
                return;
            }

            if (_target is not { } target || _owner._capture is not { } capture ||
                !capture.Capture(_source, new Box(0, 0, _format.Width, _format.Height), target))
            {
                _resource.SendFailed();
                return;
            }

            _resource.SendFlags(0);
            if (damage is { } box)
            {
                var physical = OutputScaling.ToPhysical(box, _format.Width / (double)Math.Max(1, Box.Width));
                var x = Math.Clamp(physical.X, 0, _format.Width);
                var y = Math.Clamp(physical.Y, 0, _format.Height);
                _resource.SendDamage(
                    (uint)x,
                    (uint)y,
                    (uint)Math.Clamp(physical.Width, 0, _format.Width - x),
                    (uint)Math.Clamp(physical.Height, 0, _format.Height - y));
            }

            var nanos = MonotonicClock.Nanos;
            var seconds = (ulong)(nanos / 1_000_000_000);
            _resource.SendReady((uint)(seconds >> 32), (uint)seconds, (uint)(nanos % 1_000_000_000));
        }
    }
}
