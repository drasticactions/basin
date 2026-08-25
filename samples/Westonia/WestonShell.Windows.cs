using Basin;
using Basin.Scene;
using Basin.Seat;
using Basin.Shell.Xdg;

namespace Westonia;

internal sealed partial class WestonShell
{
    private const int MoveSafety = 50;

    private readonly List<ShellWindow> _windows = [];
    private readonly Dictionary<XdgSurfaceState, SceneSurface> _surfaceScenes = [];
    private readonly ShellGrab _grab = new();
    private ShellWindow? _focused;

    public IReadOnlyList<ShellWindow> Windows => _windows;

    public ShellWindow? Focused => _focused;

    public ShellGrab Grab => _grab;

    public Seat? Seat { get; set; }

    public Func<IOutput?, Box>? WorkArea { get; set; }

    public Func<double, double, IOutput?>? OutputAt { get; set; }

    public Action? Repaint { get; set; }

    public Action? Restacked { get; set; }

    public void Attach(XdgShell shell)
    {
        shell.NewToplevel += OnNewToplevel;
        shell.NewPopup += OnNewPopup;
    }

    public ShellWindow? WindowOwning(Surface surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            foreach (var window in _windows)
            {
                if (ReferenceEquals(candidate, window.Surface))
                {
                    return window;
                }
            }
        }

