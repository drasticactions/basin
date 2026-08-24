using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

public sealed class XdgToplevelWindow
{
    public const string RoleName = "xdg_toplevel";

    private readonly HashSet<XdgToplevel.State> _states = [];
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _configureScheduled;

    internal XdgToplevelWindow(XdgSurfaceState xdg, XdgToplevelResource resource)
    {
        Xdg = xdg;
        Resource = resource;

        resource.SetTitle += (_, e) =>
        {
            Title = e.Title;
            TitleChanged?.Invoke();
        };
        resource.SetAppId += (_, e) =>
        {
            AppId = e.AppId;
            AppIdChanged?.Invoke();
        };
        resource.SetParent += (_, e) =>
        {
            Parent = e.Parent is { } parent ? XdgToplevelRegistry.Resolve(parent) : null;
            ParentChanged?.Invoke();
        };
        resource.SetMinSize += (_, e) => (MinWidth, MinHeight) = (e.Width, e.Height);
        resource.SetMaxSize += (_, e) => (MaxWidth, MaxHeight) = (e.Width, e.Height);

        resource.Move += (_, e) =>
        {
            if (xdg.Shell.Seat is { } seat && seat.ValidateImplicitGrabSerial(e.Serial))
            {
                MoveRequested?.Invoke(e.Serial);
            }
        };
        resource.Resize += (_, e) =>
        {
            if (xdg.Shell.Seat is { } seat && seat.ValidateImplicitGrabSerial(e.Serial))
            {
                ResizeRequested?.Invoke(e.Serial, (ResizeEdges)e.Edges);
            }
        };
        resource.SetMaximized += (_, _) =>
        {
            RequestedMaximized = true;
            MaximizeRequested?.Invoke(true);
        };
        resource.UnsetMaximized += (_, _) =>
        {
            RequestedMaximized = false;
            MaximizeRequested?.Invoke(false);
        };
        resource.SetFullscreen += (_, e) =>
        {
            RequestedFullscreenOutput = OutputGlobal.FromResource(e.Output)?.Output;
            RequestedFullscreen = true;
            FullscreenRequested?.Invoke(true);
        };
        resource.UnsetFullscreen += (_, _) =>
        {
            RequestedFullscreenOutput = null;
            RequestedFullscreen = false;
            FullscreenRequested?.Invoke(false);
        };
        resource.SetMinimized += (_, _) =>
        {
            RequestedMinimized = true;
            MinimizeRequested?.Invoke();
        };
        resource.ShowWindowMenu += (_, e) => ShowWindowMenuRequested?.Invoke(e.X, e.Y);

        resource.Destroyed += (_, _) =>
        {
            XdgToplevelRegistry.Remove(resource);
            xdg.Surface.ClearRoleObject();
            xdg.OnRoleDestroyed();
            Destroyed?.Invoke();
        };
        XdgToplevelRegistry.Register(resource, this);
    }

    public XdgSurfaceState Xdg { get; }

    public Surface Surface => Xdg.Surface;

    internal XdgToplevelResource Resource { get; }

    public string Title { get; private set; } = string.Empty;

    public string AppId { get; private set; } = string.Empty;

    public XdgToplevelWindow? Parent { get; private set; }

    public int MinWidth { get; private set; }

    public int MinHeight { get; private set; }

    public int MaxWidth { get; private set; }

    public int MaxHeight { get; private set; }

    public bool IsMapped => Xdg.IsMapped;

    public bool? RequestedFullscreen { get; private set; }

    public IOutput? RequestedFullscreenOutput { get; private set; }

    public bool? RequestedMaximized { get; private set; }

    public bool RequestedMinimized { get; private set; }

    public XdgWmCapabilities WmCapabilities { get; set; } = XdgWmCapabilities.All;

    public event Action? TitleChanged;

    public event Action? ParentChanged;

    public event Action? AppIdChanged;

    public event Action<uint>? MoveRequested;

    public event Action<uint, ResizeEdges>? ResizeRequested;

    public event Action<bool>? MaximizeRequested;

    public event Action<bool>? FullscreenRequested;

