using Basin;
using Basin.Diagnostics;
using Basin.AppMenu;
using Basin.Capabilities;
using Basin.Host;
using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;
using System7.Avalonia.Controls;
using System7.Avalonia.Rendering;

namespace System7Wm;

internal sealed partial class System7Shell
{
    private const uint LeftButton = 272;

    private readonly List<ShellWindow> _stack = [];
    private readonly Dictionary<string, AppGroup> _apps = new(StringComparer.Ordinal);
    private readonly Dictionary<Surface, ShellWindow> _bySurface = [];
    private readonly NestedPopupPlacer _popups = new();
    private AppGroup _finder = null!;
    private AppGroup? _front;
    private ShellWindow? _active;
    private DragState? _drag;
    private GrayOutline? _outline;
    private XdgDecorationManager? _decorations;
    private DecorationPreferences? _decorationPreferences;
    private XdgToplevelSource? _toplevels;
    private int _stagger;

    internal IReadOnlyList<ShellWindow> Stack => _stack;

    internal AppGroup? Front => _front;

    internal AppGroup Finder => _finder;

    internal IEnumerable<AppGroup> Apps => _apps.Values;

    private void AttachWindows(SceneCapturePack capture)
    {
        _outline = new GrayOutline(_layers.Outlines);
        _finder = new AppGroup("finder", "finder", 0, "Finder")
        {
            IsFinder = true,
            Icon = System7IconFamily.Generic("macintosh"),
        };
        _apps[_finder.Key] = _finder;
        _front = _finder;
        _decorations = _services.Find<XdgDecorationManager>();
        if (_decorations is not null)
        {
            _decorations.DefaultMode = DecorationMode.ServerSide;
            _decorations.ChooseMode = (window, preference) => RuleDecoration(window) ?? preference ?? DecorationMode.ServerSide;
        }

        _decorationPreferences = new DecorationPreferences(
            _decorations, _services.Find<KdeServerDecorationManager>(), _services.Find<XdgToplevelSource>());
        _decorationPreferences.Changed += (surface, _) =>
        {
            if (WindowOf(surface) is { Mapped: true } shell)
            {
                Reframe(shell);
            }
        };

        _toplevels = _services.Find<XdgToplevelSource>();
        if (_services.Find<XdgDialogManager>() is { } dialogs)
        {
            dialogs.ModalChanged += (toplevel, modal) =>
            {
                if (WindowOf(toplevel.Surface) is { } window)
                {
                    window.Modal = modal;
                    Reframe(window);
                    if (modal && window.Mapped)
                    {
                        Activate(window);
                    }
                }
            };
        }

        Shells.NewToplevel += OnNewToplevel;
        _popups.ConstrainBox = toplevel =>
            WindowOf(toplevel.Surface) is { } owner ? _layout.BoxOf(ViewOf(owner).Output) : null;
        Shells.NewPopup += OnNewPopup;
        var model = _services.Require<IToplevelModel>();
        capture.Attach(model, surface =>
            WindowOf(surface) is { } window ? new ToplevelCaptureTrees(window.Container, null) : null);
        AttachMenus();
        AttachAddresses();
    }

    internal ShellWindow? WindowOf(Surface? surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            if (_bySurface.TryGetValue(candidate, out var window))
            {
                return window;
            }
        }

