using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Wayland;
using static Basin.Backend.Wayland.WaylandBackendLog;

namespace Basin.Backend.Wayland;

/// <summary>
/// Runs this compositor as a client of a parent Wayland compositor. Each output is an xdg toplevel on the parent, and the parent's seat supplies the input devices.
/// The parent connection is one more fd on the compositor's own event loop.
/// </summary>
public sealed class WaylandBackend : IDisposable
{
    private readonly ICompositorEventLoop _loop;

    internal ICompositorEventLoop Loop => _loop;
    private readonly string? _parentSocket;
    private readonly List<WaylandOutput> _outputs = [];
    private IEventSource? _source;
    private WlDisplay? _parent;

    internal WlDisplay? ParentDisplay => _parent;
    private bool _gone;
    private int _outputCounter;
    private Action<WaylandPointerDevice>? _pointerAdded;
    private CursorImages? _frameCursors;
    private ShmAllocator? _frameCursorAllocator;
    private Action<WaylandKeyboardDevice>? _keyboardAdded;
    private Action<WaylandTouchDevice>? _touchAdded;

    internal WlCompositor ParentCompositor = null!;
    internal WlShm ParentShm = null!;
    internal XdgWmBase ParentWmBase = null!;
    internal WlSubcompositor? ParentSubcompositor;
    internal WlSeat? ParentSeat;
    internal ZwpLinuxDmabufV1? ParentDmabuf;
    internal WpViewporter? ParentViewporter;
    internal WpFractionalScaleManagerV1? ParentFractionalScale;
    internal ZxdgDecorationManagerV1? ParentDecorations;
    internal WpLinuxDrmSyncobjManagerV1? ParentSyncobj;
    internal ZwpPointerGesturesV1? ParentPointerGestures;
    internal WlDataDeviceManager? ParentDataDeviceManager;
    internal ZwpPrimarySelectionDeviceManagerV1? ParentPrimarySelectionManager;
    internal WlDataDevice? ParentDataDevice;
    internal ZwpPrimarySelectionDeviceV1? ParentPrimarySelectionDevice;
    internal ZwpPointerConstraintsV1? ParentPointerConstraints;
    internal ZwpRelativePointerManagerV1? ParentRelativePointer;
    internal ZwpTextInputManagerV3? ParentTextInputManager;
    internal WpPresentation? ParentPresentation;
    internal ZwpIdleInhibitManagerV1? ParentIdleInhibit;
    internal XdgActivationV1? ParentActivation;

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe void* mmap(void* addr, nuint length, int prot, int flags, int fd, long offset);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int munmap(void* addr, nuint length);

    [DllImport("libc")]
    private static extern int close(int fd);

    /// <summary>
    /// Initializes a new instance of the <see cref="WaylandBackend"/> class. It does not connect until <see cref="Start"/>.
    /// </summary>
    /// <param name="loop">The compositor's event loop, which dispatches the parent connection.</param>
    /// <param name="parentSocket">The parent's socket name, or <see langword="null"/> to use <c>WAYLAND_DISPLAY</c>.</param>
    public WaylandBackend(ICompositorEventLoop loop, string? parentSocket = null)
    {
        _loop = loop;
        _parentSocket = parentSocket;
    }

    /// <summary>Gets the outputs that <see cref="CreateOutput"/> made and that are not destroyed.</summary>
    public IReadOnlyList<WaylandOutput> Outputs => _outputs;

    /// <summary>
    /// Gets or sets the GPU device that renders the outputs' frames. Set it before <see cref="Start"/>, and set it again after a lost renderer is replaced.
    /// With a device and a parent syncobj manager, outputs synchronize explicitly with the parent. The syncobj timelines live on a duplicate of the first device fd, so they stay valid when the device changes.
    /// </summary>
    public IRenderDevice? RenderDevice
    {
        get => _renderDevice;
        set
        {
            _renderDevice = value;
            if (value is not null && _syncFd < 0)
            {
                _syncFd = fcntl(value.DrmFd, FDupFdCloexec, 3);
            }
        }
    }

