using Basin;
using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;
using Microsoft.Extensions.Logging;

namespace EightWm;

internal sealed partial class Shell
{
    internal const long CloseGraceMillis = 10_000;

    private readonly CloseQueue _closing = new() { GraceMillis = CloseGraceMillis };

    private FractionalScaleManager? _scales;
    private readonly List<SurfaceBox> _presence = [];
    private bool _scalesStale = true;

    private void AttachScales()
    {
        _scales = _services.Find<FractionalScaleManager>();
        if (_scales is null || _services.Find<CompositorGlobal>() is not { } compositor)
        {
            return;
        }

        compositor.SurfaceCreated += surface => _scales.AnnounceScale(surface, MaxScale());
    }

    private double MaxScale()
    {
        var scale = 1.0;
        foreach (var view in Views)
        {
            scale = Math.Max(scale, view.Output.Scale);
        }

        return scale;
    }

    internal void AnnounceScales()
    {
        _scalesStale = false;
        if (_scales is null)
        {
            return;
        }

        _scene.CollectSurfaces(_presence);
        foreach (var (surface, box) in _presence)
        {
            var home = OwnerOf(surface) is { } app ? HomeOf(app) : ViewOver(box);
            foreach (var view in Views)
            {
                surface.SetOutputPresence(view.Global, ReferenceEquals(view, home));
            }

            _scales.AnnounceScale(surface, home.Output.Scale);
        }
    }

    private ShellView ViewOver(in Box box)
    {
        foreach (var view in Views)
        {
            var outputBox = _layout.BoxOf(view.Output);
            if (box.X < outputBox.Right && box.Right > outputBox.X &&
                box.Y < outputBox.Bottom && box.Bottom > outputBox.Y)
            {
                return view;
            }
        }

        return PrimaryView;
    }

    private void AttachShell()
    {
        Shells.NewToplevel += OnNewToplevel;
        Shells.NewPopup += OnNewPopup;
        _services.Require<XdgToplevelSource>().ActivateRequested += window =>
        {
            if (AppOf(window) is { } app)
            {
                Show(app);
            }
        };
    }

    internal ShellView PrimaryView => Views.Count > 0 ? Views[0] : throw new InvalidOperationException("no output");

    internal ShellView HomeOf(AppWindow app) => _homes.TryGetValue(app, out var view) ? view : PrimaryView;

    internal ShellView ViewAt(double x, double y)
    {
        foreach (var view in Views)
        {
            var box = view.Box;
            if (x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom)
            {
                return view;
            }
        }

        return PrimaryView;
    }

    internal AppWindow? OwnerOf(Surface? surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            if (_owners.TryGetValue(candidate, out var app))
            {
                return app;
            }
        }

