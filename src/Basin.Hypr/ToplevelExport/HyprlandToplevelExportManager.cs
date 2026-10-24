using Basin.Capabilities;
using Basin.Diagnostics;
using Basin.Desktop;
using Basin.Hypr.Protocol;
using Wayland;
using Wayland.Server;

namespace Basin.Hypr;

/// <summary>
/// Advertises <c>hyprland_toplevel_export_manager_v1</c>. It copies one toplevel into a client buffer through <see cref="IScreenCapture"/>. A frame without <c>ignore_damage</c> waits for damage that touches the toplevel.
/// </summary>
public sealed class HyprlandToplevelExportManager : ICaptureDamageObserver, IToplevelObserver, IDisposable, IHyprlandToplevelExportManagerV1Handler
{
    private static readonly BasinLogger Log = BasinLog.For("hyprland");

    /// <summary>
    /// The highest protocol version that the global advertises.
    /// </summary>
    public const int Version = 2;

    private const DrmFormat ExportFormat = DrmFormat.Xrgb8888;

    private readonly WlGlobal _global;
    private readonly OutputLayout _layout;
    private readonly ClientBufferRegistry _buffers;
    private readonly IScreenCapture? _capture;
    private readonly IToplevelModel? _toplevels;
    private readonly ICaptureDmabufConstraints? _dmabuf;
    private readonly List<Frame> _waiting = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="HyprlandToplevelExportManager"/> class and subscribes to the capture damage and the toplevel model.
    /// </summary>
    /// <param name="display">The display that hosts the global.</param>
    /// <param name="layout">The layout that maps output damage into layout coordinates.</param>
    /// <param name="buffers">The registry that imports the client buffers.</param>
    /// <param name="capture">The capture that renders the toplevel. When it is <see langword="null"/>, every frame fails.</param>
    /// <param name="toplevels">The model that resolves the handles. When it is <see langword="null"/>, every frame fails.</param>
    /// <param name="dmabuf">The constraints for dmabuf targets. When it is <see langword="null"/>, the frame offers only shm and refuses a dmabuf buffer.</param>
    /// <exception cref="ArgumentNullException"><paramref name="display"/>, <paramref name="layout"/> or <paramref name="buffers"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Gets the number of frames that have a buffer and wait for damage.
    /// </summary>
    public int WaitingFrames => _waiting.Count;

    /// <summary>
    /// Unsubscribes from the toplevel model and the capture and destroys the global. Waiting frames get no event.
    /// </summary>
    public void Dispose()
    {
        _toplevels?.RemoveObserver(this);
        _capture?.RemoveDamageObserver(this);
        _global.Dispose();
    }

    /// <summary>
    /// Completes each waiting frame whose toplevel geometry intersects the damage. The frame reports the intersection as its damage.
    /// </summary>
    /// <param name="output">The output that changed.</param>
    /// <param name="damage">The changed area in output buffer pixels.</param>
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

    /// <summary>
    /// Completes each waiting frame that asked for the cursor, with damage over the whole frame.
    /// </summary>
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

    /// <summary>
    /// Does nothing. A new toplevel has no waiting frame.
    /// </summary>
    /// <param name="toplevelId">The id of the new toplevel. It is ignored.</param>
    public void OnToplevelAdded(ulong toplevelId)
    {
    }

    /// <summary>
    /// Does nothing. A waiting frame keeps the geometry that it had when it started.
    /// </summary>
    /// <param name="toplevelId">The id of the changed toplevel. It is ignored.</param>
    public void OnToplevelChanged(ulong toplevelId)
    {
    }

