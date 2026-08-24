using Basin;
using Basin.Capabilities;
using Basin.Plasma;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace PlasmaHost;

internal sealed class PlasmaHostWindows
{
    private readonly OutputLayout _layout;
    private readonly Basin.Seat.Seat _seat;
    private readonly PlasmaShellPlacement _placement;
    private readonly PlasmaShellManager _manager;
    private readonly List<PlasmaHostView> _views = [];
    private readonly Dictionary<Surface, PlasmaHostView> _owners = [];
    private readonly Dictionary<Surface, bool> _ssdPreference = [];
    private Basin.Desktop.LayerShellSceneDriver _layerDriver = null!;
    private readonly Dictionary<IOutput, Box> _usable = [];
    private readonly Basin.Desktop.PopupPlacer _popups;
    private XdgToplevelSource? _source;
    private XdgDecorationManager? _decorations;
    private Basin.Desktop.KdeServerDecorationManager? _kdeDecorations;
    private ServerDecorationPaletteManager? _palettes;
    private PlasmaHostFrames? _frames;
    private int _cascade;

    public PlasmaHostWindows(
        OutputLayout layout,
        Basin.Seat.Seat seat,
        PlasmaShellPlacement placement,
        PlasmaShellManager manager)
    {
        _layout = layout;
        _popups = new Basin.Desktop.PopupPlacer(layout);
        _seat = seat;
        _placement = placement;
        _manager = manager;
        _placement.UsableAreaChanged += RecomputeUsable;
        _layout.Changed += RecomputeUsable;
    }

    public IReadOnlyList<PlasmaHostView> Views => _views;

    public Func<bool>? FocusLocked { get; set; }

    public event Action<PlasmaHostView>? ViewMapped;

    public event Action<SceneSurface>? SceneCreated;

    public event Action<PlasmaHostView, uint?>? MoveGrabRequested;

    public event Action<PlasmaHostView, ResizeEdges, uint?>? ResizeGrabRequested;

    public IReadOnlyList<(LayerSurface Layer, SceneSurface? Scene)> LayerSurfaces => _layerDriver.Surfaces;

    public PlasmaHostView? FocusedView
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

    public void Attach(XdgShell shell, XdgToplevelSource source, LayerShell layerShell)
    {
        _source = source;
        shell.NewToplevel += OnNewToplevel;
        shell.NewPopup += OnNewPopup;
        WireLayerShell(shell, layerShell);
        source.ActivateRequested += window =>
        {
            if (ViewOf(window) is { } view)
            {
                if (view.Minimized)
                {
                    Minimize(view, false);
                    return;
                }

                view.Tree.RaiseToTop();
                Focus(view);
            }
        };
        source.MinimizeRequested += (window, minimized) =>
        {
            if (ViewOf(window) is { } view)
            {
                Minimize(view, minimized);
            }
        };
        source.NoBorderRequested += (window, noBorder) =>
            RecordDecorationPreference(window.Surface, !noBorder);
    }

    public void WireDecorations(
        XdgDecorationManager decorations,
        Basin.Desktop.KdeServerDecorationManager kdeDecorations,
        ServerDecorationPaletteManager palettes,
        PlasmaHostFrames frames)
    {
        _decorations = decorations;
        _kdeDecorations = kdeDecorations;
        _palettes = palettes;
        _frames = frames;
        decorations.ModeChanged += (toplevel, mode) =>
            RecordDecorationPreference(toplevel.Surface, mode == DecorationMode.ServerSide);
        kdeDecorations.ModeRequested += (surface, mode) =>
            RecordDecorationPreference(surface, mode == Basin.Desktop.KdeServerDecorationManager.DecorationMode.Server);
        palettes.PaletteChanged += (surface, _) =>
        {
            if (_owners.TryGetValue(surface, out var view))
            {
                LayoutDecorations(view);
            }
        };
    }

    public PlasmaHostView? ViewFor(Surface surface) => _owners.GetValueOrDefault(surface);

    public Box UsableArea(IOutput output) =>
        _usable.TryGetValue(output, out var box) ? box : _placement.UsableArea(output);