    public event Action? MinimizeRequested;

    public event Action<int, int>? ShowWindowMenuRequested;

    public event Action? Destroyed;

    public void SetActivated(bool activated) => SetState(XdgToplevel.State.Activated, activated);

    public void SetMaximized(bool maximized) => SetState(XdgToplevel.State.Maximized, maximized);

    public void SetFullscreen(bool fullscreen) => SetState(XdgToplevel.State.Fullscreen, fullscreen);

    public void SetResizing(bool resizing) => SetState(XdgToplevel.State.Resizing, resizing);

    public void SetTiled(ResizeEdges edges)
    {
        SetState(XdgToplevel.State.TiledLeft, (edges & ResizeEdges.Left) != 0);
        SetState(XdgToplevel.State.TiledRight, (edges & ResizeEdges.Right) != 0);
        SetState(XdgToplevel.State.TiledTop, (edges & ResizeEdges.Top) != 0);
        SetState(XdgToplevel.State.TiledBottom, (edges & ResizeEdges.Bottom) != 0);
    }

    public ToplevelRestore? Restoring { get; private set; }

    public event Action<ToplevelRestore>? Restored;

    public void Restore(in ToplevelRestore restore)
    {
        Restoring = restore;
        var states = restore.State.States;
        SetSize(restore.State.Geometry.Width, restore.State.Geometry.Height);
        SetMaximized((states & ToplevelSessionStates.Maximized) != 0);
        SetFullscreen((states & ToplevelSessionStates.Fullscreen) != 0);
        SetTiled(
            ((states & ToplevelSessionStates.TiledLeft) != 0 ? ResizeEdges.Left : ResizeEdges.None) |
            ((states & ToplevelSessionStates.TiledRight) != 0 ? ResizeEdges.Right : ResizeEdges.None) |
            ((states & ToplevelSessionStates.TiledTop) != 0 ? ResizeEdges.Top : ResizeEdges.None) |
            ((states & ToplevelSessionStates.TiledBottom) != 0 ? ResizeEdges.Bottom : ResizeEdges.None));
        Restored?.Invoke(restore);
    }

    public ToplevelSessionStates SessionStates
    {
        get
        {
            var states = ToplevelSessionStates.None;
            foreach (var (state, flag) in SessionStateMap)
            {
                if (HasState(state))
                {
                    states |= flag;
                }
            }

            return states;
        }
    }

    private static readonly (XdgToplevel.State State, ToplevelSessionStates Flag)[] SessionStateMap =
    [
        (XdgToplevel.State.Maximized, ToplevelSessionStates.Maximized),
        (XdgToplevel.State.Fullscreen, ToplevelSessionStates.Fullscreen),
        (XdgToplevel.State.TiledLeft, ToplevelSessionStates.TiledLeft),
        (XdgToplevel.State.TiledRight, ToplevelSessionStates.TiledRight),
        (XdgToplevel.State.TiledTop, ToplevelSessionStates.TiledTop),
        (XdgToplevel.State.TiledBottom, ToplevelSessionStates.TiledBottom),
    ];

    public void SetSuspended(bool suspended) => SetState(XdgToplevel.State.Suspended, suspended);

    public void SetConstrained(ResizeEdges edges)
    {
        SetState(XdgToplevel.State.ConstrainedLeft, (edges & ResizeEdges.Left) != 0);
        SetState(XdgToplevel.State.ConstrainedRight, (edges & ResizeEdges.Right) != 0);
        SetState(XdgToplevel.State.ConstrainedTop, (edges & ResizeEdges.Top) != 0);
        SetState(XdgToplevel.State.ConstrainedBottom, (edges & ResizeEdges.Bottom) != 0);
    }

    public void SetBounds(int width, int height)
    {
        if (_boundsWidth == width && _boundsHeight == height)
        {
            return;
        }

        (_boundsWidth, _boundsHeight) = (width, height);
        _boundsPending = true;
        ScheduleConfigure();
    }

    private int _boundsWidth;
    private int _boundsHeight;
    private bool _boundsPending;

    public bool HasState(XdgToplevel.State state) => _states.Contains(state);