        return null;
    }

    private DecorationMode? RuleDecoration(XdgToplevelWindow window) =>
        _config.RuleFor(Basin.Config.WindowFacts.ForWayland(
            window.AppId, window.Title, window.Parent is not null, ProcessIdentity.Of(window.Surface)))?.Decorations switch
        {
            "frame" => DecorationMode.ServerSide,
            "none" => DecorationMode.ClientSide,
            _ => null,
        };

    private AppGroup AppFor(string appId, uint pid)
    {
        var id = appId.Length > 0 ? appId : $"pid-{pid}";
        var key = id;
        if (_apps.TryGetValue(key, out var existing) && existing.Pid != pid && existing.Pid != 0 && existing.Windows.Count > 0)
        {
            key = $"{id}#{pid}";
        }

        if (_apps.TryGetValue(key, out var app))
        {
            return app;
        }

        var entry = appId.Length > 0 ? _desktopEntries.FindForAppId(appId) : null;
        app = new AppGroup(key, appId, pid, entry?.Name is { Length: > 0 } name ? name : appId.Length > 0 ? appId : "Application");
        app.Icon = IconFor(entry?.Icon ?? appId, "application");
        BuildSynthesized(app);
        _apps[key] = app;
        return app;
    }

    private static uint PidOf(Surface surface) =>
        surface.Resource.Client.TryGetCredentials(out var credentials) ? (uint)credentials.Pid : 0;

    private void OnNewToplevel(XdgToplevelWindow toplevel)
    {
        var container = new SceneTree(_layers.Windows) { Enabled = false };
        var scene = new SceneSurface(container, toplevel.Surface);
        _popups.Add(toplevel.Xdg, scene);
        var window = new ShellWindow(container, _finder) { Xdg = toplevel, Owner = WindowOf, Scene = scene, ContentNode = scene.Tree };
        _bySurface[toplevel.Surface] = window;

        void Align()
        {
            var geometry = toplevel.Xdg.EffectiveGeometry;
            scene.Tree.SetPosition(window.X - geometry.X, window.Y - geometry.Y);
        }

        toplevel.Xdg.Mapped += () =>
        {
            window.App = AppFor(toplevel.AppId, PidOf(toplevel.Surface));
            window.App.Windows.Add(window);
            window.Mapped = true;
            ApplyQuirks(window);
            window.Kind = KindOf(window);
            Place(window);
            Align();
            container.Enabled = !window.App.Hidden;
            if (window.App.Hidden)
            {
                container.Reparent(_layers.Hidden);
            }

            _stack.Add(window);
            Reframe(window);
            ApplyAddress(window);
            Activate(window);
            BasinReport.Line($"WINDOW + {window.Id} {window.AppId} \"{window.Title}\"");
        };
        toplevel.Xdg.Unmapped += () => Forget(window, destroyed: false);
        toplevel.Xdg.Committed += () =>
        {
            Align();
            if (window.Mapped)
            {
                UpdateFrame(window);
            }
        };
        toplevel.TitleChanged += () => UpdateFrame(window);
        toplevel.AppIdChanged += () =>
        {
            if (window.Mapped && window.App.AppId != toplevel.AppId)
            {
                window.App.Windows.Remove(window);
                window.App = AppFor(toplevel.AppId, PidOf(toplevel.Surface));
                window.App.Windows.Add(window);
                RefreshMenus();
            }
        };
        toplevel.Destroyed += () =>
        {
            Forget(window, destroyed: true);
            _popups.Remove(toplevel.Xdg);
            _bySurface.Remove(toplevel.Surface);
            if (!container.IsDestroyed)
            {
                container.Destroy();
            }
        };
        toplevel.MoveRequested += _ => BeginDrag(window, grow: false);
        toplevel.ResizeRequested += (_, _) => BeginDrag(window, grow: true);
        toplevel.MaximizeRequested += maximized =>
        {
            if (maximized != window.Zoomed)
            {
                Zoom(window);
            }
        };
        toplevel.MinimizeRequested += () => HideApp(window.App);
    }

    private void OnNewPopup(XdgPopupWindow popup) => _popups.Attach(popup);

    private WindowKind KindOf(ShellWindow window)
    {
        if (window.Xdg is { } xdg && _decorationPreferences?.Preference(xdg.Surface) == false)
        {
            return WindowKind.Unframed;
        }

        if (window.X11 is { WantsDecorations: false })
        {
            return WindowKind.Unframed;
        }

        var hasParent = window.Xdg?.Parent is not null || window.X11?.TransientFor is not null;
        return window.Modal || (hasParent && !window.CanResize) ? WindowKind.Dialog : WindowKind.Document;
    }

    private void Forget(ShellWindow window, bool destroyed)
    {
        if (!window.Mapped)
        {
            return;
        }

        window.Mapped = false;
        if (_drag?.Window == window)
        {
            CancelDrag();
        }

        window.Frame?.Dispose();
        window.Frame = null;
        window.Container.Enabled = false;
        _stack.Remove(window);
        window.App.Windows.Remove(window);
        window.MenuImport?.Dispose();
        window.MenuImport = null;
        if (window.App.Windows.Count == 0 && !window.App.IsFinder)
        {
            _apps.Remove(window.App.Key);
        }

        if (ReferenceEquals(_active, window))
        {
            _active = null;
            var next = _stack.LastOrDefault(w => w.Mapped && !w.App.Hidden && ReferenceEquals(w.App, window.App))
                ?? (window.App.IsFinder ? null : _stack.LastOrDefault(w => w.Mapped && !w.App.Hidden));
            if (next is not null)
            {
                Activate(next);
            }
            else
            {
                ActivateFinder();
            }
        }

        RefreshMenus();
        BasinReport.Line($"WINDOW - {window.Id}{(destroyed ? " destroyed" : "")}");
    }

    internal OutputView ViewOf(ShellWindow window)
    {
        var (width, height) = window.Size;
        var cx = window.X + (width / 2);
        var cy = window.Y + (height / 2);
        foreach (var view in _outputs.Views)
        {
            var box = _layout.BoxOf(view.Output);
            if (cx >= box.X && cy >= box.Y && cx < box.Right && cy < box.Bottom)
            {
                return view;
            }
        }

        return MainView ?? _outputs.Views[0];
    }

    internal Box WorkArea(OutputView view)
    {
        var box = _layout.BoxOf(view.Output);
        var usable = UsableOf(view.Output, box);
        if (!ReferenceEquals(view, MainView))
        {
            return usable;
        }

        var bar = DevicePlacement.ToLogicalSpan(MenuBar.Height * DevicePlacement.ChromeScale(view.Output.Scale), view.Output.Scale);
        var top = Math.Max(usable.Y, box.Y + bar);
        return new Box(usable.X, top, usable.Width, Math.Max(0, usable.Bottom - top));
    }

    private Box ClientDevice(ShellWindow window, OutputView view)
    {
        if (window.IsShell)
        {
            return window.ShellDevice;
        }

        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        var (width, height) = window.Size;
        var x = DevicePlacement.ToDevice(window.X - box.X, scale);
        var y = DevicePlacement.ToDevice(window.Y - box.Y, scale);
        return new Box(
            x,
            y,
            DevicePlacement.ToDevice(window.X - box.X + width, scale) - x,
            DevicePlacement.ToDevice(window.Y - box.Y + height, scale) - y);
    }

    private void Place(ShellWindow window)
    {
        var view = MainView ?? _outputs.Views[0];
        var area = WorkArea(view);
        var scale = view.Output.Scale;
        var chrome = DevicePlacement.ChromeScale(scale);
        var (width, height) = window.Size;
        if (window.Parent is { } parent)
        {
            var (pw, ph) = parent.Size;
            window.X = parent.X + Math.Max(0, (pw - width) / 2);
            window.Y = parent.Y + Math.Max(0, (ph - height) / 3);
            return;
        }

        var title = DevicePlacement.ToLogicalSpan((ClientFrame.TitleHeight + 1) * chrome, scale);
        var step = DevicePlacement.ToLogicalSpan(20 * chrome, scale);
        var left = area.X + DevicePlacement.ToLogicalSpan(8 * chrome, scale) + (_stagger * step);
        var top = area.Y + title + DevicePlacement.ToLogicalSpan(4 * chrome, scale) + (_stagger * step);
        if (left + width > area.Right || top + height > area.Bottom)
        {
            _stagger = 0;
            left = area.X + DevicePlacement.ToLogicalSpan(8 * chrome, scale);
            top = area.Y + title + DevicePlacement.ToLogicalSpan(4 * chrome, scale);
        }

        _stagger = (_stagger + 1) % 8;
        window.X = left;
        window.Y = top;
    }

    private void Reframe(ShellWindow window)
    {
        if (!window.Mapped)
        {
            return;
        }

        window.Kind = KindOf(window);
        if (window.Kind == WindowKind.Unframed)
        {
            window.Frame?.Dispose();
            window.Frame = null;
            return;
        }

        if (window.Frame is null)
        {
            var frame = new ClientFrame(window.Container, _ui, _chrome);
            frame.CloseRequested += () => window.Close();
            frame.ZoomRequested += () => Zoom(window);
            frame.SetDepth(_config.Depth);
            window.Frame = frame;
        }

        UpdateFrame(window);
    }

    internal Box FrameBoxOf(ShellWindow window, OutputView view)
    {
        if (window.Frame is { } frame)
        {
            return frame.FrameBox;
        }

        if (window.IsShell)
        {
            var n = DevicePlacement.ChromeScale(view.Output.Scale);
            var content = window.ShellDevice;
            return window.IsAlert
                ? new Box(content.X, content.Y, content.Width, content.Height)
                : new Box(content.X - n, content.Y - (ClientFrame.TitleHeight * n), content.Width + (3 * n), content.Height + ((ClientFrame.TitleHeight + 2) * n));
        }

        return ClientDevice(window, view);
    }

    private void UpdateShellWindow(ShellWindow window)
    {
        if (window.ShellSurface is not { } surface || window.ShellFrame is not { } frame)
        {
            return;
        }

        var view = ViewOf(window);
        var box = _layout.BoxOf(view.Output);
        var n = DevicePlacement.ChromeScale(view.Output.Scale);
        frame.Title = window.ShellTitle;
        frame.IsActive = window.Active;
        var (dipW, dipH) = window.ContentDip;
        var frameDip = window.IsAlert ? (dipW, dipH) : (dipW + 3, dipH + ClientFrame.TitleHeight + 2);
        frame.Width = frameDip.Item1;
        frame.Height = frameDip.Item2;
        window.ShellDevice = new Box(window.ShellDevice.X, window.ShellDevice.Y, dipW * n, dipH * n);
        window.ShellLogicalSize = (DevicePlacement.ToLogicalSpan(dipW * n, view.Output.Scale), DevicePlacement.ToLogicalSpan(dipH * n, view.Output.Scale));
        var frameBox = FrameBoxOf(window, view);
        if (window.ShellGrown)
        {
            var (width, height) = DevicePlacement.OutputSize(view.Output);
            frame.Margin = new Avalonia.Thickness(frameBox.X / (double)n, frameBox.Y / (double)n, 0, 0);
            surface.Place(box.X, box.Y, view.Output.Scale, new Box(0, 0, width, height));
        }
        else
        {
            frame.Margin = default;
            surface.Place(box.X, box.Y, view.Output.Scale, frameBox, frameDip.Item1, frameDip.Item2);
        }

        surface.Node.Node.RaiseToTop();
    }

    private void UpdateFrame(ShellWindow window)
    {
        if (window.IsShell && window.Mapped)
        {
            UpdateShellWindow(window);
            return;
        }

        if (window.Frame is not { } frame || !window.Mapped)
        {
            return;
        }

        var view = ViewOf(window);
        var box = _layout.BoxOf(view.Output);
        var document = window.Kind == WindowKind.Document;
        frame.Shaded = window.Shaded;
        frame.Update(box.X, box.Y, view.Output.Scale, ClientDevice(window, view), window.Title,
            window.Active, canClose: document, canZoom: document && window.CanResize, canResize: document && window.CanResize);
    }

    private void UpdateAllFrames()
    {
        foreach (var window in _stack)
        {
            UpdateFrame(window);
        }
    }

    internal void Activate(ShellWindow window)
    {
        if (!window.Mapped)
        {
            return;
        }

        var modal = _stack.LastOrDefault(w => w.Mapped && w.Modal && ReferenceEquals(w.Parent, window));
        if (modal is not null && !ReferenceEquals(modal, window))
        {
            window = modal;
        }

        if (window.App.Hidden)
        {
            ShowApp(window.App);
        }

        if (!ReferenceEquals(_active, window) && _active is { Mapped: true } previous)
        {
            previous.Active = false;
            previous.Xdg?.SetActivated(false);
            UpdateFrame(previous);
        }

        var menusChange = !ReferenceEquals(_active, window);
        _active = window;
        window.Active = true;
        window.Xdg?.SetActivated(true);
        if (window.X11 is { } x11)
        {
            x11.Activate();
        }

        if (_config.AppLayering)
        {
            foreach (var sibling in _stack.Where(w => ReferenceEquals(w.App, window.App) && !ReferenceEquals(w, window)).ToList())
            {
                Raise(sibling);
            }
        }

        Raise(window);
        UpdateFrame(window);
        if (!_locked)
        {
            FocusKeyboard(window);
        }

        SetFront(window.App);
        if (menusChange && ReferenceEquals(_front, window.App))
        {
            RefreshMenus();
        }

        _outputs.ScheduleAll();
    }

    private void FocusKeyboard(ShellWindow window)
    {
        if (window.ShellSurface?.Surface is { } chrome)
        {
            Seat.Keyboard.NotifyClearFocus();
            _seat.Router.SetKeyboardFocus(chrome);
        }
        else
        {
            _seat.Router.SetKeyboardFocus(null);
            if (window.Surface is { } surface)
            {
                Seat.Keyboard.NotifyEnter(surface);
            }
        }
    }

    internal void ActivateFinder()
    {
        if (_active is { Mapped: true } previous)
        {
            previous.Active = false;
            previous.Xdg?.SetActivated(false);
            UpdateFrame(previous);
        }

        _active = null;
        if (!_locked)
        {
            Seat.Keyboard.NotifyClearFocus();
        }

        SetFront(_finder);
        _outputs.ScheduleAll();
    }

    private void Raise(ShellWindow window)
    {
        window.Container.RaiseToTop();
        _stack.Remove(window);
        _stack.Add(window);
    }

    private void SetFront(AppGroup app)
    {
        if (ReferenceEquals(_front, app))
        {
            RefreshApplicationMenu();
            return;
        }

        _front = app;
        RefreshMenus();
    }

    internal void Zoom(ShellWindow window)
    {
        var view = ViewOf(window);
        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        var chrome = DevicePlacement.ChromeScale(scale);
        if (window.Zoomed && window.UserBox is { } user)
        {
            window.Zoomed = false;
            window.Xdg?.SetMaximized(false);
            window.X11?.SetMaximized(false);
            if (window.IsShell)
            {
                window.ShellDevice = new Box(user.X, user.Y, user.Width * chrome, user.Height * chrome);
                window.ContentDip = (user.Width, user.Height);
                SyncShellLogical(window, view);
                window.ShellResized?.Invoke();
                UpdateFrame(window);
                window.ShellPlaced?.Invoke();
                _outputs.ScheduleAll();
                return;
            }

            Resize(window, user.X, user.Y, user.Width, user.Height);
            return;
        }

        window.UserBox = window.IsShell
            ? new Box(window.ShellDevice.X, window.ShellDevice.Y, window.ContentDip.Width, window.ContentDip.Height)
            : window.Logical;
        window.Zoomed = true;
        window.Xdg?.SetMaximized(true);
        window.X11?.SetMaximized(true);
        var area = WorkArea(view);
        var areaX = DevicePlacement.ToDevice(area.X - box.X, scale);
        var areaY = DevicePlacement.ToDevice(area.Y - box.Y, scale);
        var areaW = DevicePlacement.ToDeviceSpan(area.X - box.X, area.Width, scale);
        var areaH = DevicePlacement.ToDeviceSpan(area.Y - box.Y, area.Height, scale);
        var frameX = areaX + (2 * chrome);
        var frameY = areaY + (3 * chrome);
        var frameW = areaW - (3 * chrome);
        var frameH = areaH - (4 * chrome);
        var clientX = frameX + chrome;
        var clientY = frameY + (ClientFrame.TitleHeight * chrome);
        var clientW = frameW - (3 * chrome);
        var clientH = frameH - ((ClientFrame.TitleHeight + 2) * chrome);
        if (window.IsShell)
        {
            window.ShellDevice = new Box(clientX, clientY, clientW, clientH);
            window.ContentDip = (clientW / chrome, clientH / chrome);
            SyncShellLogical(window, view);
            window.ShellResized?.Invoke();
            UpdateFrame(window);
            _outputs.ScheduleAll();
            return;
        }

        Resize(
            window,
            box.X + DevicePlacement.ToLogicalSpan(clientX, scale),
            box.Y + DevicePlacement.ToLogicalSpan(clientY, scale),
            (int)Math.Floor(clientW / scale),
            (int)Math.Floor(clientH / scale));
    }

    private void Resize(ShellWindow window, int x, int y, int width, int height)
    {
        window.X = x;
        window.Y = y;
        if (window.Xdg is { } xdg)
        {
            xdg.SetSize(Math.Max(1, width), Math.Max(1, height));
            xdg.RequestConfigure();
            var geometry = xdg.Xdg.EffectiveGeometry;
            window.Scene?.Tree.SetPosition(window.X - geometry.X, window.Y - geometry.Y);
        }
        else if (window.X11 is { } x11)
        {
            x11.Configure(x, y, Math.Max(1, width), Math.Max(1, height));
            window.ContentNode?.SetPosition(x, y);
        }

        UpdateFrame(window);
        _outputs.ScheduleAll();
    }

    private void MoveTo(ShellWindow window, int x, int y)
    {
        var (width, height) = window.Size;
        if (window.X11 is { } x11)
        {
            window.X = x;
            window.Y = y;
            x11.Configure(x, y, width, height);
            window.ContentNode?.SetPosition(x, y);
        }
        else
        {
            var geometry = window.Xdg?.Xdg.EffectiveGeometry ?? default;
            window.X = x;
            window.Y = y;
            window.Scene?.Tree.SetPosition(x - geometry.X, y - geometry.Y);
        }

        UpdateFrame(window);
        _outputs.ScheduleAll();
    }

    internal void Shade(ShellWindow window)
    {
        if (window.Frame is null)
        {
            return;
        }

        window.Shaded = !window.Shaded;
        if (window.ContentNode is { } content)
        {
            content.Enabled = !window.Shaded;
        }

        window.Xdg?.SetSuspended(window.Shaded || window.App.Hidden);
        window.Xdg?.RequestConfigure();
        UpdateFrame(window);
        if (_config.ShadeSound)
        {
            Beep();
        }

        BasinReport.Line($"SHADE {window.Id} {(window.Shaded ? "collapsed" : "expanded")}");
        _outputs.ScheduleAll();
    }

    internal void HideApp(AppGroup app)
    {
        if (app.IsFinder || app.Hidden)
        {
            return;
        }

        app.Hidden = true;
        foreach (var window in app.Windows)
        {
            window.Container.Reparent(_layers.Hidden);
            window.Xdg?.SetSuspended(true);
            window.Xdg?.RequestConfigure();
            if (window.Xdg is { } xdg)
            {
                _toplevels?.SetMinimized(xdg, true);
            }
        }

        if (ReferenceEquals(_active?.App, app) || ReferenceEquals(_front, app))
        {
            var next = _stack.LastOrDefault(w => w.Mapped && !w.App.Hidden);
            if (next is not null)
            {
                Activate(next);
            }
            else
            {
                ActivateFinder();
            }
        }

        BasinReport.Line($"HIDE {app.Key}");
        RefreshApplicationMenu();
        _outputs.ScheduleAll();
    }

    internal void ShowApp(AppGroup app)
    {
        if (!app.Hidden)
        {
            return;
        }

        app.Hidden = false;
        foreach (var window in app.Windows)
        {
            window.Container.Reparent(_layers.Windows);
            window.Container.Enabled = true;
            window.Xdg?.SetSuspended(window.Shaded);
            window.Xdg?.RequestConfigure();
            if (window.Xdg is { } xdg)
            {
                _toplevels?.SetMinimized(xdg, false);
            }
        }

        foreach (var window in _stack.Where(w => ReferenceEquals(w.App, app)).ToList())
        {
            Raise(window);
        }

        BasinReport.Line($"SHOW {app.Key}");
        RefreshApplicationMenu();
        _outputs.ScheduleAll();
    }

    internal void HideOthers(AppGroup keep)
    {
        foreach (var app in _apps.Values.ToList())
        {
            if (!ReferenceEquals(app, keep))
            {
                HideApp(app);
            }
        }
    }

    internal void ShowAll()
    {
        foreach (var app in _apps.Values.ToList())
        {
            ShowApp(app);
        }
    }

    internal void SwitchTo(AppGroup app)
    {
        if (app.IsFinder)
        {
            ActivateFinder();
            return;
        }

        ShowApp(app);
        if (_stack.LastOrDefault(w => ReferenceEquals(w.App, app)) is { } top)
        {
            Activate(top);
        }
    }

    private ShellWindow? WindowAt(double x, double y, out OutputView? view, out int dx, out int dy)
    {
        view = null;
        dx = 0;
        dy = 0;
        for (var i = _stack.Count - 1; i >= 0; i--)
        {
            var window = _stack[i];
            if (!window.Mapped || window.App.Hidden)
            {
                continue;
            }

            var candidate = ViewOf(window);
            var box = _layout.BoxOf(candidate.Output);
            var scale = candidate.Output.Scale;
            var px = (int)Math.Floor((x - box.X) * scale);
            var py = (int)Math.Floor((y - box.Y) * scale);
            var bounds = FrameBoxOf(window, candidate);
            if (px >= bounds.X && py >= bounds.Y && px < bounds.Right && py < bounds.Bottom)
            {
                view = candidate;
                dx = px;
                dy = py;
                return window;
            }
        }

        return null;
    }

    private bool OnPress(uint time, uint button, bool pressed, double x, double y)
    {
        if (_locked)
        {
            return false;
        }

        if (_seat.Router.Hovered is { } hovered)
        {
            if (ReferenceEquals(hovered, _menuBar?.Surface.Surface))
            {
                _menuBar!.Grow();
                return false;
            }

            if (ReferenceEquals(hovered, _desktopSurface?.Surface))
            {
                if (_stack.Any(w => w.IsAlert && w.Mapped))
                {
                    Beep();
                    return true;
                }

                ActivateFinder();
                return false;
            }

            if (ShellChromeOwner(hovered) is null)
            {
                if (_stack.Any(w => w.IsAlert && w.Mapped))
                {
                    Beep();
                    return true;
                }

                return false;
            }
        }

        if (_menuBar is { IsOpen: true })
        {
            return false;
        }

        if (AlertBlocks(x, y))
        {
            return true;
        }

        if (WindowAt(x, y, out var view, out var dx, out var dy) is not { } window || view is null)
        {
            if (button == LeftButton)
            {
                ActivateFinder();
            }

            return false;
        }

        var commandHeld = (HeldModifiers & _config.Command) != 0;
        if ((window.Frame is not null || (window.IsShell && !window.IsAlert)) && button == LeftButton)
        {
            var n = DevicePlacement.ChromeScale(view.Output.Scale);
            var frameBox = FrameBoxOf(window, view);
            var lx = (dx - frameBox.X) / n;
            var ly = (dy - frameBox.Y) / n;
            var document = window.Kind == WindowKind.Document;
            var part = System7WindowGeometry.HitTest(
                System7WindowKind.Document, frameBox.Width / n, frameBox.Height / n, lx, ly,
                window.Active, canClose: document, canZoom: document && window.CanResize, canResize: document && window.CanResize && !window.Shaded);
            switch (part)
            {
                case System7WindowPart.Close or System7WindowPart.Zoom:
                    return false;
                case System7WindowPart.TitleBar:
                    if (commandHeld && PreparePathMenu(window, lx))
                    {
                        return false;
                    }

                    if (!commandHeld && !window.Active)
                    {
                        Activate(window);
                    }

                    if (CountTitleClick(window, time) && document)
                    {
                        Shade(window);
                        return true;
                    }

                    BeginDrag(window, grow: false);
                    return true;
                case System7WindowPart.Grow:
                    BeginDrag(window, grow: true);
                    return true;
                case System7WindowPart.None:
                    return true;
            }
        }

        if (!window.Active)
        {
            Activate(window);
            return button == LeftButton && !_config.ClickThrough;
        }

        return false;
    }

    private bool AlertBlocks(double x, double y)
    {
        if (_stack.LastOrDefault(w => w.IsAlert && w.Mapped) is not { } alert)
        {
            return false;
        }

        if (WindowAt(x, y, out _, out _, out _) is { } hit && ReferenceEquals(hit, alert))
        {
            return false;
        }

        Beep();
        return true;
    }

    private bool CountTitleClick(ShellWindow window, uint time)
    {
        if (!_config.ShadeEnabled || !ShadeModifierHeld())
        {
            return false;
        }

        var now = Environment.TickCount64;
        window.TitleClicks = now - window.LastTitleClick <= _config.DoubleClickMs ? window.TitleClicks + 1 : 1;
        window.LastTitleClick = now;
        if (window.TitleClicks >= _config.ShadeClicks)
        {
            window.TitleClicks = 0;
            return true;
        }

        return false;
    }

    private bool ShadeModifierHeld()
    {
        var mask = HeldModifiers;
        return _config.ShadeModifier switch
        {
            "command" => (mask & _config.Command) != 0,
            "option" => mask.HasFlag(Basin.Config.Modifiers.Mod1),
            "control" => mask.HasFlag(Basin.Config.Modifiers.Ctrl),
            "shift" => mask.HasFlag(Basin.Config.Modifiers.Shift),
            _ => true,
        };
    }

    private void BeginDrag(ShellWindow window, bool grow)
    {
        if (!window.Mapped || (grow && !window.CanResize) || _outline is null)
        {
            return;
        }

        var view = ViewOf(window);
        var origin = FrameBoxOf(window, view);
        _drag = new DragState
        {
            Window = window,
            View = view,
            Grow = grow,
            StartX = _seat.PointerX,
            StartY = _seat.PointerY,
            Origin = origin,
            Proposed = origin,
        };
        _seat.Grabbing = true;
        ShowOutline();
    }

    private void ShowOutline()
    {
        if (_drag is not { } drag || _outline is null)
        {
            return;
        }

        if (!drag.Valid)
        {
            _outline.Hide();
            return;
        }

        var view = drag.Under ?? drag.View;
        var box = _layout.BoxOf(view.Output);
        var (deviceWidth, deviceHeight) = DevicePlacement.OutputSize(view.Output);
        var scale = view.Output.Scale;
        var chrome = DevicePlacement.ChromeScale(scale);
        var proposed = drag.Proposed;
        if (!ReferenceEquals(view, drag.View))
        {
            var from = _layout.BoxOf(drag.View.Output);
            var fromScale = drag.View.Output.Scale;
            var logicalX = from.X + (proposed.X / fromScale) - box.X;
            var logicalY = from.Y + (proposed.Y / fromScale) - box.Y;
            proposed = new Box(
                (int)Math.Round(logicalX * scale),
                (int)Math.Round(logicalY * scale),
                (int)Math.Round(proposed.Width / fromScale * scale),
                (int)Math.Round(proposed.Height / fromScale * scale));
        }

        var framed = drag.Window.Frame is not null || drag.Window.IsShell;
        var outline = !framed
            ? proposed
            : new Box(proposed.X, proposed.Y, proposed.Width - chrome, proposed.Height - chrome);
        _outline.Show(box.X, box.Y, scale, deviceWidth, deviceHeight, outline,
            !framed ? 0 : (ClientFrame.TitleHeight + 1) * chrome);
        _outputs.ScheduleAll();
    }

    private bool OnGrabMotion(double x, double y)
    {
        if (_fileDrag is not null)
        {
            ShowFileOutline(x, y);
            return true;
        }

        if (_drag is not { } drag)
        {
            return false;
        }

        var scale = drag.View.Output.Scale;
        var dx = (int)Math.Round((x - drag.StartX) * scale);
        var dy = (int)Math.Round((y - drag.StartY) * scale);
        drag.Moved |= dx != 0 || dy != 0;
        if (drag.Grow)
        {
            var chrome = DevicePlacement.ChromeScale(scale);
            var frame = drag.Window.Frame is not null || drag.Window.IsShell;
            var extraW = frame ? 3 * chrome : 0;
            var extraH = frame ? (ClientFrame.TitleHeight + 2) * chrome : 0;
            var (minW, minH) = drag.Window.MinSize;
            var (maxW, maxH) = drag.Window.MaxSize;
            var minimumW = Math.Max(5 * chrome, (int)Math.Ceiling(minW * scale) + extraW);
            var minimumH = Math.Max(21 * chrome, (int)Math.Ceiling(minH * scale) + extraH);
            var maximumW = maxW > 0 ? Math.Max(minimumW, (int)Math.Floor(maxW * scale) + extraW) : int.MaxValue;
            var maximumH = maxH > 0 ? Math.Max(minimumH, (int)Math.Floor(maxH * scale) + extraH) : int.MaxValue;
            drag.Proposed = new Box(drag.Origin.X, drag.Origin.Y,
                Math.Clamp(drag.Origin.Width + dx, minimumW, maximumW),
                Math.Clamp(drag.Origin.Height + dy, minimumH, maximumH));
            drag.Valid = true;
        }
        else
        {
            var under = _layout.OutputAt(x, y) is { } output && _outputs.ViewOf(output) is { } hovered ? hovered : drag.View;
            drag.Under = under;
            var area = WorkArea(under);
            var underScale = under.Output.Scale;
            var top = area.Y + DevicePlacement.ToLogicalSpan(4 * DevicePlacement.ChromeScale(underScale), underScale);
            drag.Valid = x >= area.X && x < area.Right && y >= top && y < area.Bottom;
            if (drag.Valid)
            {
                drag.Proposed = drag.Origin.Translated(dx, dy);
            }
        }

        ShowOutline();
        return true;
    }

    private bool OnGrabRelease(uint time, uint button)
    {
        if (FinishFileDrag(_seat.PointerX, _seat.PointerY))
        {
            return true;
        }

        if (_drag is not { } drag)
        {
            return false;
        }

        _drag = null;
        _seat.Grabbing = false;
        _outline?.Hide();
        var window = drag.Window;
        if (drag.Valid && drag.Moved && window.Mapped)
        {
            var box = _layout.BoxOf(drag.View.Output);
            var scale = drag.View.Output.Scale;
            var chrome = DevicePlacement.ChromeScale(scale);
            var inset = window.Frame is null && !window.IsShell ? (0, 0) : (chrome, ClientFrame.TitleHeight * chrome);
            if (window.IsShell)
            {
                var content = window.ShellDevice;
                if (drag.Grow)
                {
                    window.ContentDip = ((drag.Proposed.Width / chrome) - 3, (drag.Proposed.Height / chrome) - (ClientFrame.TitleHeight + 2));
                    window.ShellResized?.Invoke();
                }
                else
                {
                    window.ShellDevice = new Box(drag.Proposed.X + inset.Item1, drag.Proposed.Y + inset.Item2, content.Width, content.Height);
                }

                SyncShellLogical(window, drag.View);
                UpdateFrame(window);
                window.ShellPlaced?.Invoke();
                _seat.Refresh();
                _outputs.ScheduleAll();
                return true;
            }

            if (drag.Grow)
            {
                var extraW = window.Frame is null ? 0 : 3 * chrome;
                var extraH = window.Frame is null ? 0 : (ClientFrame.TitleHeight + 2) * chrome;
                window.Zoomed = false;
                Resize(window, window.X, window.Y,
                    (int)Math.Round((drag.Proposed.Width - extraW) / scale),
                    (int)Math.Round((drag.Proposed.Height - extraH) / scale));
                BasinReport.Line($"GROW {window.Id} {(int)Math.Round((drag.Proposed.Width - extraW) / scale)}x{(int)Math.Round((drag.Proposed.Height - extraH) / scale)}");
            }
            else
            {
                var clientX = drag.Proposed.X + inset.Item1;
                var clientY = drag.Proposed.Y + inset.Item2;
                MoveTo(window, box.X + (int)Math.Round(clientX / scale), box.Y + (int)Math.Round(clientY / scale));
                BasinReport.Line($"MOVE {window.Id} {window.X} {window.Y}");
            }
        }

        _seat.Refresh();
        _outputs.ScheduleAll();
        return true;
    }

    private void SyncShellLogical(ShellWindow window, OutputView view)
    {
        var box = _layout.BoxOf(view.Output);
        var scale = view.Output.Scale;
        window.X = box.X + (int)Math.Floor(window.ShellDevice.X / scale);
        window.Y = box.Y + (int)Math.Floor(window.ShellDevice.Y / scale);
    }

    private void CancelDrag()
    {
        _drag = null;
        _seat.Grabbing = false;
        _outline?.Hide();
    }

    private string? CursorAt(double x, double y) => _drag is not null ? "default" : null;
}
