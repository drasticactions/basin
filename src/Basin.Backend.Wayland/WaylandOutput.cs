using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Wayland;

namespace Basin.Backend.Wayland;

public sealed class WaylandOutput : OutputBase, IPresentingOutput
{
    private const int ProtReadWrite = 3;
    private const int MapShared = 1;

    private readonly WaylandBackend _backend;
    private readonly WlSurface _surface;
    private readonly XdgSurface _xdgSurface;
    private readonly XdgToplevel _toplevel;
    private readonly Slot?[] _slots = new Slot?[2];
    private readonly Dictionary<IBuffer, ImportedBuffer> _imported = [];
    private EventHandler<WlCallback.DoneEventArgs>? _onFrameDone;
    private EventHandler<WpPresentationFeedback.PresentedEventArgs>? _onPresented;
    private EventHandler<WpPresentationFeedback.DiscardedEventArgs>? _onDiscarded;
    private nint _mapping;
    private int _mappingSize;
    private int _mappingFd = -1;
    private WlShmPool? _pool;
    private readonly IEventSource _frameFallback;
    private readonly WpViewport? _viewport;
    private readonly WpFractionalScaleV1? _fractionalScale;
    private readonly ZxdgToplevelDecorationV1? _decoration;
    private ZwpLockedPointerV1? _lockedPointer;
    private WlRegion? _lockRegion;
    private WpLinuxDrmSyncobjSurfaceV1? _syncobjSurface;
    private DrmSyncobjTimeline? _acquireTimeline;
    private WpLinuxDrmSyncobjTimelineV1? _acquireProxy;
    private ulong _acquirePoint;
    private bool _syncobjWarned;
    private bool _contentCommitted;
    private bool _geometryPending;
    private bool _framePending;
    private bool _frameRequested;
    private bool _configured;
    private int _logicalWidth;
    private int _logicalHeight;
    private double _presentationScale = 1;
    private bool _scaleWarned;

    private int _configureWidth;
    private int _configureHeight;
    private WaylandHostFrame? _hostFrame;
    private Action<WaylandHostFrame>? _hostFrameAvailable;
    private bool _hostFrameWarned;

    private const int MinContentWidth = 128;
    private const int MinContentHeight = 128;

    private const int FrameFallbackMillis = 50;