    private const int FDupFdCloexec = 1030;
    private IRenderDevice? _renderDevice;
    private int _syncFd = -1;

    internal int SyncFd => _syncFd;

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int command, int argument);

    /// <summary>Gets the pointer of the parent seat. It is <see langword="null"/> while the parent seat has no pointer.</summary>
    public WaylandPointerDevice? Pointer { get; private set; }

    /// <summary>Gets the keyboard of the parent seat. It is <see langword="null"/> while the parent seat has no keyboard.</summary>
    public WaylandKeyboardDevice? Keyboard { get; private set; }

    /// <summary>Gets the touch device of the parent seat. It is <see langword="null"/> while the parent seat has no touch device.</summary>
    public WaylandTouchDevice? Touch { get; private set; }

    /// <summary>
    /// Occurs when the parent seat gains a pointer. A handler added while <see cref="Pointer"/> is set runs at once with it.
    /// </summary>
    public event Action<WaylandPointerDevice> PointerAdded
    {
        add
        {
            _pointerAdded += value;
            if (Pointer is not null)
            {
                value(Pointer);
            }
        }

        remove => _pointerAdded -= value;
    }

    /// <summary>
    /// Occurs when the parent seat gains a keyboard. A handler added while <see cref="Keyboard"/> is set runs at once with it.
    /// </summary>
    public event Action<WaylandKeyboardDevice> KeyboardAdded
    {
        add
        {
            _keyboardAdded += value;
            if (Keyboard is not null)
            {
                value(Keyboard);
            }
        }

        remove => _keyboardAdded -= value;
    }

    /// <summary>
    /// Occurs when the parent seat gains a touch device. A handler added while <see cref="Touch"/> is set runs at once with it.
    /// </summary>
    public event Action<WaylandTouchDevice> TouchAdded
    {
        add
        {
            _touchAdded += value;
            if (Touch is not null)
            {
                value(Touch);
            }
        }

        remove => _touchAdded -= value;
    }

    /// <summary>
    /// Occurs after the parent seat loses its pointer. The device released its held buttons and left its output first, and it is disposed.
    /// </summary>
    public event Action<WaylandPointerDevice>? PointerRemoved;

    /// <summary>Occurs after the parent seat loses its keyboard. The device released its held keys and raised its leave event first.</summary>
    public event Action<WaylandKeyboardDevice>? KeyboardRemoved;

    /// <summary>Occurs after the parent seat loses its touch device. The device cancelled any points that were down first.</summary>
    public event Action<WaylandTouchDevice>? TouchRemoved;

    /// <summary>
    /// Occurs once when the parent connection hangs up or a dispatch or flush on it fails. After it, flushes do nothing.
    /// </summary>
    public event Action? ParentGone;

    /// <summary>Gets a value indicating whether the parent has pointer constraints and the seat has a pointer, so outputs can lock the pointer.</summary>
    public bool SupportsPointerLock => ParentPointerConstraints is not null && Pointer is not null;

    /// <summary>Gets a value indicating whether the parent has a text input manager and a seat for <see cref="WaylandSeamTextInput"/>.</summary>
    public bool SupportsTextInput => ParentTextInputManager is not null && ParentSeat is not null;

    internal bool ParentPresentationClockMatches { get; private set; }

    /// <summary>
    /// Gets the dmabuf formats and modifiers that the parent accepts. It is empty before <see cref="Start"/> or when the parent has no dmabuf version 3 or later.
    /// </summary>
    public DrmFormatSet ParentDmabufFormats { get; private set; } = DrmFormatSet.Empty;

    /// <summary>Gets the parent's color manager, or <see langword="null"/> when the parent has none.</summary>
    public WpColorManagerV1? ParentColorManager { get; private set; }

    /// <summary>Gets the color management features that the parent announced.</summary>
    public HashSet<WpColorManagerV1.Feature> ParentColorFeatures { get; } = [];

    /// <summary>Gets the named transfer functions that the parent announced.</summary>
    public HashSet<WpColorManagerV1.TransferFunction> ParentColorTransfers { get; } = [];

    /// <summary>Gets the named primaries that the parent announced.</summary>
    public HashSet<WpColorManagerV1.Primaries> ParentColorPrimaries { get; } = [];

    /// <summary>Gets the parent's toplevel icon manager, or <see langword="null"/> when the parent has none.</summary>
    public XdgToplevelIconManagerV1? ParentToplevelIcon { get; private set; }

    /// <summary>
    /// Connects to the parent, binds its globals and adds the connection to the event loop. Call it once.
    /// </summary>
    /// <exception cref="InvalidOperationException">The parent lacks <c>wl_compositor</c>, <c>wl_shm</c> or <c>xdg_wm_base</c>.</exception>
    public void Start()
    {
        _parent = _parentSocket is null ? WlDisplay.Connect() : WlDisplay.Connect(_parentSocket);

        WlCompositor? compositor = null;
        WlShm? shm = null;
        XdgWmBase? wmBase = null;
        WlSubcompositor? subcompositor = null;
        WlSeat? seat = null;
        ZwpLinuxDmabufV1? dmabuf = null;
        WpViewporter? viewporter = null;
        WpFractionalScaleManagerV1? fractionalScale = null;
        ZxdgDecorationManagerV1? decorations = null;
        WpLinuxDrmSyncobjManagerV1? syncobj = null;
        ZwpPointerGesturesV1? pointerGestures = null;
        WlDataDeviceManager? dataDeviceManager = null;
        ZwpPrimarySelectionDeviceManagerV1? primarySelection = null;
        ZwpPointerConstraintsV1? pointerConstraints = null;
        ZwpRelativePointerManagerV1? relativePointer = null;
        ZwpTextInputManagerV3? textInputManager = null;
        WpPresentation? presentation = null;
        ZwpIdleInhibitManagerV1? idleInhibit = null;
        XdgActivationV1? activation = null;
        WpColorManagerV1? colorManager = null;
        XdgToplevelIconManagerV1? toplevelIcon = null;
        var registry = _parent.GetRegistry();
        registry.Global += (_, e) =>
        {
            switch (e.Interface)
            {
                case "wl_compositor":
                    compositor = registry.Bind<WlCompositor>(e.Name, Math.Min(e.Version, 6));
                    break;
                case "wl_shm":
                    shm = registry.Bind<WlShm>(e.Name, 1);
                    break;
                case "wl_subcompositor":
                    subcompositor = registry.Bind<WlSubcompositor>(e.Name, 1);
                    break;
                case "xdg_wm_base":
                    wmBase = registry.Bind<XdgWmBase>(e.Name, Math.Min(e.Version, 5));
                    break;
                case "wl_seat":
                    var bound = registry.Bind<WlSeat>(e.Name, Math.Min(e.Version, 8));
                    seat = bound;

                    bound.Capabilities += (_, ev) => OnSeatCapabilities(bound, ev.Capabilities);
                    break;
                case "zwp_linux_dmabuf_v1" when e.Version >= 3:
                    dmabuf = registry.Bind<ZwpLinuxDmabufV1>(e.Name, Math.Min(e.Version, 4));
                    break;
                case "wp_viewporter":
                    viewporter = registry.Bind<WpViewporter>(e.Name, 1);
                    break;
                case "wp_fractional_scale_manager_v1":
                    fractionalScale = registry.Bind<WpFractionalScaleManagerV1>(e.Name, 1);
                    break;
                case "zxdg_decoration_manager_v1":
                    decorations = registry.Bind<ZxdgDecorationManagerV1>(e.Name, Math.Min(e.Version, 2));
                    break;
                case "wp_linux_drm_syncobj_manager_v1":
                    syncobj = registry.Bind<WpLinuxDrmSyncobjManagerV1>(e.Name, 1);
                    break;
                case "zwp_pointer_gestures_v1":
                    pointerGestures = registry.Bind<ZwpPointerGesturesV1>(e.Name, Math.Min(e.Version, 3));
                    break;
                case "wl_data_device_manager":
                    dataDeviceManager = registry.Bind<WlDataDeviceManager>(e.Name, Math.Min(e.Version, 3));
                    break;
                case "zwp_primary_selection_device_manager_v1":
                    primarySelection = registry.Bind<ZwpPrimarySelectionDeviceManagerV1>(e.Name, 1);
                    break;
                case "zwp_pointer_constraints_v1":
                    pointerConstraints = registry.Bind<ZwpPointerConstraintsV1>(e.Name, 1);
                    break;
                case "zwp_relative_pointer_manager_v1":
                    relativePointer = registry.Bind<ZwpRelativePointerManagerV1>(e.Name, 1);
                    break;
                case "zwp_text_input_manager_v3":
                    textInputManager = registry.Bind<ZwpTextInputManagerV3>(e.Name, 1);
                    break;
                case "xdg_activation_v1":
                    activation = registry.Bind<XdgActivationV1>(e.Name, 1);
                    break;
                case "wp_color_manager_v1":
                    colorManager = registry.Bind<WpColorManagerV1>(e.Name, Math.Min(e.Version, 2));
                    colorManager.SupportedFeature += (_, ev) => ParentColorFeatures.Add(ev.Feature);
                    colorManager.SupportedTfNamed += (_, ev) => ParentColorTransfers.Add(ev.Tf);
                    colorManager.SupportedPrimariesNamed += (_, ev) => ParentColorPrimaries.Add(ev.Primaries);
                    break;
                case "xdg_toplevel_icon_manager_v1":
                    toplevelIcon = registry.Bind<XdgToplevelIconManagerV1>(e.Name, 1);
                    break;
                case "zwp_idle_inhibit_manager_v1":
                    idleInhibit = registry.Bind<ZwpIdleInhibitManagerV1>(e.Name, 1);
                    break;
                case "wp_presentation":
                    presentation = registry.Bind<WpPresentation>(e.Name, Math.Min(e.Version, 2));
                    presentation.ClockId += (_, ev) => OnParentClockId(ev.ClkId);
                    break;
            }
        };
        _parent.Roundtrip();

        ParentCompositor = compositor ?? throw new InvalidOperationException("The parent compositor has no wl_compositor. The Wayland backend needs a parent with wl_compositor, wl_shm and xdg_wm_base.");
        ParentShm = shm ?? throw new InvalidOperationException("The parent compositor has no wl_shm. The Wayland backend needs a parent with wl_compositor, wl_shm and xdg_wm_base.");
        ParentWmBase = wmBase ?? throw new InvalidOperationException("The parent compositor has no xdg_wm_base. The Wayland backend needs a parent with wl_compositor, wl_shm and xdg_wm_base.");
        ParentSubcompositor = subcompositor;
        ParentSeat = seat;
        ParentDmabuf = dmabuf;
        ParentViewporter = viewporter;
        ParentFractionalScale = fractionalScale;
        ParentDecorations = decorations;
        ParentSyncobj = syncobj;
        ParentPointerGestures = pointerGestures;
        ParentDataDeviceManager = dataDeviceManager;
        ParentPrimarySelectionManager = primarySelection;
        ParentPointerConstraints = pointerConstraints;
        ParentRelativePointer = relativePointer;
        ParentTextInputManager = textInputManager;
        ParentPresentation = presentation;
        ParentIdleInhibit = idleInhibit;
        ParentActivation = activation;
        ParentColorManager = colorManager;
        ParentToplevelIcon = toplevelIcon;
        if (seat is not null)
        {
            ParentDataDevice = dataDeviceManager?.GetDataDevice(seat);
            ParentPrimarySelectionDevice = primarySelection?.GetDevice(seat);
        }

        ReportSeamGlobals();
        AttachPointerGestures();
        AttachRelativePointer();
        if (dmabuf is not null)
        {
            ParentDmabufFormats = CollectParentFormats(dmabuf);
        }
        ParentWmBase.Ping += (_, e) => ParentWmBase.Pong(e.Serial);

        _source = _loop.AddFd(_parent.Fd, FdReadiness.Readable, (_, events) =>
        {
            if ((events & (FdReadiness.Hangup | FdReadiness.Error)) != 0)
            {
                OnParentGone();
                return;
            }

            try
            {
                _parent.Dispatch();
            }
            catch (WaylandException e)
            {
                OnParentGone(e);
            }
        });
    }

    /// <summary>
    /// Creates an output as a new toplevel window on the parent. The output leaves <see cref="Outputs"/> when it is destroyed. Call it after <see cref="Start"/>.
    /// </summary>
    /// <param name="name">The output name, or <see langword="null"/> for <c>WL-1</c>, <c>WL-2</c> and so on.</param>
    /// <returns>The new output. It has no mode until the parent's first configure.</returns>
    public WaylandOutput CreateOutput(string? name = null)
    {
        var output = new WaylandOutput(this, name ?? $"WL-{++_outputCounter}");
        _outputs.Add(output);
        output.Destroyed += () => _outputs.Remove(output);
        Flush();
        return output;
    }

    /// <summary>
    /// Sends the queued requests to the parent. It does nothing before <see cref="Start"/> or after the parent is gone.
    /// A failure raises <see cref="ParentGone"/> instead of an exception.
    /// </summary>
    public void Flush()
    {
        if (_gone || _parent is null)
        {
            return;
        }

        try
        {
            _parent.Flush();
        }
        catch (WaylandException e)
        {
            OnParentGone(e);
        }
    }

    internal bool ShowFrameCursor(string name, double scale)
    {
        if (Pointer is not { } pointer)
        {
            return false;
        }

        if (_frameCursors is null)
        {
            var size = int.TryParse(Environment.GetEnvironmentVariable("XCURSOR_SIZE"), out var parsed) && parsed > 0 ? parsed : 24;
            _frameCursorAllocator = new ShmAllocator();
            _frameCursors = new CursorImages(_frameCursorAllocator, 256, 256, Environment.GetEnvironmentVariable("XCURSOR_THEME"), size, scale);
        }

        _frameCursors.Use(new CursorKey(scale, null), static () => { });
        return (_frameCursors.Named(name) ?? _frameCursors.Named("left_ptr")) is { } image &&
            pointer.SetFrameCursor(image.Buffer, image.HotspotX, image.HotspotY, scale);
    }

    /// <summary>Destroys every output, releases the input devices and the parent objects, and closes the parent connection.</summary>
    public void Dispose()
    {
        foreach (var output in _outputs.ToArray())
        {
            output.Destroy();
        }

        Pointer?.Dispose();
        _frameCursors?.Dispose();
        _frameCursors = null;
        _frameCursorAllocator?.Dispose();
        _frameCursorAllocator = null;
        DisposeParent(ParentPrimarySelectionDevice);
        DisposeParent(ParentDataDevice);
        ParentPrimarySelectionDevice = null;
        ParentDataDevice = null;
        _source?.Remove();
        _source = null;
        if (_parent is not null)
        {
            if (!_gone)
            {
                try
                {
                    _parent.Flush();
                }
                catch (WaylandException e)
                {
                    Log.Debug(e, $"parent: final flush failed at dispose");
                }
            }

            _parent.Dispose();
            _parent = null;
        }

        if (_syncFd >= 0)
        {
            _ = close(_syncFd);
            _syncFd = -1;
        }
    }

    internal void Roundtrip()
    {
        if (!_gone && _parent is not null)
        {
            try
            {
                _parent.Roundtrip();
            }
            catch (WaylandException e)
            {
                OnParentGone(e);
            }
        }
    }

    internal WaylandOutput? FindOutput(WlSurface? surface)
    {
        foreach (var output in _outputs)
        {
            if (output.ParentSurface == surface)
            {
                return output;
            }
        }

        return null;
    }

    internal uint? LastPointerSerial { get; set; }

    internal uint? LastKeyboardSerial { get; set; }

    internal uint? LastPointerButtonSerial { get; set; }

    internal uint LatestInputSerial => (LastPointerSerial, LastKeyboardSerial) switch
    {
        (null, null) => 0,
        ({ } pointer, null) => pointer,
        (null, { } keyboard) => keyboard,
        ({ } pointer, { } keyboard) => pointer > keyboard ? pointer : keyboard,
    };

    internal WaylandHostFrame? FindHostFrame(WlSurface? surface, out Point origin)
    {
        foreach (var output in _outputs)
        {
            if (output.HostFrame is { } frame && frame.TryLocate(surface, out origin))
            {
                return frame;
            }
        }

        origin = default;
        return null;
    }

    private DrmFormatSet CollectParentFormats(ZwpLinuxDmabufV1 dmabuf)
    {
        var formats = new DrmFormatSet();
        if (dmabuf.Version >= 4)
        {
            var feedback = dmabuf.GetDefaultFeedback();
            var tableFd = -1;
            var tableSize = 0u;
            var done = false;
            feedback.FormatTable += (_, e) => (tableFd, tableSize) = (e.Fd, e.Size);
            feedback.Done += (_, _) => done = true;
            for (var i = 0; i < 4 && !done; i++)
            {
                _parent!.Roundtrip();
            }

            if (tableFd >= 0)
            {
                ParseFormatTable(tableFd, tableSize, formats);
                _parent!.CloseFd(tableFd);
            }

            feedback.Dispose();
        }
        else
        {
#pragma warning disable CS0618
            dmabuf.Modifier += (_, e) =>
                formats.Add((DrmFormat)e.Format, ((ulong)e.ModifierHi << 32) | e.ModifierLo);
#pragma warning restore CS0618
            _parent!.Roundtrip();
        }

        return formats;
    }

    private static unsafe void ParseFormatTable(int fd, uint size, DrmFormatSet formats)
    {
        if (size == 0)
        {
            return;
        }

        var map = mmap(null, size, 1 , 1 , fd, 0);
        if ((nint)map == -1)
        {
            return;
        }

        var entries = (int)(size / 16);
        for (var i = 0; i < entries; i++)
        {
            var entry = (byte*)map + i * 16;
            formats.Add((DrmFormat)(*(uint*)entry), *(ulong*)(entry + 8));
        }

        munmap(map, size);
    }

    private void OnSeatCapabilities(WlSeat seat, WlSeat.Capability capabilities)
    {
        var time = (uint)Environment.TickCount64;
        if (!capabilities.HasFlag(WlSeat.Capability.Pointer) && Pointer is { } pointer)
        {
            Pointer = null;
            pointer.Remove(time);
            PointerRemoved?.Invoke(pointer);
        }

        if (!capabilities.HasFlag(WlSeat.Capability.Keyboard) && Keyboard is { } keyboard)
        {
            Keyboard = null;
            keyboard.Remove(time);
            KeyboardRemoved?.Invoke(keyboard);
        }

        if (!capabilities.HasFlag(WlSeat.Capability.Touch) && Touch is { } touch)
        {
            Touch = null;
            touch.Remove();
            TouchRemoved?.Invoke(touch);
        }

        if (capabilities.HasFlag(WlSeat.Capability.Pointer) && Pointer is null)
        {
            Pointer = new WaylandPointerDevice(this, seat.GetPointer());
            AttachPointerGestures();
            AttachRelativePointer();
            _pointerAdded?.Invoke(Pointer);
        }

        if (capabilities.HasFlag(WlSeat.Capability.Keyboard) && Keyboard is null)
        {
            Keyboard = new WaylandKeyboardDevice(this, seat.GetKeyboard());
            _keyboardAdded?.Invoke(Keyboard);
        }

        if (capabilities.HasFlag(WlSeat.Capability.Touch) && Touch is null)
        {
            Touch = new WaylandTouchDevice(this, seat.GetTouch());
            _touchAdded?.Invoke(Touch);
        }
    }

    internal static void ReleaseInputProxy(WlProxy proxy)
    {
        if (proxy.IsDestroyed)
        {
            return;
        }

        switch (proxy)
        {
            case WlKeyboard keyboard when keyboard.Version >= 3:
                keyboard.Release();
                break;
            case WlPointer pointer when pointer.Version >= 3:
                pointer.Release();
                break;
            case WlTouch touch when touch.Version >= 3:
                touch.Release();
                break;
            default:
                proxy.Dispose();
                break;
        }
    }

    internal static void DisposeParent(WlProxy? proxy)
    {
        if (proxy is { IsDestroyed: false })
        {
            proxy.Dispose();
        }
    }

    private void ReportSeamGlobals()
    {
        if (ParentSeat is null)
        {
            Log.Info(
                $"parent: no wl_seat, guests keep their own clipboard and take no host input");
            return;
        }

        if (ParentDataDeviceManager is null)
        {
            Log.Info(
                $"parent: no wl_data_device_manager, guests share a clipboard only with each other");
        }
        else if (ParentDataDeviceManager.Version < 3)
        {
            Log.Info(
                $"parent: wl_data_device_manager is version {ParentDataDeviceManager.Version}, drag actions fall back to copy");
        }

        if (ParentPrimarySelectionManager is null)
        {
            Log.Info(
                $"parent: no zwp_primary_selection_device_manager_v1, middle-click paste works only among guests");
        }

        if (ParentPointerConstraints is null)
        {
            Log.Info(
                $"parent: no zwp_pointer_constraints_v1, a guest pointer lock cannot confine the host cursor");
        }

        if (ParentRelativePointer is null)
        {
            Log.Info(
                $"parent: no zwp_relative_pointer_manager_v1, a locked pointer gets deltas from absolute motion");
        }

        if (ParentTextInputManager is null)
        {
            Log.Info(
                $"parent: no zwp_text_input_manager_v3, guests get no composition from the host input method");
        }

        if (ParentPresentation is null)
        {
            Log.Info(
                $"parent: no wp_presentation, guest presentation timestamps stay synthesized");
        }

        if (ParentIdleInhibit is null)
        {
            Log.Info(
                $"parent: no zwp_idle_inhibit_manager_v1, the host can blank while a guest plays video");
        }

        if (ParentActivation is null)
        {
            Log.Info(
                $"parent: no xdg_activation_v1, a guest cannot raise the nested window");
        }
    }

    private void OnParentClockId(uint clockId)
    {
        ParentPresentationClockMatches = clockId == PresentationTimeGlobal.ClockMonotonic;
        if (!ParentPresentationClockMatches)
        {
            Log.Warn(
                $"parent: presents on clock {clockId}, not CLOCK_MONOTONIC, guest presentation timestamps stay synthesized");
        }
    }

    private void AttachPointerGestures()
    {
        if (Pointer is { } pointer && ParentPointerGestures is { } gestures)
        {
            pointer.AttachGestures(gestures);
        }
    }

    private void AttachRelativePointer()
    {
        if (Pointer is { } pointer && ParentRelativePointer is { } manager)
        {
            pointer.AttachRelativePointer(manager);
        }
    }

    private void OnParentGone(Exception? cause = null)
    {
        if (!_gone)
        {
            _gone = true;
            if (cause is null)
            {
                Log.Info($"parent: connection closed");
            }
            else
            {
                Log.Error(cause, $"parent: connection lost");
            }

            ParentGone?.Invoke();
        }
    }
}