    /// <summary>
    /// Sends <c>failed</c> to each waiting frame of the removed toplevel.
    /// </summary>
    /// <param name="toplevelId">The id of the removed toplevel.</param>
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
        _ = new HyprlandToplevelExportManagerV1Resource(client, version, id, this);
    }

    void IHyprlandToplevelExportManagerV1Handler.CaptureToplevel(
        HyprlandToplevelExportManagerV1Resource resource,
        in HyprlandToplevelExportManagerV1Resource.CaptureToplevelEventArgs args) =>
        Start(resource, args.Frame, ResolveHandle(_toplevels, args.Handle), args.OverlayCursor != 0);

    void IHyprlandToplevelExportManagerV1Handler.CaptureToplevelWithWlrToplevelHandle(
        HyprlandToplevelExportManagerV1Resource resource,
        in HyprlandToplevelExportManagerV1Resource.CaptureToplevelWithWlrToplevelHandleEventArgs args) =>
        Start(resource, args.Frame, ForeignToplevelManager.ToplevelOf(args.HandleHandle), args.OverlayCursor != 0);

    void IHyprlandToplevelExportManagerV1Handler.Destroy(HyprlandToplevelExportManagerV1Resource resource)
    {
    }

    /// <summary>
    /// Converts the 32-bit window handle of the protocol into a toplevel id. It first tries the handle as an id, then matches the low 32 bits of every id.
    /// </summary>
    /// <param name="toplevels">The model to search.</param>
    /// <param name="handle">The handle that the client sent.</param>
    /// <returns>The toplevel id, or 0 when the model is <see langword="null"/>, no toplevel matches, or more than one toplevel matches.</returns>
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

    private void Start(HyprlandToplevelExportManagerV1Resource manager, uint frameId, ulong toplevelId, bool overlayCursor)
    {
        var source = CaptureSource.Toplevel(toplevelId, clientOnly: true, overlayCursor: overlayCursor);
        if (toplevelId == 0 || _capture is not { } capture || _toplevels is not { } toplevels ||
            !toplevels.TryGet(toplevelId, out var info) || !capture.Supports(source) ||
            !capture.TryDescribe(source, out var format) || format.Width <= 0 || format.Height <= 0)
        {
            new HyprlandToplevelExportFrameV1Resource(manager.Client, manager.Version, frameId).SendFailed();
            return;
        }

        _ = new Frame(this, manager.Client, manager.Version, frameId, source, info.Geometry, format);
    }

    private sealed class Frame : IHyprlandToplevelExportFrameV1Handler
    {
        private readonly HyprlandToplevelExportManager _owner;
        private readonly HyprlandToplevelExportFrameV1Resource _resource;
        private readonly CaptureSource _source;
        private readonly CaptureFormat _format;
        private IBuffer? _target;
        private bool _used;

        public Frame(
            HyprlandToplevelExportManager owner,
            WlClient client,
            uint version,
            uint id,
            in CaptureSource source,
            in Box box,
            in CaptureFormat format)
        {
            _owner = owner;
            _resource = new HyprlandToplevelExportFrameV1Resource(client, version, id, this);
            _source = source;
            _format = format;
            Box = box;

            _resource.SendBuffer(WlShm.Format.Xrgb8888, (uint)format.Width, (uint)format.Height, (uint)format.Stride);
            if (owner.OffersDmabuf)
            {
                _resource.SendLinuxDmabuf((uint)ExportFormat, (uint)format.Width, (uint)format.Height);
            }

            _resource.SendBufferDone();

            _resource.Destroyed += (_, _) => _owner._waiting.Remove(this);
        }

        void IHyprlandToplevelExportFrameV1Handler.Copy(HyprlandToplevelExportFrameV1Resource resource, in HyprlandToplevelExportFrameV1Resource.CopyEventArgs args) =>
            OnCopy(args.BufferHandle, args.IgnoreDamage != 0);

        void IHyprlandToplevelExportFrameV1Handler.Destroy(HyprlandToplevelExportFrameV1Resource resource)
        {
        }

        public Box Box { get; }

        public ulong ToplevelId => _source.ToplevelId;

        public bool OverlayCursor => _source.OverlayCursor;

        private void OnCopy(nint bufferHandle, bool ignoreDamage)
        {
            if (_used)
            {
                _resource.PostError((uint)HyprlandToplevelExportFrameV1.Error.AlreadyUsed, "The frame already copied a buffer. Use a new frame for each copy.");
                return;
            }

            _used = true;
            var buffer = _owner._buffers.GetOrImport(bufferHandle);
            if (buffer is null || buffer.Width != _format.Width || buffer.Height != _format.Height ||
                !Matches(buffer) || !_owner.Accepts(buffer))
            {
                _resource.PostError((uint)HyprlandToplevelExportFrameV1.Error.InvalidBuffer, "The buffer attributes do not match the announced parameters.");
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

            if (_target is not { } target || _owner._capture is not { } capture)
            {
                _resource.SendFailed();
                return;
            }

            bool captured;
            try
            {
                captured = capture.Capture(_source, new Box(0, 0, _format.Width, _format.Height), target);
            }
            catch (InvalidOperationException e)
            {
                Log.Debug(e, $"toplevel-export: frame refused the client buffer");
                captured = false;
            }

            if (!captured)
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
