using Basin.Capabilities;
using Basin.Shell.Xdg.Protocol;
using Wayland;

namespace Basin.Shell.Xdg;

/// <summary>
/// An <c>xdg_toplevel</c> role object. It reports the client's requests as events and coalesces state and size changes into one configure on the next idle callback.
/// </summary>
public sealed class XdgToplevelWindow : IToplevelHandle
{
    /// <summary>
    /// The role name that a toplevel sets on its <see cref="Basin.Surface"/>.
    /// </summary>
    public const string RoleName = "xdg_toplevel";

    private readonly HashSet<XdgToplevel.State> _states = [];
    private int _pendingWidth;
    private int _pendingHeight;
    private bool _sizeRequested;
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

    /// <summary>
    /// Gets the <c>xdg_surface</c> state under the toplevel.
    /// </summary>
    public XdgSurfaceState Xdg { get; }

    /// <summary>
    /// Gets the <c>wl_surface</c> of the toplevel. It is never <see langword="null"/>.
    /// </summary>
    public Surface Surface => Xdg.Surface;

    internal XdgToplevelResource Resource { get; }

    /// <summary>
    /// Gets the title that the client set. It is empty when the client set no title.
    /// </summary>
    public string Title { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the app_id that the client set. It is empty when the client set none.
    /// </summary>
    public string AppId { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the toplevel that the client made the parent, or <see langword="null"/> when it has none or the parent's role object is destroyed.
    /// </summary>
    public XdgToplevelWindow? Parent { get; private set; }

    /// <summary>
    /// Gets the minimum width that the client set, in logical pixels. A value of 0 means no minimum.
    /// </summary>
    public int MinWidth { get; private set; }

    /// <summary>
    /// Gets the minimum height that the client set, in logical pixels. A value of 0 means no minimum.
    /// </summary>
    public int MinHeight { get; private set; }

    /// <summary>
    /// Gets the maximum width that the client set, in logical pixels. A value of 0 means no maximum.
    /// </summary>
    public int MaxWidth { get; private set; }

    /// <summary>
    /// Gets the maximum height that the client set, in logical pixels. A value of 0 means no maximum.
    /// </summary>
    public int MaxHeight { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the <c>xdg_surface</c> is mapped.
    /// </summary>
    public bool IsMapped => Xdg.IsMapped;

    /// <summary>
    /// Gets the fullscreen state that the client last asked for, or <see langword="null"/> when it never asked.
    /// </summary>
    public bool? RequestedFullscreen { get; private set; }

    /// <summary>
    /// Gets the output that the client named in its last fullscreen request, or <see langword="null"/> when it named none.
    /// </summary>
    public IOutput? RequestedFullscreenOutput { get; private set; }

    /// <summary>
    /// Gets the maximized state that the client last asked for, or <see langword="null"/> when it never asked.
    /// </summary>
    public bool? RequestedMaximized { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the client asked to be minimized at least once. Nothing clears it.
    /// </summary>
    public bool RequestedMinimized { get; private set; }

    /// <summary>
    /// Gets or sets the actions that the first configure advertises to a client of version 5 or later. Later changes are not sent, so a consumer sets it in <see cref="XdgShell.NewToplevel"/>.
    /// </summary>
    public XdgWmCapabilities WmCapabilities { get; set; } = XdgWmCapabilities.All;

    /// <summary>
    /// Occurs when the client sets the title.
    /// </summary>
    public event Action? TitleChanged;

    /// <summary>
    /// Occurs when the client sets or clears the parent.
    /// </summary>
    public event Action? ParentChanged;

    /// <summary>
    /// Occurs when the client sets the app_id.
    /// </summary>
    public event Action? AppIdChanged;

    /// <summary>
    /// Occurs when the client asks for an interactive move with a serial that the seat accepts as an implicit grab. The argument is the serial.
    /// </summary>
    public event Action<uint>? MoveRequested;

    /// <summary>
    /// Occurs when the client asks for an interactive resize with a serial that the seat accepts as an implicit grab. The arguments are the serial and the edges.
    /// </summary>
    public event Action<uint, ResizeEdges>? ResizeRequested;

    /// <summary>
    /// Occurs when the client asks to be maximized or unmaximized, and when <see cref="RaiseMaximizeRequest"/> runs. The argument is the requested state.
    /// </summary>
    public event Action<bool>? MaximizeRequested;

    /// <summary>
    /// Occurs when the client asks to be fullscreen or not, and when <see cref="RaiseFullscreenRequest"/> runs. The argument is the requested state.
    /// </summary>
    public event Action<bool>? FullscreenRequested;

    /// <summary>
    /// Occurs when the client asks to be minimized.
    /// </summary>
    public event Action? MinimizeRequested;

    /// <summary>
    /// Occurs when the client asks for the window menu. The arguments are the position in surface-local coordinates. The serial is not validated.
    /// </summary>
    public event Action<int, int>? ShowWindowMenuRequested;

    /// <summary>
    /// Occurs when the client destroys the toplevel role object, after the surface unmaps.
    /// </summary>
    public event Action? Destroyed;

    /// <summary>
    /// Occurs when a state setter adds or removes a state, before the configure goes out.
    /// </summary>
    public event Action? StateChanged;

    IToplevelHandle? IToplevelHandle.Parent => Parent;

    bool IToplevelHandle.WantsFocus => true;

    (int Width, int Height) IToplevelHandle.NaturalSize
    {
        get
        {
            var geometry = Xdg.EffectiveGeometry;
            return (geometry.Width, geometry.Height);
        }
    }

    void IToplevelHandle.Configure(int x, int y, int width, int height) => SetSize(width, height);

    /// <summary>
    /// Sets or clears the activated state and schedules a configure when it changes. It does not move keyboard focus.
    /// </summary>
    /// <param name="activated"><see langword="true"/> to show the window as active.</param>
    public void SetActivated(bool activated) => SetState(XdgToplevel.State.Activated, activated);

    /// <summary>
    /// Sets or clears the maximized state and schedules a configure when it changes. The size is the consumer's to set with <see cref="SetSize"/>.
    /// </summary>
    /// <param name="maximized"><see langword="true"/> to tell the client that it is maximized.</param>
    public void SetMaximized(bool maximized) => SetState(XdgToplevel.State.Maximized, maximized);

    /// <summary>
    /// Sets or clears the fullscreen state and schedules a configure when it changes. The size is the consumer's to set with <see cref="SetSize"/>.
    /// </summary>
    /// <param name="fullscreen"><see langword="true"/> to tell the client that it is fullscreen.</param>
    public void SetFullscreen(bool fullscreen) => SetState(XdgToplevel.State.Fullscreen, fullscreen);

    /// <summary>
    /// Raises <see cref="MaximizeRequested"/> as if the client asked, so that policy decides. It does not change <see cref="RequestedMaximized"/>.
    /// </summary>
    /// <param name="maximized">The state to request.</param>
    /// <returns><see langword="false"/> when no handler is attached.</returns>
    public bool RaiseMaximizeRequest(bool maximized)
    {
        if (MaximizeRequested is not { } requested)
        {
            return false;
        }

        requested(maximized);
        return true;
    }

    /// <summary>
    /// Raises <see cref="FullscreenRequested"/> as if the client asked, so that policy decides. It does not change <see cref="RequestedFullscreen"/>.
    /// </summary>
    /// <param name="fullscreen">The state to request.</param>
    /// <returns><see langword="false"/> when no handler is attached.</returns>
    public bool RaiseFullscreenRequest(bool fullscreen)
    {
        if (FullscreenRequested is not { } requested)
        {
            return false;
        }

        requested(fullscreen);
        return true;
    }

    /// <summary>
    /// Sets or clears the resizing state and schedules a configure when it changes.
    /// </summary>
    /// <param name="resizing"><see langword="true"/> while an interactive resize runs.</param>
    public void SetResizing(bool resizing) => SetState(XdgToplevel.State.Resizing, resizing);

    /// <summary>
    /// Sets the four tiled states from the edges and schedules a configure when one changes. A client below version 2 does not receive them.
    /// </summary>
    /// <param name="edges">The edges that touch another window or the output edge. The other edges are cleared.</param>
    public void SetTiled(ResizeEdges edges)
    {
        SetState(XdgToplevel.State.TiledLeft, (edges & ResizeEdges.Left) != 0);
        SetState(XdgToplevel.State.TiledRight, (edges & ResizeEdges.Right) != 0);
        SetState(XdgToplevel.State.TiledTop, (edges & ResizeEdges.Top) != 0);
        SetState(XdgToplevel.State.TiledBottom, (edges & ResizeEdges.Bottom) != 0);
    }

    /// <summary>
    /// Gets the session state that <see cref="Restore"/> applied. It is <see langword="null"/> again after the next configure is sent.
    /// </summary>
    public ToplevelRestore? Restoring { get; private set; }

    /// <summary>
    /// Occurs at the end of <see cref="Restore"/>. The argument carries the saved position, which the consumer applies.
    /// </summary>
    public event Action<ToplevelRestore>? Restored;

    /// <summary>
    /// Applies the size, maximized, fullscreen and tiled states of a saved session to the next configure. It does not place the window.
    /// </summary>
    /// <param name="restore">The saved state.</param>
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

    /// <summary>
    /// Gets the maximized, fullscreen and tiled states that the compositor set, in the form that a session manager saves.
    /// </summary>
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

    /// <summary>
    /// Sets or clears the suspended state and schedules a configure when it changes. A client below version 6 does not receive it.
    /// </summary>
    /// <param name="suspended"><see langword="true"/> when the window is not visible.</param>
    public void SetSuspended(bool suspended) => SetState(XdgToplevel.State.Suspended, suspended);

    /// <summary>
    /// Sets the four constrained states from the edges and schedules a configure when one changes. A client below version 7 does not receive them.
    /// </summary>
    /// <param name="edges">The edges that the client cannot resize. The other edges are cleared.</param>
    public void SetConstrained(ResizeEdges edges)
    {
        SetState(XdgToplevel.State.ConstrainedLeft, (edges & ResizeEdges.Left) != 0);
        SetState(XdgToplevel.State.ConstrainedRight, (edges & ResizeEdges.Right) != 0);
        SetState(XdgToplevel.State.ConstrainedTop, (edges & ResizeEdges.Top) != 0);
        SetState(XdgToplevel.State.ConstrainedBottom, (edges & ResizeEdges.Bottom) != 0);
    }

    /// <summary>
    /// Sets the bounds that the next configure sends to a client of version 4 or later. A value equal to the last one schedules nothing.
    /// </summary>
    /// <param name="width">The largest useful width in logical pixels, or 0 for no bound.</param>
    /// <param name="height">The largest useful height in logical pixels, or 0 for no bound.</param>
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

    /// <summary>
    /// Finds whether the compositor set a state, also one that the client's version cannot receive.
    /// </summary>
    /// <param name="state">The state to look for.</param>
    /// <returns><see langword="true"/> when the state is set.</returns>
    public bool HasState(XdgToplevel.State state) => _states.Contains(state);

    /// <summary>
    /// Gets the size that the next configure carries, in logical pixels. When the client answers every configure and is not maximized or fullscreen, it follows the size that the client commits.
    /// </summary>
    public (int Width, int Height) ConfiguredSize => (_pendingWidth, _pendingHeight);

    /// <summary>
    /// Sets the size of the next configure and schedules it. A size equal to the current one schedules nothing. A value of 0 lets the client choose that dimension.
    /// </summary>
    /// <param name="width">The width in logical pixels.</param>
    /// <param name="height">The height in logical pixels.</param>
    public void SetSize(int width, int height)
    {
        if (_pendingWidth == width && _pendingHeight == height)
        {
            return;
        }

        _pendingWidth = width;
        _pendingHeight = height;
        _sizeRequested = true;
        ScheduleConfigure();
    }

    internal void AdoptCommittedSize()
    {
        if (Xdg.HasUnackedConfigure ||
            _sizeRequested ||
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

    /// <summary>
    /// Sends <c>close</c> to the client. The client can ignore it.
    /// </summary>
    public void Close()
    {
        if (!Resource.IsDestroyed)
        {
            Resource.SendClose();
        }
    }

    /// <summary>
    /// Occurs at the start of each configure, before the events go out. State that a handler sets goes into this configure.
    /// </summary>
    public event Action? Configuring;

    /// <summary>
    /// Schedules a configure for the next idle callback, also when nothing changed.
    /// </summary>
    public void RequestConfigure() => ScheduleConfigure();

    /// <summary>
    /// Sends a configure now and cancels a scheduled one.
    /// </summary>
    /// <returns>The <c>xdg_surface</c> configure serial.</returns>
    public uint SendConfigure() => SendConfigureCore(null);

    /// <summary>
    /// Sends a configure now, cancels a scheduled one, and makes the surface join a transaction.
    /// </summary>
    /// <param name="transaction">The transaction that waits for the client to acknowledge and commit.</param>
    /// <returns>The <c>xdg_surface</c> configure serial.</returns>
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
        _sizeRequested = false;

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
            StateChanged?.Invoke();
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