    public (Frame Frame, PlasmaHostView Owner)? FindFrame(SceneNode node)
    {
        foreach (var view in _views)
        {
            if (view.Frame is { } frame && frame.OwnsNode(node))
            {
                return (frame, view);
            }
        }

        return null;
    }

    public PlasmaHostView? FindOwner(SceneNode node)
    {
        for (SceneNode? candidate = node; candidate is not null; candidate = candidate.Parent)
        {
            foreach (var view in _views)
            {
                if (ReferenceEquals(candidate, view.Tree))
                {
                    return view;
                }
            }
        }

        return null;
    }

    public bool IsAboveWindows(SceneNode node)
    {
        for (SceneNode? candidate = node; candidate is not null; candidate = candidate.Parent)
        {
            if (ReferenceEquals(candidate, _placement.Top) ||
                ReferenceEquals(candidate, _placement.Overlay) ||
                ReferenceEquals(candidate, _placement.Layers.Lock))
            {
                return true;
            }
        }

        return false;
    }

    public (Frame Frame, PlasmaHostView Owner)? FindMenu(SceneNode node)
    {
        foreach (var view in _views)
        {
            if (view.Frame is { } frame && frame.OwnsMenuNode(node))
            {
                return (frame, view);
            }
        }

        return null;
    }

    public void SetIconName(XdgToplevelWindow window, string? name)
    {
        if (ViewOf(window) is { } view)
        {
            view.IconName = name;
            LayoutDecorations(view);
        }
    }