    [DllImport("libc", SetLastError = true)]
    private static extern int memfd_create(string name, uint flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int ftruncate(int fd, long length);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

    internal WaylandOutput(WaylandBackend backend, string name)
        : base(name)
    {
        _backend = backend;
        _frameFallback = backend.Loop.AddTimer(OnFrameFallback);
        Make = "basin";
        Model = "wayland";
        Description = $"parent compositor window {name}";

        _surface = backend.ParentCompositor.CreateSurface();
        _xdgSurface = backend.ParentWmBase.GetXdgSurface(_surface);
        _toplevel = _xdgSurface.GetToplevel();
        SetTitle($"basin — {name}");
        _toplevel.SetAppId("dev.basin.compositor");
        _decoration = backend.ParentDecorations?.GetToplevelDecoration(_toplevel);
        _decoration?.SetMode(ZxdgToplevelDecorationV1.Mode.ServerSide);
        _viewport = backend.ParentViewporter?.GetViewport(_surface);
        _fractionalScale = backend.ParentFractionalScale?.GetFractionalScale(_surface);
        if (_fractionalScale is not null)
        {
            _fractionalScale.PreferredScale += (_, e) =>
            {
                var scale = e.Scale / 120.0;
                if (scale != HostScale)
                {
                    HostScale = scale;
                    HostScaleChanged?.Invoke();
                }
            };
        }

        var pendingWidth = 0;
        var pendingHeight = 0;
        var pendingDecorated = false;
        var pendingStates = Array.Empty<byte>();
        _toplevel.Configure += (_, e) =>
        {
            pendingWidth = e.Width;
            pendingHeight = e.Height;
            pendingStates = e.States;
        };
        if (_decoration is not null)
        {
            var declineLogged = false;
            _decoration.Configure += (_, e) =>
            {
                pendingDecorated = e.Mode == ZxdgToplevelDecorationV1.Mode.ServerSide;
                if (!pendingDecorated && !declineLogged)
                {
                    declineLogged = true;
                    Basin.Diagnostics.BasinLog.Info(
                        $"{Name}: parent kept client-side decorations; the window is framed only if the consumer draws one");
                }
            };
        }

        _xdgSurface.Configure += (_, e) =>
        {
            _xdgSurface.AckConfigure(e.Serial);
            Decorated = pendingDecorated;

            _configureWidth = pendingWidth;
            _configureHeight = pendingHeight;
            var first = !_configured;
            _configured = true;
            EnsureHostFrame();

            ApplyToplevelStates(pendingStates);
            ApplyConfigure();
            if (first)
            {
                RequestFrame();
            }
        };
        _toplevel.Close += (_, _) => CloseRequested?.Invoke();

        _surface.Commit();
    }

    public void SetTitle(string title)
    {
        ArgumentNullException.ThrowIfNull(title);
        _toplevel.SetTitle(title);
    }

    public event Action? CloseRequested;

    public event Action<ulong, uint, ulong>? PresentedOnScreen;

    public event Action? PresentationDiscarded;

    public double HostScale { get; private set; } = 1;

    public event Action? HostScaleChanged;

    public bool Decorated { get; private set; }

    public WaylandHostFrame? HostFrame => _hostFrame;

    public event Action<WaylandHostFrame> HostFrameAvailable
    {
        add
        {
            _hostFrameAvailable += value;
            if (_hostFrame is not null)
            {
                value(_hostFrame);
            }
        }

        remove => _hostFrameAvailable -= value;
    }

    public bool RequestActivation()
    {
        if (_backend.ParentActivation is not { } activation)
        {
            return false;
        }

        var token = activation.GetActivationToken();
        token.SetSurface(_surface);
        if (_backend.ParentSeat is { } seat && _backend.LatestInputSerial is var serial and > 0)
        {
            token.SetSerial(serial, seat);
        }

        token.Done += (_, e) =>
        {
            activation.Activate(e.Token, _surface);
            WaylandBackend.DisposeParent(token);
            _backend.Flush();
        };
        token.Commit();
        _backend.Flush();
        return true;
    }

    public bool LockPointer(bool locked, Region? confine = null)
    {
        if (_backend.ParentPointerConstraints is not { } constraints || _backend.Pointer is not { } pointer)
        {
            return false;
        }

        if (!locked)
        {
            ReleasePointerLock();
            return true;
        }

        if (_lockedPointer is { IsDestroyed: false })
        {
            return true;
        }

        _lockRegion = BuildLockRegion(confine);
        _lockedPointer = constraints.LockPointer(
            _surface, pointer.Proxy, _lockRegion, ZwpPointerConstraintsV1.Lifetime.Persistent);
        _backend.Flush();
        return true;
    }

    public void SetCursorPositionHint(double x, double y)
    {
        if (_lockedPointer is not { IsDestroyed: false } locked)
        {
            return;
        }

        var factor = _presentationScale <= 0 ? 1 : _presentationScale;
        locked.SetCursorPositionHint(WlFixed.FromDouble(x / factor), WlFixed.FromDouble(y / factor));
        ApplyParentState();
    }

    private WlRegion? BuildLockRegion(Region? confine)
    {
        if (confine is null)
        {
            return null;
        }

        var factor = _presentationScale <= 0 ? 1 : _presentationScale;
        var region = _backend.ParentCompositor.CreateRegion();
        foreach (var rect in RegionRects.Of(confine.Pixman))
        {
            region.Add(
                (int)Math.Floor(rect.X1 / factor),
                (int)Math.Floor(rect.Y1 / factor),
                (int)Math.Ceiling((rect.X2 - rect.X1) / factor),
                (int)Math.Ceiling((rect.Y2 - rect.Y1) / factor));
        }

        return region;
    }

    private void ReleasePointerLock()
    {
        if (_lockedPointer is { IsDestroyed: false } locked)
        {
            locked.Dispose();
        }

        _lockedPointer = null;
        if (_lockRegion is { IsDestroyed: false } region)
        {
            region.Dispose();
        }

        _lockRegion = null;
        _backend.Flush();
    }

    internal WlSurface ParentSurface => _surface;

    internal XdgToplevel ParentToplevel => _toplevel;

    internal int ContentLogicalWidth => _logicalWidth;

    internal int ContentLogicalHeight => _logicalHeight;

    internal void ApplyHostFrameInsets()
    {
        if (_configured)
        {
            ApplyConfigure();
        }
    }

    internal double SurfaceToPhysical => _presentationScale;

    private OutputMode PhysicalMode() => new(
        (int)Math.Round(_logicalWidth * Scale),
        (int)Math.Round(_logicalHeight * Scale),
        60_000);

    private void EnsureHostFrame()
    {
        if (_hostFrame is not null || Decorated)
        {
            return;
        }

        if (_backend.ParentSubcompositor is not { } subcompositor || _backend.ParentViewporter is null)
        {
            if (!_hostFrameWarned)
            {
                _hostFrameWarned = true;
                var missing = _backend.ParentSubcompositor is null ? "wl_subcompositor" : "wp_viewporter";
                Basin.Diagnostics.BasinLog.Info(
                    $"{Name}: parent draws no decorations and lacks {missing}; the window stays bare");
            }

            return;
        }

        _hostFrame = new WaylandHostFrame(_backend, this, _surface, subcompositor);
        _hostFrameAvailable?.Invoke(_hostFrame);
    }

    private void ApplyToplevelStates(byte[] states)
    {
        if (_hostFrame is null)
        {
            return;
        }

        var maximized = false;
        var fullscreen = false;
        var resizing = false;
        var activated = false;
        foreach (var value in MemoryMarshal.Cast<byte, uint>(states))
        {
            switch ((XdgToplevel.State)value)
            {
                case XdgToplevel.State.Maximized:
                    maximized = true;
                    break;
                case XdgToplevel.State.Fullscreen:
                    fullscreen = true;
                    break;
                case XdgToplevel.State.Resizing:
                    resizing = true;
                    break;
                case XdgToplevel.State.Activated:
                    activated = true;
                    break;
            }
        }

        if (maximized == _hostFrame.Maximized &&
            fullscreen == _hostFrame.Fullscreen &&
            resizing == _hostFrame.Resizing &&
            activated == _hostFrame.Activated)
        {
            return;
        }

        _hostFrame.Maximized = maximized;
        _hostFrame.Fullscreen = fullscreen;
        _hostFrame.Resizing = resizing;
        _hostFrame.Activated = activated;
        _hostFrame.RaiseStateChanged();
    }

    private void ApplyConfigure()
    {
        var insets = _hostFrame?.Insets ?? default;
        var outerWidth = _configureWidth > 0 ? _configureWidth : 1024;
        var outerHeight = _configureHeight > 0 ? _configureHeight : 768;
        _logicalWidth = Math.Max(outerWidth - insets.Left - insets.Right, MinContentWidth);
        _logicalHeight = Math.Max(outerHeight - insets.Top - insets.Bottom, MinContentHeight);

        if (_hostFrame is not null)
        {
            _geometryPending = true;
            ApplyWindowGeometry();
        }

        var mode = PhysicalMode();
        if (mode != CurrentMode)
        {
            DestroySlots();
            ApplyPresentationScale();
            using var state = new OutputState();
            Commit(state.SetEnabled(true).SetMode(mode));
        }

        _hostFrame?.OnOutputResized();
        if (_hostFrame is not null)
        {
            ApplyParentState();
        }
    }

    internal void ApplyParentState()
    {
        if (_configured && _contentCommitted)
        {
            ApplyWindowGeometry();
            _surface.Commit();
            _backend.Flush();
        }
    }

    private void ApplyWindowGeometry()
    {
        if (!_geometryPending || !_contentCommitted || _hostFrame is not { HasContent: true } frame)
        {
            return;
        }

        var insets = frame.Insets;
        _geometryPending = false;
        _xdgSurface.SetWindowGeometry(
            -insets.Left,
            -insets.Top,
            _logicalWidth + insets.Left + insets.Right,
            _logicalHeight + insets.Top + insets.Bottom);
    }

    private void ApplyPresentationScale()
    {
        if (_viewport is not null)
        {
            if (Scale == 1)
            {
                _viewport.SetDestination(-1, -1);
            }
            else
            {
                _viewport.SetDestination(_logicalWidth, _logicalHeight);
            }

            _presentationScale = Scale;
            return;
        }

        if (Scale == Math.Floor(Scale))
        {
            _surface.SetBufferScale((int)Scale);
            _presentationScale = Scale;
            return;
        }

        if (!_scaleWarned)
        {
            _scaleWarned = true;
            Basin.Diagnostics.BasinLog.Warn(
                $"{Name}: parent lacks wp_viewporter; fractional scale {Scale} presents 1:1 (window grows)");
        }

        _presentationScale = 1;
    }

    public override void RequestFrame()
    {
        if (_framePending || _frameRequested || !_configured)
        {
            return;
        }

        _frameRequested = true;
        EmitFrame();
        _frameRequested = false;
    }

    public override bool SupportsInFence => _backend.ParentSyncobj is not null && _backend.RenderDevice is not null;

    protected override bool TestCommitCore(OutputState state)
    {
        if ((state.Fields & OutputStateFields.Mode) != 0 &&
            (state.Mode.Width <= 0 || state.Mode.Height <= 0))
        {
            return false;
        }

        if ((state.Fields & OutputStateFields.Buffer) != 0)
        {
            if (state.Buffer is null ||
                state.Buffer.Width != CurrentMode.Width ||
                state.Buffer.Height != CurrentMode.Height)
            {
                return false;
            }
        }

        return true;
    }

    protected override bool CommitCore(OutputState state)
    {
        if ((state.Fields & OutputStateFields.Scale) != 0 && _configured)
        {
            ApplyPresentationScale();
            var mode = PhysicalMode();
            if (mode != CurrentMode)
            {
                DestroySlots();
                using var modeState = new OutputState();
                Commit(modeState.SetMode(mode));
            }
        }

        if ((state.Fields & OutputStateFields.Buffer) == 0 || !_configured)
        {
            return true;
        }

        WlBuffer? proxy = null;
        ImportedBuffer? synchronized = null;
        var unhonored = (state.Fields & OutputStateFields.InFence) != 0 && state.InFenceFd >= 0;
        if (TryImportDmabuf(state.Buffer!) is { } imported)
        {
            if (!imported.Presented)
            {
                state.Buffer!.Lock();
                imported.Presented = true;
            }

            proxy = imported.Proxy;
            if (TrySynchronize(state.Buffer!, imported, state))
            {
                synchronized = imported;
                unhonored = false;
            }
        }
        else
        {
            if (unhonored)
            {
                RenderFences.WaitSyncFile(state.InFenceFd);
                unhonored = false;
            }

            if (_pool is null)
            {
                RebuildSlots(CurrentMode.Width, CurrentMode.Height);
            }

            var slot = FreeSlot();
            if (slot is null)
            {
                return false;
            }

            if (!CopyInto(slot, state.Buffer!))
            {
                return false;
            }

            slot.Busy = true;
            proxy = slot.Proxy;
        }

        if (synchronized is null)
        {
            DestroySyncobjSurface();
        }

        if (unhonored)
        {
            RenderFences.WaitSyncFile(state.InFenceFd);
        }

        ApplyWindowGeometry();
        var callback = _surface.Frame();
        callback.Done += _onFrameDone ??= (_, _) => OnFrameDone();
        if (_backend.ParentPresentation is { } presentation && _backend.ParentPresentationClockMatches)
        {
            var feedback = presentation.Feedback(_surface);
            feedback.Presented += _onPresented ??= (_, e) => OnPresented(e);
            feedback.Discarded += _onDiscarded ??= (_, _) => PresentationDiscarded?.Invoke();
        }

        _surface.Attach(proxy, 0, 0);
        _surface.Damage(0, 0, CurrentMode.Width, CurrentMode.Height);
        _surface.Commit();
        _contentCommitted = true;
        _framePending = true;
        _frameFallback.UpdateTimer(FrameFallbackMillis);
        _backend.Flush();
        return true;
    }

    private bool TrySynchronize(IBuffer buffer, ImportedBuffer imported, OutputState state)
    {
        if (_backend.ParentSyncobj is not { } manager ||
            _backend.RenderDevice is not { } device ||
            (state.Fields & OutputStateFields.InFence) == 0 ||
            state.InFenceFd < 0)
        {
            return false;
        }

        if ((_acquireTimeline is null && !TryCreateAcquireTimeline(manager, device)) ||
            (imported.ReleaseTimeline is null && !TryCreateReleaseTimeline(manager, device, imported)))
        {
            return false;
        }

        var acquire = ++_acquirePoint;
        if (!_acquireTimeline!.ImportSyncFileAt(acquire, state.InFenceFd))
        {
            WarnSyncobjOnce("the render's fence would not go onto a timeline point");
            return false;
        }

        var release = ++imported.ReleasePoint;
        imported.ReleaseWaiter?.Dispose();
        imported.ReleaseWaiter = imported.ReleaseTimeline!.TryWait(
            _backend.Loop, release, () => OnReleasePoint(buffer, imported));
        if (imported.ReleaseWaiter is null)
        {
            WarnSyncobjOnce("syncobj timeline points cannot be waited on (kernel or libdrm too old)");
            return false;
        }

        if (_syncobjSurface is null)
        {
            _syncobjSurface = manager.GetSurface(_surface);
            Basin.Diagnostics.BasinLog.Info(
                $"{Name}: presenting with explicit sync (linux-drm-syncobj-v1)");
        }

        _syncobjSurface.SetAcquirePoint(_acquireProxy!, (uint)(acquire >> 32), (uint)acquire);
        _syncobjSurface.SetReleasePoint(imported.ReleaseProxy!, (uint)(release >> 32), (uint)release);
        return true;
    }

    private bool TryCreateAcquireTimeline(WpLinuxDrmSyncobjManagerV1 manager, IRenderDevice device)
    {
        if (DrmSyncobjTimeline.TryCreate(device.DrmFd) is not { } timeline)
        {
            WarnSyncobjOnce($"{device.DevicePath} will not create a syncobj timeline");
            return false;
        }

        var fd = timeline.TryExportFd();
        if (fd < 0)
        {
            timeline.Release();
            WarnSyncobjOnce("a syncobj timeline would not export");
            return false;
        }

        _acquireProxy = manager.ImportTimeline(fd);
        close(fd);
        _acquireTimeline = timeline;
        return true;
    }

    private bool TryCreateReleaseTimeline(WpLinuxDrmSyncobjManagerV1 manager, IRenderDevice device, ImportedBuffer imported)
    {
        if (DrmSyncobjTimeline.TryCreate(device.DrmFd) is not { } timeline)
        {
            return false;
        }

        var fd = timeline.TryExportFd();
        if (fd < 0)
        {
            timeline.Release();
            return false;
        }

        imported.ReleaseProxy = manager.ImportTimeline(fd);
        close(fd);
        imported.ReleaseTimeline = timeline;
        return true;
    }

    private static void OnReleasePoint(IBuffer buffer, ImportedBuffer imported)
    {
        imported.ReleaseWaiter?.Dispose();
        imported.ReleaseWaiter = null;
        if (imported.Presented)
        {
            imported.Presented = false;
            buffer.Unlock();
        }
    }

    private void DestroySyncobjSurface()
    {
        if (_syncobjSurface is { IsDestroyed: false } surface)
        {
            surface.Dispose();
        }

        _syncobjSurface = null;
    }

    private void WarnSyncobjOnce(string reason)
    {
        if (_syncobjWarned)
        {
            return;
        }

        _syncobjWarned = true;
        Basin.Diagnostics.BasinLog.Warn(
            $"wayland backend: presenting without explicit sync — {reason}. On a driver that does not maintain a dmabuf's implicit fences this shows as tearing or corruption.");
    }

    private ImportedBuffer? TryImportDmabuf(IBuffer buffer)
    {
        if (_imported.TryGetValue(buffer, out var cached))
        {
            return cached;
        }

        if (_backend.ParentDmabuf is not { } dmabuf ||
            !buffer.TryGetDmabuf(out var attributes) ||
            !_backend.ParentDmabufFormats.Contains(attributes.Format, attributes.Modifier))
        {
            return null;
        }

        var bufferParams = dmabuf.CreateParams();
        for (var plane = 0; plane < attributes.PlaneCount; plane++)
        {
            bufferParams.Add(
                attributes.Fds[plane],
                (uint)plane,
                attributes.Offsets[plane],
                attributes.Strides[plane],
                (uint)(attributes.Modifier >> 32),
                (uint)attributes.Modifier);
        }

        var proxy = bufferParams.CreateImmed(buffer.Width, buffer.Height, (uint)attributes.Format, 0);
        bufferParams.Dispose();

        var imported = new ImportedBuffer(proxy);
        proxy.Release += (_, _) =>
        {
            if (imported.ReleaseWaiter is not null)
            {
                return;
            }

            if (imported.Presented)
            {
                imported.Presented = false;
                buffer.Unlock();
            }
        };
        buffer.Destroyed += () =>
        {
            imported.DisposeSync();
            _imported.Remove(buffer);
            if (imported.Presented)
            {
                imported.Presented = false;
                buffer.Unlock();
            }

            if (!proxy.IsDestroyed)
            {
                proxy.Dispose();
                _backend.Flush();
            }
        };

        _imported[buffer] = imported;
        return imported;
    }

    protected override void OnDestroy()
    {
        foreach (var (buffer, imported) in _imported)
        {
            imported.DisposeSync();
            if (imported.Presented)
            {
                imported.Presented = false;
                buffer.Unlock();
            }

            if (!imported.Proxy.IsDestroyed)
            {
                imported.Proxy.Dispose();
            }
        }

        _imported.Clear();
        ReleasePointerLock();
        DestroySyncobjSurface();
        if (_acquireProxy is { IsDestroyed: false } acquire)
        {
            acquire.Dispose();
        }

        _acquireProxy = null;
        _acquireTimeline?.Release();
        _acquireTimeline = null;
        _frameFallback.Remove();
        DestroySlots();
        _hostFrame?.Dispose();
        _hostFrame = null;
        _fractionalScale?.Dispose();
        _viewport?.Dispose();
        _decoration?.Dispose();
        _toplevel.Dispose();
        _xdgSurface.Dispose();
        _surface.Dispose();
        _backend.Flush();
    }

    private void OnPresented(WpPresentationFeedback.PresentedEventArgs e)
    {
        var seconds = ((ulong)e.TvSecHi << 32) | e.TvSecLo;
        PresentedOnScreen?.Invoke(
            (seconds * 1_000_000_000UL) + e.TvNsec,
            e.Refresh,
            ((ulong)e.SeqHi << 32) | e.SeqLo);
    }

    private void OnFrameDone()
    {
        if (!_framePending)
        {
            return;
        }

        _framePending = false;
        _frameFallback.UpdateTimer(0);
        EmitFrame();
    }

    private void OnFrameFallback()
    {
        if (_framePending)
        {
            _framePending = false;
            EmitFrame();
        }
    }

    private Slot? FreeSlot()
    {
        foreach (var slot in _slots)
        {
            if (slot is { Busy: false })
            {
                return slot;
            }
        }

        return null;
    }

    private unsafe bool CopyInto(Slot slot, IBuffer source)
    {
        if (!source.BeginDataAccess(BufferDataAccess.Read, out var view))
        {
            return false;
        }

        try
        {
            var rowBytes = CurrentMode.Width * 4;
            for (var y = 0; y < CurrentMode.Height; y++)
            {
                System.Buffer.MemoryCopy(
                    (void*)(view.Data + y * view.Stride),
                    (void*)(slot.Data + y * rowBytes),
                    rowBytes,
                    rowBytes);
            }

            return true;
        }
        finally
        {
            source.EndDataAccess();
        }
    }

    private unsafe void RebuildSlots(int width, int height)
    {
        DestroySlots();
        var stride = width * 4;
        _mappingSize = stride * height * 2;
        _mappingFd = memfd_create("basin-wl-output", 1 );
        if (_mappingFd < 0 || ftruncate(_mappingFd, _mappingSize) != 0)
        {
            throw new InvalidOperationException("output shm creation failed");
        }

        var map = mmap(null, (nuint)_mappingSize, ProtReadWrite, MapShared, _mappingFd, 0);
        if ((nint)map == -1)
        {
            throw new InvalidOperationException("output shm mmap failed");
        }

        _mapping = (nint)map;
        _pool = _backend.ParentShm.CreatePool(_mappingFd, _mappingSize);
        for (var i = 0; i < 2; i++)
        {
            var offset = i * stride * height;
            var proxy = _pool.CreateBuffer(offset, width, height, stride, WlShm.Format.Xrgb8888);
            var slot = new Slot(proxy, _mapping + offset);
            proxy.Release += (_, _) => slot.Busy = false;
            _slots[i] = slot;
        }
    }

    private unsafe void DestroySlots()
    {
        foreach (var slot in _slots)
        {
            if (slot is not null && !slot.Proxy.IsDestroyed)
            {
                slot.Proxy.Dispose();
            }
        }

        Array.Clear(_slots);
        _pool?.Dispose();
        _pool = null;
        if (_mapping != 0)
        {
            munmap((void*)_mapping, (nuint)_mappingSize);
            _mapping = 0;
            close(_mappingFd);
            _mappingFd = -1;
        }
    }

    private sealed class Slot(WlBuffer proxy, nint data)
    {
        public WlBuffer Proxy { get; } = proxy;

        public nint Data { get; } = data;

        public bool Busy { get; set; }
    }

    private sealed class ImportedBuffer(WlBuffer proxy)
    {
        public WlBuffer Proxy { get; } = proxy;

        public bool Presented { get; set; }

        public DrmSyncobjTimeline? ReleaseTimeline { get; set; }

        public WpLinuxDrmSyncobjTimelineV1? ReleaseProxy { get; set; }

        public ulong ReleasePoint { get; set; }

        public DrmSyncobjTimeline.DrmSyncobjWaiter? ReleaseWaiter { get; set; }

        public void DisposeSync()
        {
            ReleaseWaiter?.Dispose();
            ReleaseWaiter = null;
            if (ReleaseProxy is { IsDestroyed: false } proxy)
            {
                proxy.Dispose();
            }

            ReleaseProxy = null;
            ReleaseTimeline?.Release();
            ReleaseTimeline = null;
        }
    }
}