        return null;
    }

    internal AppWindow? Focused
    {
        get
        {
            if (Seat.Keyboard.Focus is not { } focus)
            {
                return null;
            }

            foreach (var app in _apps)
            {
                if (ReferenceEquals(app.Surface, focus))
                {
                    return app;
                }
            }

            return null;
        }
    }

    private AppWindow? AppOf(XdgToplevelWindow window)
    {
        foreach (var app in _apps)
        {
            if (ReferenceEquals(app.Xdg, window))
            {
                return app;
            }
        }

        return null;
    }

    private void OnNewToplevel(XdgToplevelWindow window)
    {
        var slot = new SceneTree(_scene.Root) { Enabled = false };
        var frame = new SceneTransform(slot);
        var scene = new SceneSurface(frame, window.Surface);
        var app = new AppWindow(window, slot, frame, scene);
        scene.Destroyed += () =>
        {
            if (!slot.IsDestroyed)
            {
                slot.Destroy();
            }
        };
        var configured = false;

        window.WmCapabilities = XdgWmCapabilities.Fullscreen;

        window.Configuring += () =>
        {
            if (configured)
            {
                return;
            }

            configured = true;
            var view = HomeOf(app);
            if (app.IsTransient)
            {
                app.PlaceCentered(CellOfParent(app, view));
            }
            else
            {
                app.PlaceInCell(AppArea(view));
            }
        };

        window.FullscreenRequested += _ => window.RequestConfigure();
        window.MaximizeRequested += _ => window.RequestConfigure();
        window.MinimizeRequested += () => window.RequestConfigure();

        window.Xdg.Mapped += () => Map(app);
        window.Xdg.Unmapped += () => Unmap(app);
        window.Destroyed += () =>
        {
            Unmap(app);
            _closing.Forget(app);
        };
    }

    internal ShellView PlacementView()
    {
        if (Views.Count == 0)
        {
            throw new InvalidOperationException("no output");
        }

        foreach (var view in Views)
        {
            if (view.StartVisible)
            {
                return view;
            }
        }

        if (Focused is { } focused && _homes.TryGetValue(focused, out var home))
        {
            return home;
        }

        return Views[Math.Clamp(StartOutputNow, 0, Views.Count - 1)];
    }

    private void Map(AppWindow app)
    {
        var view = PlacementView();
        _homes[app] = view;
        _apps.Add(app);
        ApplyRules(app);
        if (app.Surface is { } surface)
        {
            _owners[surface] = app;
        }

        app.Slot.Reparent(app.IsTransient ? view.Transients : view.Apps);
        app.Slot.Enabled = true;
        _capture.Stack.RaiseChanged();

        if (app.IsTransient)
        {
            app.PlaceCentered(CellOfParent(app, view));
            view.Dim.Enabled = true;
        }
        else
        {
            view.Host.Replace(app);
            view.StartVisible = false;
            Relayout(view);
            Animate(app, Animation.EnterPage, offsetScale: view.Scale);
            DismissSplash(view, crossFade: true);
        }

        Focus(app);
        Console.WriteLine($"APP + {app.AppId} {app.Title}");
    }

    private void Unmap(AppWindow app)
    {
        if (!_apps.Remove(app))
        {
            return;
        }

        if (ReferenceEquals(DraggedTitle, app))
        {
            TitleCancel();
        }

        if (ReferenceEquals(DraggedEntry, app))
        {
            RailCancel();
        }

        if (app.Surface is { } surface)
        {
            _owners.Remove(surface);
        }

        var view = HomeOf(app);
        _homes.Remove(app);
        view.Host.Forget(app);
        if (!app.Slot.IsDestroyed)
        {
            app.Slot.Enabled = false;
        }

        if (view.Host.IsEmpty)
        {
            view.StartVisible = true;
        }
        else if (view.Host.Active is { } next)
        {
            Focus(next);
        }

        Relayout(view);
        Console.WriteLine($"APP - {app.AppId}");
    }

    private Box CellOfParent(AppWindow app, ShellView view)
    {
        if (app.Xdg?.Parent is { } parent && AppOf(parent) is { } owner && !owner.Cell.IsEmpty)
        {
            return owner.Cell;
        }

        return AppArea(view);
    }

    internal Box AppArea(ShellView view)
    {
        var box = view.Box;
        var usable = view.UsableArea;
        return usable.Width > 0 && usable.Height > 0
            ? usable
            : new Box(0, 0, box.Width, box.Height);
    }

    private static readonly RenderColor RailColor = new(0.10f, 0.10f, 0.10f, 1f);
    private static readonly RenderColor RailGrip = new(0.55f, 0.55f, 0.55f, 1f);

    internal void Relayout(ShellView view)
    {
        view.Host.MinWidth = MinWidthNow;
        view.Host.Layout(AppArea(view), view.IsPortrait);
        if (view.StartVisible)
        {
            ParkCells(view);
        }

        view.Apps.Enabled = !view.Host.IsEmpty;
        view.Background.Enabled = view.StartVisible || view.Host.IsEmpty;
        if (view.Start is { } start)
        {
            start.Enabled = view.Background.Enabled;
        }

        view.Dim.Enabled = DimWanted(view);
        _scalesStale = true;
        SyncVacancy(view);
        SyncRails(view);
    }

    private static readonly RenderColor PreviewColor = new(0.35f, 0.35f, 0.35f, 0.35f);

    internal void ShowPreview(ShellView view, in Box box)
    {
        if (box.IsEmpty)
        {
            HidePreview(view);
            return;
        }

        var fill = view.PreviewFill ??= new SceneRect(view.Preview, 1, 1, PreviewColor);
        fill.Width = box.Width;
        fill.Height = box.Height;
        fill.SetPosition(box.X, box.Y);
        view.Preview.Enabled = true;
    }

    internal static void HidePreview(ShellView view) => view.Preview.Enabled = false;

    internal Box LandingBox(ShellView view, AppWindow app, int at)
    {
        if (view.Host.TryMeasureSplit(app, at, 0.5, out var box))
        {
            return box;
        }

        return view.Host.Active is { Cell.IsEmpty: false } active ? active.Cell : AppArea(view);
    }

    private void SyncVacancy(ShellView view)
    {
        var box = view.Host.VacantArea;
        var wanted = view.Host.HasVacancy && !view.StartVisible && !box.IsEmpty;
        view.Vacant.Enabled = wanted;
        if (!wanted)
        {
            return;
        }

        var colour = DesktopColor;
        var fill = view.VacantFill ??= new SceneRect(view.Vacant, 1, 1, colour);
        fill.Color = colour;
        fill.Width = box.Width;
        fill.Height = box.Height;
        fill.SetPosition(box.X, box.Y);
    }

    private RenderColor DesktopColor
    {
        get
        {
            var packed = _config.Background;
            return new RenderColor(
                ((packed >> 16) & 0xff) / 255f,
                ((packed >> 8) & 0xff) / 255f,
                (packed & 0xff) / 255f,
                ((packed >> 24) & 0xff) / 255f);
        }
    }

    private static void SyncRails(ShellView view)
    {
        var wanted = Math.Max(0, view.Host.SlotCount - 1) * 2;
        while (view.Splitters.Count < wanted)
        {
            view.Splitters.Add(new SceneRect(view.Rails, 1, 1, RailColor));
        }

        while (view.Splitters.Count > wanted)
        {
            view.Splitters[^1].Destroy();
            view.Splitters.RemoveAt(view.Splitters.Count - 1);
        }

        for (var i = 0; i < wanted / 2; i++)
        {
            var box = view.Host.GutterBox(i);
            var rail = view.Splitters[i * 2];
            rail.Color = RailColor;
            rail.Width = box.Width;
            rail.Height = box.Height;
            rail.SetPosition(box.X, box.Y);

            var grip = view.Splitters[(i * 2) + 1];
            grip.Color = RailGrip;
            if (view.Host.Portrait)
            {
                grip.Width = 48;
                grip.Height = 2;
                grip.SetPosition(box.X + ((box.Width - 48) / 2), box.Y + ((box.Height - 2) / 2));
            }
            else
            {
                grip.Width = 2;
                grip.Height = 48;
                grip.SetPosition(box.X + ((box.Width - 2) / 2), box.Y + ((box.Height - 48) / 2));
            }
        }

        view.Rails.Enabled = SplittersLive(view);
    }

    internal const int SplitterSlop = 8;

    internal static bool SplittersLive(ShellView view) =>
        !view.StartVisible && view.Host.SlotCount > 1;

    internal bool BeginSplitDrag(ShellView view, double localX, double localY)
    {
        if (!SplittersLive(view))
        {
            return false;
        }

        var splitter = view.Host.SplitterAt(localX, localY, SplitterSlop);
        if (splitter < 0)
        {
            return false;
        }

        view.DraggingSplitter = splitter;
        return true;
    }

    internal void DragSplitter(ShellView view, double localX, double localY)
    {
        if (view.DraggingSplitter < 0)
        {
            return;
        }

        var position = (int)Math.Round(view.Host.Portrait ? localY : localX) - (view.Host.Gutter / 2);
        if (view.Host.TrySetSplit(view.DraggingSplitter, position))
        {
            Relayout(view);
        }
    }

    internal void EndSplitDrag(ShellView view) => view.DraggingSplitter = -1;

    internal bool Snap(AppWindow app, ShellView view, int at, double fraction = 0.5)
    {
        var held = view.Host.Holds(app);
        if (held)
        {
            view.Host.Eject(app);
            Relayout(view);
        }

        if (!view.Host.TrySplit(app, at, fraction))
        {
            if (held)
            {
                view.Host.Replace(app);
                Relayout(view);
            }

            return false;
        }

        _homes[app] = view;
        app.Slot.Reparent(view.Apps);
        app.Slot.Enabled = true;
        view.StartVisible = false;
        Relayout(view);
        Focus(app);
        return true;
    }

    private static void ParkCells(ShellView view)
    {
        var cells = view.Host.Cells;
        for (var i = 0; i < cells.Count; i++)
        {
            cells[i].Hidden();
        }
    }

    private bool AnyTransient(ShellView view)
    {
        foreach (var app in _apps)
        {
            if (app.IsTransient && ReferenceEquals(HomeOf(app), view))
            {
                return true;
            }
        }

        return false;
    }

    internal void RelayoutAll()
    {
        foreach (var view in Views)
        {
            Relayout(view);
        }
    }

    internal void Focus(AppWindow? app)
    {
        if (app is null || !app.WantsFocus || app.Surface is not { } surface)
        {
            return;
        }

        if (Focused is { } previous && !ReferenceEquals(previous, app))
        {
            previous.SetActivated(false);
        }

        app.SetActivated(true);
        app.Slot.RaiseToTop();
        _capture.Stack.RaiseChanged();
        HomeOf(app).Host.Activate(app);
        Seat.Keyboard.NotifyEnter(surface);
        _seat.Refocus();
    }

    internal void Show(AppWindow app)
    {
        var view = HomeOf(app);
        if (!view.Host.Holds(app))
        {
            view.Host.Replace(app);
        }

        view.StartVisible = false;
        Relayout(view);
        Focus(app);
    }

    internal void ToggleStart(ShellView view)
    {
        CloseOtherChrome(view, ChromePanel.Switcher);
        if (view.StartVisible && view.Host.Mru.Count == 0)
        {
            return;
        }

        if (view.StartVisible)
        {
            var last = view.Host.Mru[0];
            view.StartVisible = false;
            if (!view.Host.Holds(last))
            {
                view.Host.Replace(last);
            }

            Relayout(view);
            Animate(last, Animation.EnterPage, offsetScale: view.Scale);
            Focus(last);
            return;
        }

        view.StartVisible = true;
        Relayout(view);
        Animate(ref view.StartMotion, view.BackgroundFrame, Animation.EnterPage, offsetScale: view.Scale);
        Console.WriteLine("START on");
    }

    internal void CloseFocused()
    {
        if (Focused is { } app)
        {
            CloseApp(app);
        }
    }

    internal void CloseApp(AppWindow app)
    {
        if (!_closing.Request(app, Environment.TickCount64))
        {
            return;
        }

        app.Closing = true;
        var view = HomeOf(app);
        view.Host.Eject(app);
        view.Host.Forget(app);
        view.Switcher?.Forget(app);
        Relayout(view);
        Console.WriteLine($"CLOSE {app.AppId}");
    }

    private readonly List<IClosable> _killScratch = [];

    private void ExpireCloseTimers()
    {
        if (_closing.Count == 0 || _closing.Expire(Environment.TickCount64, _killScratch) == 0)
        {
            return;
        }

        foreach (var closable in _killScratch)
        {
            if (closable is AppWindow app)
            {
                Kill(app);
            }
        }
    }

    private void Kill(AppWindow app)
    {
        if (!app.IsAttributable)
        {
            _log.LogDebug("no pid for the X11 window {AppId}; it is not killed", app.AppId);
            return;
        }

        var pid = app.Pid;
        if (pid <= 0)
        {
            _log.LogDebug("no credentials for {AppId}; it is not killed", app.AppId);
            return;
        }

        _log.LogDebug(
            "{AppId} did not close in {Grace}ms; killing pid {Pid}", app.AppId, _closing.GraceMillis, pid);
        try
        {
            System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: false);
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _log.LogDebug("pid {Pid} was gone already", pid);
        }
    }

    internal void AttachXWayland(Basin.XWayland.XWaylandWm wm)
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

        var slot = new SceneTree(_scene.Root);
        var frame = new SceneTransform(slot);
        var scene = new SceneSurface(frame, surface);
        var app = new AppWindow(window, slot, frame, scene);

        if (!managed)
        {
            slot.Reparent(PlacementView().Apps);
            slot.SetPosition(window.X, window.Y);
            _owners[surface] = app;
            void Drop()
            {
                _owners.Remove(surface);
                if (!scene.IsDestroyed)
                {
                    scene.Destroy();
                }

                if (!slot.IsDestroyed)
                {
                    slot.Destroy();
                }
            }

            window.Unmapped += Drop;
            window.Destroyed += Drop;
            return;
        }

        Map(app);
        void Cleanup()
        {
            Unmap(app);
            _closing.Forget(app);
            if (!scene.IsDestroyed)
            {
                scene.Destroy();
            }

            if (!slot.IsDestroyed)
            {
                slot.Destroy();
            }
        }

        window.Unmapped += Cleanup;
        window.Destroyed += Cleanup;
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (RootAppOf(popup) is not { } app)
        {
            return;
        }

        var scene = _popups.Attach(popup, app.Scene.Tree, constrainBox: () =>
        {
            var origin = default(Point);
            if (app.Scene.Tree.TryMapSceneToLocal(0, 0, out var localX, out var localY))
            {
                origin = new Point((int)-localX, (int)-localY);
            }

            var cell = app.Cell;
            return new Box(origin.X, origin.Y, cell.Width, cell.Height);
        });
        _owners[popup.Surface] = app;
        scene.Destroyed += () => _owners.Remove(popup.Surface);
    }

    private AppWindow? RootAppOf(XdgPopupWindow popup)
    {
        var parent = popup.Parent;
        while (parent is not null)
        {
            switch (parent.Role)
            {
                case XdgToplevelWindow toplevel:
                    return AppOf(toplevel);
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
