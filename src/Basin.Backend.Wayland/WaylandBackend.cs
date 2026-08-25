using System.Runtime.InteropServices;
using Basin.Backend.Wayland.Protocol;
using Basin.Protocol;
using Wayland;
using static Basin.Backend.Wayland.WaylandBackendLog;

namespace Basin.Backend.Wayland;

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

    public WaylandBackend(ICompositorEventLoop loop, string? parentSocket = null)
    {
        _loop = loop;
        _parentSocket = parentSocket;
    }

    public IReadOnlyList<WaylandOutput> Outputs => _outputs;

    public IRenderDevice? RenderDevice { get; set; }

    public WaylandPointerDevice? Pointer { get; private set; }

    public WaylandKeyboardDevice? Keyboard { get; private set; }

    public WaylandTouchDevice? Touch { get; private set; }

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

    public event Action? ParentGone;

    public bool SupportsPointerLock => ParentPointerConstraints is not null && Pointer is not null;

    public bool SupportsTextInput => ParentTextInputManager is not null && ParentSeat is not null;

    internal bool ParentPresentationClockMatches { get; private set; }

    public DrmFormatSet ParentDmabufFormats { get; private set; } = DrmFormatSet.Empty;

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

        ParentCompositor = compositor ?? throw new InvalidOperationException("parent compositor lacks wl_compositor");
        ParentShm = shm ?? throw new InvalidOperationException("parent compositor lacks wl_shm");
        ParentWmBase = wmBase ?? throw new InvalidOperationException("parent compositor lacks xdg_wm_base");
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
            catch (WaylandException)
            {
                OnParentGone();
            }
        });
    }

    public WaylandOutput CreateOutput(string? name = null)
    {
        var output = new WaylandOutput(this, name ?? $"WL-{++_outputCounter}");
        _outputs.Add(output);
        output.Destroyed += () => _outputs.Remove(output);
        Flush();
        return output;
    }

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
        catch (WaylandException)
        {
            OnParentGone();
        }
    }

    public void Dispose()
    {
        foreach (var output in _outputs.ToArray())
        {
            output.Destroy();
        }

        Pointer?.Dispose();
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
                catch (WaylandException)
                {
                }
            }

            _parent.Dispose();
            _parent = null;
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
            catch (WaylandException)
            {
                OnParentGone();
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
                $"wayland backend: parent has no wl_seat; guests keep their own clipboard and take no host input");
            return;
        }

        if (ParentDataDeviceManager is null)
        {
            Log.Info(
                $"wayland backend: parent lacks wl_data_device_manager; guests share a clipboard with each other only");
        }
        else if (ParentDataDeviceManager.Version < 3)
        {
            Log.Info(
                $"wayland backend: parent wl_data_device_manager is version {ParentDataDeviceManager.Version}; drag actions fall back to copy");
        }

        if (ParentPrimarySelectionManager is null)
        {
            Log.Info(
                $"wayland backend: parent lacks zwp_primary_selection_device_manager_v1; middle-click paste works among guests only");
        }

        if (ParentPointerConstraints is null)
        {
            Log.Info(
                $"wayland backend: parent lacks zwp_pointer_constraints_v1; a guest pointer lock cannot confine the host cursor");
        }

        if (ParentRelativePointer is null)
        {
            Log.Info(
                $"wayland backend: parent lacks zwp_relative_pointer_manager_v1; a locked pointer gets deltas from absolute motion");
        }

        if (ParentTextInputManager is null)
        {
            Log.Info(
                $"wayland backend: parent lacks zwp_text_input_manager_v3; guests type without composition from the host's input method");
        }

        if (ParentPresentation is null)
        {
            Log.Info(
                $"wayland backend: parent lacks wp_presentation; guest presentation timestamps stay synthesized");
        }

        if (ParentIdleInhibit is null)
        {
            Log.Info(
                $"wayland backend: parent lacks zwp_idle_inhibit_manager_v1; the host may blank while a guest plays video");
        }

        if (ParentActivation is null)
        {
            Log.Info(
                $"wayland backend: parent lacks xdg_activation_v1; a guest cannot raise the nested window");
        }
    }

    private void OnParentClockId(uint clockId)
    {
        ParentPresentationClockMatches = clockId == PresentationTimeGlobal.ClockMonotonic;
        if (!ParentPresentationClockMatches)
        {
            Log.Warn(
                $"wayland backend: parent presents on clock {clockId} rather than CLOCK_MONOTONIC; guest presentation timestamps stay synthesized");
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

    private void OnParentGone()
    {
        if (!_gone)
        {
            _gone = true;
            ParentGone?.Invoke();
        }
    }
}
