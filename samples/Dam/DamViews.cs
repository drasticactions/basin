using Basin;
using Basin.Host;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Dam;

internal sealed class DamViews
{
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly Basin.Desktop.PopupPlacer _popups;
    private readonly Basin.Seat.Seat _seat;
    private readonly OutputDriver _outputs;
    private readonly List<DamView> _views = [];
    private readonly Basin.XWayland.XWaylandSceneDriver _xwayland = new();
    private readonly Dictionary<Surface, DamView> _owners = [];

    public DamViews(Scene scene, OutputLayout layout, Basin.Seat.Seat seat, OutputDriver outputs)
    {
        _scene = scene;
        _layout = layout;
        _popups = new Basin.Desktop.PopupPlacer(layout);
        _seat = seat;
        _outputs = outputs;
    }

    public IReadOnlyList<DamView> Views => _views;

    public DamView? FocusedView
    {
        get
        {
            if (_seat.Keyboard.Focus is not { } focus)
            {
                return null;
            }

            foreach (var view in _views)
            {
                if (ReferenceEquals(view.Surface, focus))
                {
                    return view;
                }
            }

            return null;
        }
    }

    public void Attach(XdgShell shell, XdgToplevelSource source)
    {
        shell.NewToplevel += OnNewToplevel;
        shell.NewPopup += OnNewPopup;
        source.ActivateRequested += window =>
        {
            if (ViewOf(window) is { } view)
            {
                view.Scene.Tree.RaiseToTop();
                Focus(view);
            }
        };
        _layout.Changed += PositionAll;
    }

    public void AttachXWayland(Basin.XWayland.XWaylandWm wm)
    {
        _xwayland.ManagedParent = _ => _scene.Root;
        _xwayland.OverrideRedirectParent = _ => _scene.Root;
        _xwayland.Adopted += OnX11Adopted;
        _xwayland.Removed += OnX11Removed;
        _xwayland.Attach(wm);
    }

    private void OnX11Adopted(Basin.XWayland.XWaylandWindow window, SceneSurface scene, bool managed)
    {
        var view = new DamView(window, scene);
        if (managed)
        {
            Position(view);
        }

        _views.Insert(0, view);
        _owners[window.Surface!] = view;
        Focus(view);
    }

    private void OnX11Removed(Basin.XWayland.XWaylandWindow window, SceneSurface scene)
    {
        for (var i = _views.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_views[i].X11, window))
            {
                continue;
            }

            var view = _views[i];
            _views.RemoveAt(i);
            _owners.Remove(view.Surface);
        }

        if (_views.Count > 0)
        {
            Focus(_views[0]);
        }
    }

    public DamView? OwnerOf(Surface? surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            if (_owners.TryGetValue(candidate, out var view))
            {
                return view;
            }
        }

        return null;
    }

    public void Focus(DamView? view)
    {
        if (view is null || ReferenceEquals(FocusedView, view))
        {
            return;
        }

        if (!view.WantsFocus)
        {
            return;
        }

        FocusedView?.SetActivated(false);

        if (!view.IsPrimary)
        {
            _views.Remove(view);
            _views.Insert(0, view);
        }

        view.SetActivated(true);
        foreach (var output in _outputs.Views)
        {
            if (output.Output is Basin.Backend.Wayland.WaylandOutput nested && nested.Enabled)
            {
                nested.SetTitle(view.Title);
            }
        }

        _seat.Keyboard.NotifyEnter(view.Surface);
    }

    public void PositionAll()
    {
        foreach (var view in _views)
        {
            Position(view);
        }
    }

    public void Position(DamView view)
    {
        var layout = _layout.Bounds;
        var (width, height) = view.GeometrySize();
        if (view.IsPrimary || layout.Width < width || layout.Height < height)
        {
            view.Scene.Tree.SetPosition(layout.X, layout.Y);
            view.Maximize(layout.X, layout.Y, layout.Width, layout.Height);
        }
        else
        {
            view.Scene.Tree.SetPosition((layout.Width - width) / 2, (layout.Height - height) / 2);
        }
    }

    public void SetFullscreen(DamView view, bool fullscreen)
    {
        var layout = _layout.Bounds;
        view.Xdg!.SetSize(layout.Width, layout.Height);
        view.Xdg.SetFullscreen(fullscreen);
    }

    private DamView? ViewOf(XdgToplevelWindow window)
    {
        foreach (var view in _views)
        {
            if (ReferenceEquals(view.Xdg, window))
            {
                return view;
            }
        }

        return null;
    }

    private void OnNewToplevel(XdgToplevelWindow window)
    {
        var scene = new SceneSurface(_scene.Root, window.Surface);
        scene.Tree.Enabled = false;
        var view = new DamView(window, scene);
        var initialized = false;

        window.WmCapabilities = XdgWmCapabilities.Fullscreen;

        window.Configuring += () =>
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            if (view.WantsFullscreen)
            {
                SetFullscreen(view, true);
            }
            else
            {
                Position(view);
            }
        };

        window.FullscreenRequested += fullscreen =>
        {
            if (!initialized)
            {
                view.WantsFullscreen = fullscreen;
                return;
            }

            SetFullscreen(view, fullscreen);
        };
        window.MaximizeRequested += _ => window.RequestConfigure();
        window.MinimizeRequested += () => window.RequestConfigure();

        window.Xdg.Mapped += () =>
        {
            scene.Tree.Enabled = true;
            Position(view);
            _views.Insert(0, view);
            _owners[window.Surface] = view;
            Focus(view);
        };
        window.Xdg.Unmapped += () =>
        {
            scene.Tree.Enabled = false;
            _views.Remove(view);
            _owners.Remove(window.Surface);
        };
        window.Destroyed += () =>
        {
            _views.Remove(view);
            _owners.Remove(window.Surface);
            if (_views.Count > 0)
            {
                Focus(_views[0]);
            }
        };
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (RootViewOf(popup) is not { } view)
        {
            return;
        }

        var scene = _popups.Attach(popup, view.Scene.Tree);
        _owners[popup.Surface] = view;
        scene.Destroyed += () => _owners.Remove(popup.Surface);
    }

    private DamView? RootViewOf(XdgPopupWindow popup)
    {
        var parent = popup.Parent;
        while (parent is not null)
        {
            switch (parent.Role)
            {
                case XdgToplevelWindow toplevel:
                    return ViewOf(toplevel);
                case XdgPopupWindow parentPopup:
                    parent = parentPopup.Parent;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

}