        return null;
    }

    public void Focus(ShellWindow? window)
    {
        if (window is null || _layers.IsLocked)
        {
            return;
        }

        if (ReferenceEquals(window, _focused))
        {
            window.Tree.RaiseToTop();
            Restacked?.Invoke();
            return;
        }

        if (_focused is { } previous && !previous.Window.Xdg.Surface.IsDestroyed)
        {
            previous.Window.SetActivated(false);
            previous.Frame?.SetActive(false);
        }

        _focused = window;
        window.Tree.RaiseToTop();
        _windows.Remove(window);
        _windows.Insert(0, window);
        Restacked?.Invoke();
        window.Window.SetActivated(true);
        window.Frame?.SetActive(true);
        KeyboardTarget = null;
        Seat?.Keyboard.NotifyEnter(window.Surface);
    }

    public void FocusNext()
    {
        if (_windows.Count < 2)
        {
            return;
        }

        var mapped = _windows.Where(w => w.Kind != ShellWindowKind.Minimized && w.Window.IsMapped).ToList();
        if (mapped.Count < 2)
        {
            return;
        }

        var index = _focused is null ? 0 : mapped.IndexOf(_focused);
        Focus(mapped[(index + 1) % mapped.Count]);
    }

    public void Kill(ShellWindow? window)
    {
        if (window is null)
        {
            return;
        }

        window.Window.Surface.Resource.Client.Destroy();
    }

    public void ToggleMaximized(ShellWindow? window)
    {
        if (window is not null)
        {
            SetMaximized(window, !window.Maximized);
        }
    }

    public void SetMaximized(ShellWindow? window, bool maximized)
    {
        if (window is null || window.Maximized == maximized)
        {
            return;
        }

        if (maximized)
        {
            SaveRestore(window);
            window.Maximized = true;
            window.Window.SetMaximized(true);
            ApplyMaximized(window);
        }
        else
        {
            window.Maximized = false;
            window.Window.SetMaximized(false);
            Restore(window);
        }

        window.Window.RequestConfigure();
        Repaint?.Invoke();
    }

    private void ApplyMaximized(ShellWindow window)
    {
        var area = WorkArea?.Invoke(window.Output ?? PlacementOutput()) ?? new Box(0, 0, 800, 600);
        window.Window.SetSize(area.Width, area.Height);
        window.MoveTo(area.X, area.Y);
    }

    private static void SaveRestore(ShellWindow window)
    {
        var geometry = window.Geometry;
        window.Restore = window.Restore.Saving(new Box(window.X, window.Y, geometry.Width, geometry.Height));
    }

    private void Restore(ShellWindow window)
    {
        if (!window.Restore.TryGet(out var saved))
        {
            var area = WorkArea?.Invoke(window.Output) ?? new Box(0, 0, 800, 600);
            var width = area.Width / 2;
            var height = area.Height / 2;
            saved = new Box(
                area.X + ((area.Width - width) / 2), area.Y + ((area.Height - height) / 2), width, height);
        }

        window.Restore = RestoreGeometry.None;
        window.Window.SetSize(saved.Width, saved.Height);
        window.MoveTo(saved.X, saved.Y);
    }

    public void ToggleFullscreen(ShellWindow? window)
    {
        if (window is not null)
        {
            SetFullscreen(window, !window.Fullscreen);
        }
    }

    public void SetFullscreen(ShellWindow? window, bool fullscreen)
    {
        if (window is null || window.Fullscreen == fullscreen)
        {
            return;
        }

        if (fullscreen)
        {
            SaveRestore(window);
            window.Fullscreen = true;
            window.Kind = ShellWindowKind.Fullscreen;
            window.Window.SetFullscreen(true);
            ApplyFullscreen(window);
        }
        else
        {
            window.Fullscreen = false;
            window.Kind = ShellWindowKind.Normal;
            if (window.Curtain is { IsDestroyed: false } curtain)
            {
                curtain.Destroy();
            }

            window.Curtain = null;
            window.Window.SetFullscreen(false);
            window.Tree.Reparent(WorkspaceTreeOf?.Invoke(window) ?? _layers.Workspaces);
            Restacked?.Invoke();
            if (window.Maximized)
            {
                ApplyMaximized(window);
            }
            else
            {
                Restore(window);
            }
        }

        window.Window.RequestConfigure();
        Repaint?.Invoke();
    }

    private void ApplyFullscreen(ShellWindow window)
    {
        var box = OutputBox(window);
        window.Window.SetSize(box.Width, box.Height);
        window.Tree.Reparent(_layers.Fullscreen);
        Restacked?.Invoke();
        var curtain = window.Curtain is { IsDestroyed: false } existing
            ? existing
            : new SceneRect(_layers.Fullscreen, box.Width, box.Height, new RenderColor(0f, 0f, 0f, 1f));
        curtain.Width = box.Width;
        curtain.Height = box.Height;
        curtain.SetPosition(box.X, box.Y);
        curtain.LowerToBottom();
        window.Curtain = curtain;
        window.MoveTo(box.X, box.Y);
    }

    public void SetTiledOrientation(ShellWindow? window, ResizeEdges edges)
    {
        if (window is null)
        {
            return;
        }

        var area = WorkArea?.Invoke(window.Output) ?? new Box(0, 0, 800, 600);
        var halfWidth = area.Width / 2;
        var halfHeight = area.Height / 2;
        var box = edges switch
        {
            ResizeEdges.Left => new Box(area.X, area.Y, halfWidth, area.Height),
            ResizeEdges.Right => new Box(area.X + halfWidth, area.Y, area.Width - halfWidth, area.Height),
            ResizeEdges.Top => new Box(area.X, area.Y, area.Width, halfHeight),
            ResizeEdges.Bottom => new Box(area.X, area.Y + halfHeight, area.Width, area.Height - halfHeight),
            _ => area,
        };

        window.Tiled = edges;
        window.Window.SetTiled(edges);
        window.Window.SetSize(box.Width, box.Height);
        window.Window.RequestConfigure();
        window.MoveTo(box.X, box.Y);
        Repaint?.Invoke();
    }

    public void BeginMove(ShellWindow window, double pointerX, double pointerY, bool clientInitiated)
    {
        if (window.Maximized || window.Fullscreen)
        {
            return;
        }

        if (window.Tiled != ResizeEdges.None)
        {
            window.Tiled = ResizeEdges.None;
            window.Window.SetTiled(ResizeEdges.None);
            window.Window.RequestConfigure();
        }

        _grab.Kind = ShellGrabKind.Move;
        _grab.Window = window;
        _grab.StartX = pointerX;
        _grab.StartY = pointerY;
        _grab.OriginX = window.X;
        _grab.OriginY = window.Y;
        _grab.ClientInitiated = clientInitiated;
        Client?.GrabCursor(Basin.Shell.Weston.ShellGrabCursor.Move);
    }

    public void BeginResize(ShellWindow window, ResizeEdges edges, double pointerX, double pointerY)
    {
        if (window.Maximized || window.Fullscreen)
        {
            return;
        }

        var geometry = window.Geometry;
        _grab.Kind = ShellGrabKind.Resize;
        _grab.Window = window;
        _grab.Edges = edges == ResizeEdges.None ? ResizeEdges.BottomRight : edges;
        _grab.StartX = pointerX;
        _grab.StartY = pointerY;
        _grab.OriginX = window.X;
        _grab.OriginY = window.Y;
        _grab.OriginWidth = geometry.Width;
        _grab.OriginHeight = geometry.Height;
        window.ResizeAnchor = new ResizeAnchor(_grab.Edges, window.X + geometry.Width, window.Y + geometry.Height);
        window.Resizing = true;
        window.Window.SetResizing(true);
        Client?.GrabCursor(CursorFor(_grab.Edges));
    }

    public bool UpdateGrab(double pointerX, double pointerY)
    {
        if (!_grab.Active || _grab.Window is not { } window)
        {
            return false;
        }

        var dx = pointerX - _grab.StartX;
        var dy = pointerY - _grab.StartY;

        switch (_grab.Kind)
        {
            case ShellGrabKind.Move:
                window.MoveTo(
                    _grab.OriginX + (int)dx,
                    ConstrainY(window, _grab.OriginY + (int)dy, _grab.ClientInitiated));
                Repaint?.Invoke();
                return true;
            case ShellGrabKind.Resize:
                {
                    var width = _grab.OriginWidth;
                    var height = _grab.OriginHeight;
                    if (_grab.Edges.HasFlag(ResizeEdges.Right))
                    {
                        width = Math.Max(1, _grab.OriginWidth + (int)dx);
                    }
                    else if (_grab.Edges.HasFlag(ResizeEdges.Left))
                    {
                        width = Math.Max(1, _grab.OriginWidth - (int)dx);
                    }

                    if (_grab.Edges.HasFlag(ResizeEdges.Bottom))
                    {
                        height = Math.Max(1, _grab.OriginHeight + (int)dy);
                    }
                    else if (_grab.Edges.HasFlag(ResizeEdges.Top))
                    {
                        height = Math.Max(1, _grab.OriginHeight - (int)dy);
                    }

                    window.Window.SetSize(width, height);
                    window.Window.RequestConfigure();
                    return true;
                }

            default:
                return false;
        }
    }

    public void EndGrab()
    {
        if (_grab.Window is { } window && _grab.Kind == ShellGrabKind.Resize)
        {
            window.Resizing = false;
            window.Window.SetResizing(false);
            window.Window.RequestConfigure();
        }

        if (_grab.Active)
        {
            Client?.GrabCursor(Basin.Shell.Weston.ShellGrabCursor.Arrow);
        }

        _grab.Clear();
    }

    private static void ApplyResizeAnchor(ShellWindow window)
    {
        if (window.ResizeAnchor is not { } anchor)
        {
            return;
        }

        var geometry = window.Geometry;
        var (x, y) = anchor.PositionFor(geometry.Width, geometry.Height, window.X, window.Y);
        if (x != window.X || y != window.Y)
        {
            window.MoveTo(x, y);
        }

        window.ResizeAnchor = ResizeAnchor.AfterCommit(window.ResizeAnchor, window.Resizing);
    }

    private int ConstrainY(ShellWindow window, int y, bool clientInitiated)
    {
        if (_ini.Shell.PanelPosition != PanelPosition.Top)
        {
            return y;
        }

        var area = WorkArea?.Invoke(window.Output);
        if (area is not { } work)
        {
            return y;
        }

        var above = window.Frame is null ? 0 : ShellFrame.TitlebarHeight;
        var below = window.Frame is null ? 0 : ShellFrame.BorderWidth;
        var height = window.Geometry.Height + above + below;

        if (y - above + height - MoveSafety < work.Y)
        {
            y = work.Y + MoveSafety - height + above;
        }

        if (clientInitiated && y - above < work.Y)
        {
            y = work.Y + above;
        }

        return y;
    }

    private static Basin.Shell.Weston.ShellGrabCursor CursorFor(ResizeEdges edges) => edges switch
    {
        ResizeEdges.Top => Basin.Shell.Weston.ShellGrabCursor.ResizeTop,
        ResizeEdges.Bottom => Basin.Shell.Weston.ShellGrabCursor.ResizeBottom,
        ResizeEdges.Left => Basin.Shell.Weston.ShellGrabCursor.ResizeLeft,
        ResizeEdges.Right => Basin.Shell.Weston.ShellGrabCursor.ResizeRight,
        ResizeEdges.TopLeft => Basin.Shell.Weston.ShellGrabCursor.ResizeTopLeft,
        ResizeEdges.TopRight => Basin.Shell.Weston.ShellGrabCursor.ResizeTopRight,
        ResizeEdges.BottomLeft => Basin.Shell.Weston.ShellGrabCursor.ResizeBottomLeft,
        ResizeEdges.BottomRight => Basin.Shell.Weston.ShellGrabCursor.ResizeBottomRight,
        _ => Basin.Shell.Weston.ShellGrabCursor.Arrow,
    };

    private Box OutputBox(ShellWindow window) =>
        (window.Output ?? PlacementOutput()) is { } output && OutputBoxOf is { } lookup
            ? lookup(output)
            : new Box(0, 0, 1280, 720);

    public Func<IOutput, Box>? OutputBoxOf { get; set; }

    public Func<ShellWindow, ShellFrame?>? FrameFactory { get; set; }

    public Func<IOutput?, double>? ScaleOf { get; set; }

    public void RescaleFrames()
    {
        foreach (var window in _windows)
        {
            var scale = ScaleOf?.Invoke(window.Output) ?? 1.0;
            if (Math.Abs(scale - window.Scale) < double.Epsilon)
            {
                continue;
            }

            window.Scale = scale;
            window.Frame?.Update(scale);
        }
    }

    public Func<SceneTree>? WorkspaceTree { get; set; }

    public Func<ShellWindow, SceneTree>? WorkspaceTreeOf { get; set; }

    public Action<ShellWindow>? WorkspaceAdopt { get; set; }

    public Action<ShellWindow>? Mapped { get; set; }

    public Action<ShellWindow>? Unmapping { get; set; }

    private void OnNewToplevel(XdgToplevelWindow window)
    {
        var container = new SceneTree(WorkspaceTree?.Invoke() ?? _layers.Workspaces) { Enabled = false };
        var scene = new SceneSurface(container, window.Surface);
        _surfaceScenes[window.Xdg] = scene;
        var shellWindow = new ShellWindow(window, container, scene);

        window.Xdg.Mapped += () =>
        {
            container.Enabled = true;
            _windows.Insert(0, shellWindow);
            Align();
            WorkspaceAdopt?.Invoke(shellWindow);
            Place(shellWindow);
            shellWindow.Frame = FrameFactory?.Invoke(shellWindow);
            Focus(shellWindow);
            Mapped?.Invoke(shellWindow);
            Repaint?.Invoke();
        };

        window.Xdg.Unmapped += () =>
        {
            if (ReferenceEquals(_grab.Window, shellWindow))
            {
                EndGrab();
            }

            Unmapping?.Invoke(shellWindow);
            if (shellWindow.Curtain is { IsDestroyed: false } curtain)
            {
                curtain.Destroy();
                shellWindow.Curtain = null;
            }

            container.Enabled = false;
            shellWindow.Frame?.Dispose();
            shellWindow.Frame = null;
            _windows.Remove(shellWindow);
            if (ReferenceEquals(_focused, shellWindow))
            {
                _focused = null;
                Focus(_windows.FirstOrDefault(w => w.Window.IsMapped));
            }
        };

        void Align()
        {
            var geometry = window.Xdg.EffectiveGeometry;
            scene.Tree.SetPosition(-geometry.X, -geometry.Y);
        }

        Align();
        window.TitleChanged += () => shellWindow.Frame?.SetTitle(window.Title);
        window.Xdg.Committed += () =>
        {
            Align();
            ApplyResizeAnchor(shellWindow);
            shellWindow.Frame?.Update(shellWindow.Scale);
        };

        window.Destroyed += () =>
        {
            shellWindow.Frame?.Dispose();
            shellWindow.Frame = null;
            _surfaceScenes.Remove(window.Xdg);
            _windows.Remove(shellWindow);
            if (!container.IsDestroyed)
            {
                container.Destroy();
            }

            if (ReferenceEquals(_focused, shellWindow))
            {
                _focused = null;
            }
        };

        window.MoveRequested += _ =>
        {
            if (PointerPosition is { } pointer)
            {
                BeginMove(shellWindow, pointer.X, pointer.Y, clientInitiated: true);
            }
        };

        window.ResizeRequested += (_, edges) =>
        {
            if (PointerPosition is { } pointer)
            {
                BeginResize(shellWindow, edges, pointer.X, pointer.Y);
            }
        };

        window.MaximizeRequested += maximized => SetMaximized(shellWindow, maximized);
        window.FullscreenRequested += fullscreen => SetFullscreen(shellWindow, fullscreen);
        window.MinimizeRequested += () => Minimize(shellWindow);
    }

    public Func<(double X, double Y)?>? PointerLocator { get; set; }

    private (double X, double Y)? PointerPosition => PointerLocator?.Invoke();

    public void Minimize(ShellWindow window)
    {
        window.Kind = ShellWindowKind.Minimized;
        window.Tree.Reparent(_layers.Minimized);
        Restacked?.Invoke();
        if (ReferenceEquals(_focused, window))
        {
            _focused = null;
            Focus(_windows.FirstOrDefault(w => w.Kind != ShellWindowKind.Minimized && w.Window.IsMapped));
        }

        Repaint?.Invoke();
    }

    private IOutput? PlacementOutput()
    {
        var pointer = PointerPosition;
        return pointer is { } position ? OutputAt?.Invoke(position.X, position.Y) : null;
    }

    private void Place(ShellWindow window)
    {
        var output = PlacementOutput();
        window.Output = output;
        window.Scale = ScaleOf?.Invoke(output) ?? 1.0;

        if (window.Fullscreen)
        {
            ApplyFullscreen(window);
            return;
        }

        if (window.Maximized)
        {
            ApplyMaximized(window);
            return;
        }

        var area = WorkArea?.Invoke(output) ?? new Box(0, 0, 1280, 720);
        var geometry = window.Window.Xdg.EffectiveGeometry;
        var x = area.X + Math.Max(0, (area.Width - geometry.Width) / 2);
        var y = area.Y + Math.Max(0, (area.Height - geometry.Height) / 2);
        window.MoveTo(x, y);
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (popup.Parent is not { } parent || !_surfaceScenes.TryGetValue(parent, out var parentScene))
        {
            return;
        }

        var scene = new SceneSurface(parentScene.Tree, popup.Surface);
        _surfaceScenes[popup.Xdg] = scene;

        void Place()
        {
            var origin = parent.EffectiveGeometry;
            var offset = popup.SurfacePosition;
            scene.Tree.SetPosition(origin.X + offset.X, origin.Y + offset.Y);
        }

        void Constrain()
        {
            var xdg = popup.Parent;
            while (xdg?.Role is XdgPopupWindow parentPopup)
            {
                xdg = parentPopup.Parent;
            }

            if (xdg?.Role is not XdgToplevelWindow toplevel ||
                WindowOwning(toplevel.Surface)?.Output is not { } output ||
                OutputPlacement?.Invoke(output) is not { } box ||
                !parentScene.Tree.TryMapSceneToLocal(0, 0, out var localX, out var localY))
            {
                return;
            }

            var origin = parent.EffectiveGeometry;
            var originX = (int)-localX + origin.X;
            var originY = (int)-localY + origin.Y;
            popup.Unconstrain(new Box(box.X - originX, box.Y - originY, box.Width, box.Height));
        }

        Constrain();
        Place();
        popup.Xdg.Committed += Place;
        popup.GeometryChanged += Place;
        popup.Repositioned += Constrain;
        popup.Destroyed += () =>
        {
            popup.Xdg.Committed -= Place;
            popup.GeometryChanged -= Place;
            popup.Repositioned -= Constrain;
            _surfaceScenes.Remove(popup.Xdg);
        };
    }
}