    public void SetSize(int width, int height)
    {
        if (_pendingWidth == width && _pendingHeight == height)
        {
            return;
        }

        _pendingWidth = width;
        _pendingHeight = height;
        ScheduleConfigure();
    }

    internal void AdoptCommittedSize()
    {
        if (Xdg.HasUnackedConfigure ||
            _states.Contains(XdgToplevel.State.Maximized) ||
            _states.Contains(XdgToplevel.State.Fullscreen))
        {
            return;
        }

        var geometry = Xdg.EffectiveGeometry;
        if (geometry.Width <= 0 || geometry.Height <= 0)
        {
            return;
        }

        _pendingWidth = geometry.Width;
        _pendingHeight = geometry.Height;
    }

    public void Close()
    {
        if (!Resource.IsDestroyed)
        {
            Resource.SendClose();
        }
    }

    public event Action? Configuring;

    public void RequestConfigure() => ScheduleConfigure();

    public uint SendConfigure() => SendConfigureCore(null);

    public uint SendConfigure(Transaction transaction)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        return SendConfigureCore(transaction);
    }

    private uint SendConfigureCore(Transaction? transaction)
    {
        Configuring?.Invoke();
        _configureScheduled = false;
        Span<byte> states = stackalloc byte[_states.Count * 4];
        var i = 0;
        foreach (var state in _states)
        {
            if (Resource.Version >= MinVersionFor(state))
            {
                BitConverter.TryWriteBytes(states[(i++ * 4)..], (uint)state);
            }
        }

        states = states[..(i * 4)];

        if (Resource.Version >= 5 && !_wmCapabilitiesSent)
        {
            _wmCapabilitiesSent = true;
            Span<byte> caps = stackalloc byte[16];
            var count = 0;
            if ((WmCapabilities & XdgWmCapabilities.WindowMenu) != 0)
            {
                BitConverter.TryWriteBytes(caps[(count++ * 4)..], 1u);
            }

            if ((WmCapabilities & XdgWmCapabilities.Maximize) != 0)
            {
                BitConverter.TryWriteBytes(caps[(count++ * 4)..], 2u);
            }

            if ((WmCapabilities & XdgWmCapabilities.Fullscreen) != 0)
            {
                BitConverter.TryWriteBytes(caps[(count++ * 4)..], 3u);
            }

            if ((WmCapabilities & XdgWmCapabilities.Minimize) != 0)
            {
                BitConverter.TryWriteBytes(caps[(count++ * 4)..], 4u);
            }

            Resource.SendWmCapabilities(caps[..(count * 4)]);
        }

        if (_boundsPending && Resource.Version >= 4)
        {
            _boundsPending = false;
            Resource.SendConfigureBounds(_boundsWidth, _boundsHeight);
        }

        Resource.SendConfigure(_pendingWidth, _pendingHeight, states);

        Restoring = null;
        return transaction is null ? Xdg.SendConfigure() : Xdg.SendConfigure(transaction);
    }

    private bool _wmCapabilitiesSent;

    private static int MinVersionFor(XdgToplevel.State state) => state switch
    {
        XdgToplevel.State.TiledLeft or XdgToplevel.State.TiledRight
            or XdgToplevel.State.TiledTop or XdgToplevel.State.TiledBottom => 2,
        XdgToplevel.State.Suspended => 6,
        XdgToplevel.State.ConstrainedLeft or XdgToplevel.State.ConstrainedRight
            or XdgToplevel.State.ConstrainedTop or XdgToplevel.State.ConstrainedBottom => 7,
        _ => 1,
    };

    private void SetState(XdgToplevel.State state, bool present)
    {
        var changed = present ? _states.Add(state) : _states.Remove(state);
        if (changed)
        {
            ScheduleConfigure();
        }
    }

    private void ScheduleConfigure()
    {
        if (!_configureScheduled)
        {
            _configureScheduled = true;
            Xdg.Shell.Display.EventLoop.AddIdle(() =>
            {
                if (_configureScheduled && !Resource.IsDestroyed)
                {
                    SendConfigureCore(null);
                }
            });
        }
    }
}