    public void FocusAt(Surface? surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            if (_manager.For(candidate) is { } plasma)
            {
                if (plasma.Focusable)
                {
                    Activate(candidate);
                }

                return;
            }

            if (_owners.TryGetValue(candidate, out var view))
            {
                var current = FocusedView;
                if (!ReferenceEquals(view, current) &&
                    (current is null || !current.IsTransientFor(view)))
                {
                    view.Tree.RaiseToTop();
                    Focus(view);
                }

                return;
            }
        }
    }

    public void Focus(PlasmaHostView view)
    {
        if (FocusLocked?.Invoke() == true || ReferenceEquals(FocusedView, view))
        {
            return;
        }

        if (FocusedView is { } previous)
        {
            previous.Xdg.SetActivated(false);
            SetFrameActive(previous, false);
        }

        view.Xdg.SetActivated(true);
        SetFrameActive(view, true);
        _views.Remove(view);
        _views.Insert(0, view);
        _seat.Keyboard.NotifyEnter(view.Surface);
    }

    private void Activate(Surface surface)
    {
        if (FocusLocked?.Invoke() == true || ReferenceEquals(_seat.Keyboard.Focus, surface))
        {
            return;
        }

        if (FocusedView is { } previous)
        {
            previous.Xdg.SetActivated(false);
            SetFrameActive(previous, false);
        }

        if (surface.RoleObject is XdgToplevelWindow toplevel)
        {
            toplevel.SetActivated(true);
        }

        _seat.Keyboard.NotifyEnter(surface);
    }

    private void SetFrameActive(PlasmaHostView view, bool active)
    {
        if (view.Active == active)
        {
            return;
        }

        view.Active = active;
        LayoutDecorations(view);
    }

    private PlasmaHostView? ViewOf(XdgToplevelWindow window)
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
        PlasmaHostView? view = null;

        window.MaximizeRequested += maximized =>
        {
            if (view is { } mapped)
            {
                SetMaximized(mapped, maximized);
            }

            window.RequestConfigure();
        };
        window.FullscreenRequested += fullscreen =>
        {
            if (view is { } mapped)
            {
                SetFullscreen(mapped, fullscreen);
            }
            else if (fullscreen)
            {
                var box = FullscreenBox(window, fallbackX: _seat.Pointer.X, fallbackY: _seat.Pointer.Y);
                window.SetFullscreen(true);
                window.SetSize(box.Width, box.Height);
            }

            window.RequestConfigure();
        };
        window.MinimizeRequested += () =>
        {
            if (view is { } mapped)
            {
                Minimize(mapped, true);
            }

            window.RequestConfigure();
        };
        window.MoveRequested += serial =>
        {
            if (view is { } mapped)
            {
                MoveGrabRequested?.Invoke(mapped, serial);
            }
        };
        window.ResizeRequested += (serial, edges) =>
        {
            if (view is { } mapped)
            {
                ResizeGrabRequested?.Invoke(mapped, edges, serial);
            }
        };
        window.TitleChanged += () =>
        {
            if (view is { } mapped)
            {
                LayoutDecorations(mapped);
            }
        };
        window.AppIdChanged += () =>
        {
            if (view is { } mapped)
            {
                LayoutDecorations(mapped);
            }
        };

        window.Xdg.Mapped += () =>
        {
            if (_manager.For(window.Surface) is { } plasma)
            {
                if (plasma.Focusable && plasma.Role == PlasmaShellRole.Normal)
                {
                    Activate(window.Surface);
                }

                return;
            }

            if (view is not null)
            {
                return;
            }

            var tree = new SceneTree(_placement.Windows);
            var scene = new SceneSurface(tree, window.Surface);
            view = new PlasmaHostView(window, tree, scene);
            if (window.RequestedFullscreen == true)
            {
                SetFullscreen(view, true);
            }
            else
            {
                Place(view);
            }
            SceneCreated?.Invoke(scene);
            _views.Insert(0, view);
            _owners[window.Surface] = view;
            Focus(view);
            SetDecorated(view, IsServerDecorated(window));
            ReportGeometry(view);
            ViewMapped?.Invoke(view);
        };
        window.Xdg.Committed += () =>
        {
            if (view is { } mapped)
            {
                ApplyResizeAnchor(mapped);
                LayoutDecorations(mapped);
                ReportGeometry(mapped);
            }
        };
        window.Xdg.Unmapped += () =>
        {
            if (view is { } gone)
            {
                gone.Tree.Enabled = false;
                _views.Remove(gone);
                _owners.Remove(window.Surface);
                gone.Frame?.Dispose();
                gone.Frame = null;
                gone.Shadow?.Dispose();
                gone.Shadow = null;
                if (!gone.Scene.IsDestroyed)
                {
                    gone.Scene.Destroy();
                }

                if (!gone.Tree.IsDestroyed)
                {
                    gone.Tree.Destroy();
                }

                view = null;
                RefocusTop();
            }
        };
        window.Destroyed += () =>
        {
            if (view is { } gone)
            {
                _views.Remove(gone);
                _owners.Remove(window.Surface);
                gone.Frame?.Dispose();
                gone.Frame = null;
                gone.Shadow?.Dispose();
                gone.Shadow = null;
                view = null;
                RefocusTop();
            }
        };
    }

    private void RecordDecorationPreference(Surface surface, bool serverSide)
    {
        if (_ssdPreference.TryAdd(surface, serverSide))
        {
            surface.Destroyed += () => _ssdPreference.Remove(surface);
        }
        else
        {
            _ssdPreference[surface] = serverSide;
        }

        if (_owners.TryGetValue(surface, out var view))
        {
            SetDecorated(view, serverSide);
        }
    }

    private bool IsServerDecorated(XdgToplevelWindow toplevel) =>
        _ssdPreference.TryGetValue(toplevel.Surface, out var serverSide)
            ? serverSide
            : _kdeDecorations?.ModeOf(toplevel.Surface) == Basin.Desktop.KdeServerDecorationManager.DecorationMode.Server ||
                _decorations?.ModeOf(toplevel) == DecorationMode.ServerSide;

    private void SetDecorated(PlasmaHostView view, bool decorated)
    {
        _source?.SetDecoration(view.Xdg, noBorder: !decorated, userCanSet: true);
        if (!decorated)
        {
            view.Frame?.Dispose();
            view.Frame = null;
            view.Shadow?.Dispose();
            view.Shadow = null;
            ReportGeometry(view);
            return;
        }

        if (view.Frame is not null || _frames is null)
        {
            return;
        }

        var frame = _frames.Create(view.Tree, view.Xdg.AppId ?? view.Xdg.Title ?? "toplevel");
        frame.Requested += action => OnFrameAction(view, action);
        view.Frame = frame;
        view.Shadow ??= _frames.Shadows.Create(view.Tree);
        LayoutDecorations(view);
        ReportGeometry(view);
    }

    public void LayoutDecorations(PlasmaHostView view)
    {
        LayoutShadow(view);
        if (view.Frame is not { } frame)
        {
            return;
        }

        var geometry = view.Xdg.Xdg.EffectiveGeometry;
        var visible = geometry.Width > 0 && geometry.Height > 0 && !view.Minimized &&
            !view.Xdg.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen);
        frame.Visible = visible;
        if (!visible)
        {
            return;
        }

        var scale = ScaleAt(view);
        if (!frame.HasPendingFor(geometry, scale))
        {
            frame.Configure(geometry, scale, BuildState(view));
        }

        frame.Commit();
    }

    private void LayoutShadow(PlasmaHostView view)
    {
        if (view.Shadow is not { } shadow)
        {
            return;
        }

        var geometry = view.Xdg.Xdg.EffectiveGeometry;
        var visible = geometry.Width > 0 && geometry.Height > 0 && !view.Minimized && !view.Maximized &&
            !view.Xdg.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen);
        shadow.Visible = visible;
        if (!visible)
        {
            return;
        }

        shadow.Texture = _frames?.Shadows.TextureFor(ScaleAt(view), view.Active);
        var insets = FrameInsetsOf(view);
        shadow.SetGeometry(new Box(
            geometry.X - insets.Left,
            geometry.Y - insets.Top,
            geometry.Width + insets.Left + insets.Right,
            geometry.Height + insets.Top + insets.Bottom));
    }

    private FrameState BuildState(PlasmaHostView view) => new()
    {
        Title = view.Xdg.Title,
        AppId = view.Xdg.AppId,
        Icon = new FrameIcon(view.IconName, null),
        Active = view.Active,
        Maximized = view.Maximized,
        Fullscreen = view.Xdg.HasState(Basin.Shell.Xdg.Protocol.XdgToplevel.State.Fullscreen),
        Resizing = view.Resizing,
        Capabilities = FrameCapabilities.WindowMenu | FrameCapabilities.Maximize | FrameCapabilities.Minimize,
        Palette = _palettes?.PaletteOf(view.Surface)?.Palette,
    };

    private double ScaleAt(PlasmaHostView view) =>
        _layout.OutputAt(view.Tree.X + 1, view.Tree.Y + 1)?.Scale ?? 1.0;

    private FrameInsets FrameInsetsOf(PlasmaHostView view) =>
        view.Frame?.Measure(BuildState(view), ScaleAt(view)) ?? default;

    private void OnFrameAction(PlasmaHostView view, FrameAction action)
    {
        switch (action.Kind)
        {
            case FrameActionKind.Close:
                view.Xdg.Close();
                break;
            case FrameActionKind.ToggleMaximize:
                SetMaximized(view, !view.Maximized);
                view.Xdg.RequestConfigure();
                break;
            case FrameActionKind.Minimize:
                Minimize(view, true);
                break;
            case FrameActionKind.Move:
                MoveGrabRequested?.Invoke(view, null);
                break;
            case FrameActionKind.Resize:
                ResizeGrabRequested?.Invoke(view, (ResizeEdges)action.Edges, null);
                break;
        }
    }

    public void Minimize(PlasmaHostView view, bool minimized)
    {
        if (view.Minimized == minimized)
        {
            return;
        }

        view.Minimized = minimized;
        view.Tree.Enabled = !minimized;
        _source?.SetMinimized(view.Xdg, minimized);
        if (minimized)
        {
            if (ReferenceEquals(FocusedView, view))
            {
                view.Xdg.SetActivated(false);
                SetFrameActive(view, false);
                _seat.Keyboard.NotifyClearFocus();
                RefocusTop();
            }
        }
        else
        {
            view.Tree.RaiseToTop();
            LayoutDecorations(view);
            Focus(view);
        }
    }

    public void MoveView(PlasmaHostView view, int x, int y)
    {
        view.Tree.SetPosition(x, y);
        ReportGeometry(view);
    }

    public void ResizeView(PlasmaHostView view, int x, int y, int width, int height, ResizeEdges edges)
    {
        view.ResizeAnchor = ResizeAnchor.For(edges, x, y, width, height);
        if (view.ResizeAnchor is null)
        {
            MoveView(view, x, y);
        }

        view.Xdg.SetSize(width, height);
        view.Xdg.RequestConfigure();
    }

    public void SetResizing(PlasmaHostView view, bool resizing)
    {
        view.Resizing = resizing;
        view.Xdg.SetResizing(resizing);
        view.Xdg.RequestConfigure();
        if (!resizing)
        {
            view.ResizeAnchor = null;
        }
    }

    private void ApplyResizeAnchor(PlasmaHostView view)
    {
        if (view.ResizeAnchor is not { } anchor)
        {
            return;
        }

        var (width, height) = view.GeometrySize();
        var (x, y) = anchor.PositionFor(width, height, view.Tree.X, view.Tree.Y);
        if (x != view.Tree.X || y != view.Tree.Y)
        {
            MoveView(view, x, y);
        }

        view.ResizeAnchor = ResizeAnchor.AfterCommit(view.ResizeAnchor, view.Resizing);
    }

    public Box FrameBoxOf(PlasmaHostView view)
    {
        var geometry = view.Xdg.Xdg.EffectiveGeometry;
        var client = new Box(
            view.Tree.X + geometry.X,
            view.Tree.Y + geometry.Y,
            Math.Max(geometry.Width, 1),
            Math.Max(geometry.Height, 1));
        if (view.Frame is null)
        {
            return client;
        }

        var insets = FrameInsetsOf(view);
        return new Box(
            client.X - insets.Left,
            client.Y - insets.Top,
            client.Width + insets.Left + insets.Right,
            client.Height + insets.Top + insets.Bottom);
    }

    private void ReportGeometry(PlasmaHostView view)
    {
        var geometry = view.Xdg.Xdg.EffectiveGeometry;
        var client = new Box(
            view.Tree.X + geometry.X,
            view.Tree.Y + geometry.Y,
            Math.Max(geometry.Width, 1),
            Math.Max(geometry.Height, 1));
        _source?.SetGeometry(view.Xdg, view.Frame is null ? client : FrameBoxOf(view), client);
    }

    private void RefocusTop()
    {
        foreach (var view in _views)
        {
            if (!view.Minimized)
            {
                Focus(view);
                return;
            }
        }
    }

    private void Place(PlasmaHostView view)
    {
        var usable = UsableAreaAt(_seat.Pointer.X, _seat.Pointer.Y);
        var (width, height) = view.GeometrySize();
        var offset = 30 * (_cascade++ % 8);
        var x = usable.X + Math.Max(0, (usable.Width - width) / 2) + offset;
        var y = usable.Y + Math.Max(0, (usable.Height - height) / 2) + offset;
        view.Tree.SetPosition(
            Math.Clamp(x, usable.X, Math.Max(usable.X, usable.X + usable.Width - width)),
            Math.Clamp(y, usable.Y, Math.Max(usable.Y, usable.Y + usable.Height - height)));
    }

    private Box UsableAreaAt(double x, double y)
    {
        if (_layout.OutputAt(x, y) is { } under)
        {
            return UsableArea(under);
        }

        foreach (var (output, _) in _layout.Outputs)
        {
            return UsableArea(output);
        }

        return _layout.Bounds;
    }

    public void SetMaximized(PlasmaHostView view, bool maximized)
    {
        view.Maximized = maximized;
        if (maximized)
        {
            ApplyMaximizedGeometry(view);
            view.Xdg.SetMaximized(true);
        }
        else
        {
            view.Xdg.SetSize(0, 0);
            view.Xdg.SetMaximized(false);
            Place(view);
        }
    }

    private void ApplyMaximizedGeometry(PlasmaHostView view)
    {
        var usable = UsableAreaAt(view.Tree.X, view.Tree.Y);
        var insets = FrameInsetsOf(view);
        view.Tree.SetPosition(usable.X + insets.Left, usable.Y + insets.Top);
        view.Xdg.SetSize(
            Math.Max(usable.Width - insets.Left - insets.Right, 1),
            Math.Max(usable.Height - insets.Top - insets.Bottom, 1));
    }

    private Box FullscreenBox(XdgToplevelWindow window, double fallbackX, double fallbackY)
    {
        var output = window.RequestedFullscreenOutput ?? _layout.OutputAt(fallbackX, fallbackY);
        return output is null ? _layout.Bounds : _layout.BoxOf(output);
    }

    private void SetFullscreen(PlasmaHostView view, bool fullscreen)
    {
        if (fullscreen)
        {
            var box = FullscreenBox(view.Xdg, view.Tree.X, view.Tree.Y);
            view.Tree.SetPosition(box.X, box.Y);
            view.Xdg.SetSize(box.Width, box.Height);
        }
        else
        {
            view.Xdg.SetSize(0, 0);
        }

        view.Xdg.SetFullscreen(fullscreen);
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (RootTreeOf(popup) is not { } parentTree)
        {
            return;
        }

        SceneCreated?.Invoke(_popups.Attach(popup, parentTree));
    }

    private SceneTree? RootTreeOf(XdgPopupWindow popup)
    {
        var parent = popup.Parent;
        while (parent is not null)
        {
            switch (parent.Role)
            {
                case XdgToplevelWindow toplevel:
                    if (_manager.For(toplevel.Surface) is { } plasma &&
                        _placement.SceneOf(plasma) is { } placed)
                    {
                        return placed.Tree;
                    }

                    return ViewOf(toplevel)?.Tree;
                case XdgPopupWindow parentPopup:
                    parent = parentPopup.Parent;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    private void WireLayerShell(XdgShell shell, LayerShell layerShell)
    {
        _layerDriver = new Basin.Desktop.LayerShellSceneDriver(
            layerShell, _layout, layer => _placement.TreeFor(layer.Layer));
        _layerDriver.TrackPopups(shell);
        _layerDriver.PopupSceneCreated += (_, _, scene) => SceneCreated?.Invoke(scene);
        _layerDriver.SceneCreated += (layer, scene) =>
        {
            if (layer.KeyboardInteractivity != Basin.Shell.Xdg.Protocol.ZwlrLayerSurfaceV1.KeyboardInteractivity.None)
            {
                _seat.Keyboard.NotifyEnter(layer.Surface);
            }

            SceneCreated?.Invoke(scene);
        };
        _layerDriver.UsableAreaChanged += (output, local) =>
        {
            var box = _layout.BoxOf(output);
            var usable = local with { X = box.X + local.X, Y = box.Y + local.Y };
            _usable[output] = usable.Intersect(_placement.UsableArea(output));
        };
        _layerDriver.Arranged += RemaximizeAll;
    }

    public void ArrangeLayerSurfaces() => _layerDriver.Rearrange();

    public void ClampIntoLayout()
    {
        foreach (var view in _views)
        {
            if (view.Maximized)
            {
                continue;
            }

            if (view.Xdg.RequestedFullscreen == true)
            {
                SetFullscreen(view, true);
                continue;
            }

            var frame = FrameBoxOf(view);
            var screen = ScreenFor(frame);
            var x = Math.Clamp(frame.X, screen.X, Math.Max(screen.X, screen.X + screen.Width - frame.Width));
            var y = Math.Clamp(frame.Y, screen.Y, Math.Max(screen.Y, screen.Y + screen.Height - frame.Height));
            if (x != frame.X || y != frame.Y)
            {
                MoveView(view, view.Tree.X + (x - frame.X), view.Tree.Y + (y - frame.Y));
            }
        }
    }

    private Box ScreenFor(in Box frame)
    {
        var chosen = _layout.Bounds;
        var best = -1;
        foreach (var (output, _) in _layout.Outputs)
        {
            var box = _layout.BoxOf(output);
            var overlap =
                Math.Max(0, Math.Min(box.X + box.Width, frame.X + frame.Width) - Math.Max(box.X, frame.X)) *
                Math.Max(0, Math.Min(box.Y + box.Height, frame.Y + frame.Height) - Math.Max(box.Y, frame.Y));
            if (overlap > best)
            {
                best = overlap;
                chosen = box;
            }
        }

        return chosen;
    }

    private void RecomputeUsable() => ArrangeLayerSurfaces();

    private void RemaximizeAll()
    {
        foreach (var view in _views)
        {
            if (view.Maximized)
            {
                ApplyMaximizedGeometry(view);
            }
        }
    }

}
