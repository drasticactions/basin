using Basin;
using Basin.Host;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace Dam;

internal sealed class DamViews
{
    private readonly Scene _scene;
    private readonly OutputLayout _layout;
    private readonly Basin.Seat.Seat _seat;
    private readonly OutputDriver _outputs;
    private readonly List<DamView> _views = [];
    private readonly Dictionary<Surface, DamView> _owners = [];

    public DamViews(Scene scene, OutputLayout layout, Basin.Seat.Seat seat, OutputDriver outputs)
    {
        _scene = scene;
        _layout = layout;
        _seat = seat;
        _outputs = outputs;
    }

    public IReadOnlyList<DamView> Views => _views;

    public Action? PointerRefocus { get; set; }

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
        wm.WindowMapped += window => OnX11Mapped(window, managed: true);
        wm.OverrideRedirectMapped += window => OnX11Mapped(window, managed: false);
    }

    private void OnX11Mapped(Basin.XWayland.XWaylandWindow window, bool managed)
    {
        if (window.Surface is not { } surface)
        {
            return;
        }

        var scene = new SceneSurface(_scene.Root, surface);
        var view = new DamView(window, scene);
        if (managed)
        {
            Position(view);
        }
        else
        {
            scene.Tree.SetPosition(window.X, window.Y);
        }

        _views.Insert(0, view);
        _owners[surface] = view;

        void Cleanup()
        {
            window.Unmapped -= OnUnmapped;
            window.Destroyed -= OnDestroyed;
            _views.Remove(view);
            _owners.Remove(surface);
            if (!scene.IsDestroyed)
            {
                scene.Destroy();
            }
        }

        void OnUnmapped() => Cleanup();

        void OnDestroyed()
        {
            Cleanup();
            if (_views.Count > 0)
            {
                Focus(_views[0]);
            }
        }

        window.Unmapped += OnUnmapped;
        window.Destroyed += OnDestroyed;
        Focus(view);
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
        PointerRefocus?.Invoke();
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

        var scene = new SceneSurface(view.Scene.Tree, popup.Surface);
        _owners[popup.Surface] = view;

        void Place()
        {
            var chain = PopupChainOffset(popup);
            scene.Tree.SetPosition(
                chain.X + popup.SurfacePosition.X, chain.Y + popup.SurfacePosition.Y);
        }

        void Constrain()
        {
            var chain = PopupChainOffset(popup);
            var originX = view.Scene.Tree.X + chain.X;
            var originY = view.Scene.Tree.Y + chain.Y;
            var output = _layout.OutputAt(originX, originY);
            var box = output is null ? _layout.Bounds : _layout.BoxOf(output);
            popup.Unconstrain(new Box(box.X - originX, box.Y - originY, box.Width, box.Height));
        }

        Constrain();
        Place();
        popup.Xdg.Committed += Place;
        popup.GeometryChanged += Place;
        popup.Repositioned += Constrain;
        scene.Destroyed += () =>
        {
            popup.Xdg.Committed -= Place;
            popup.GeometryChanged -= Place;
            popup.Repositioned -= Constrain;
            _owners.Remove(popup.Surface);
        };
        popup.Destroyed += () =>
        {
            if (!scene.IsDestroyed)
            {
                scene.Destroy();
            }
        };
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

    private static Point PopupChainOffset(XdgPopupWindow popup)
    {
        var x = 0;
        var y = 0;
        var xdg = popup.Parent;
        while (xdg?.Role is XdgPopupWindow parent)
        {
            x += parent.Geometry.X;
            y += parent.Geometry.Y;
            xdg = parent.Parent;
        }

        return new Point(x, y);
    }
}
