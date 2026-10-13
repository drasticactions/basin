using Basin;
using Basin.Desktop;
using Basin.Scene;
using Basin.Shell.Xdg;

namespace LunaWm;

internal sealed partial class LunaShell
{
    private readonly List<Card> _cards = [];
    private readonly Dictionary<Surface, Card> _owners = [];
    private readonly Dictionary<SceneTree, CardScene> _x11Parts = [];
    private readonly Dictionary<Basin.XWayland.XWaylandWindow, Card> _x11Cards = [];
    private readonly Dictionary<Card, long> _closing = [];
    private readonly List<Card> _killScratch = [];
    private readonly HashSet<int> _launchRoots = [];
    private readonly HashSet<Surface> _shellActivated = [];
    private readonly Basin.XWayland.XWaylandSceneDriver _xwayland = new();
    private bool _xwaylandWired;
    private readonly List<SurfaceBox> _presence = [];
    private SceneTree _limbo = null!;
    private SceneTree _overrideRedirect = null!;
    private PopupPlacer _popups = null!;
    private FractionalScaleManager? _scales;
    private LunaActivationTokens _tokens = null!;
    private long _focusClock;

    internal IReadOnlyList<Card> Cards => _cards;

    private void AttachWindows()
    {
        _limbo = new SceneTree(_scene.Root) { Enabled = false };
        _overrideRedirect = new SceneTree(_scene.Root);
        _popups = new PopupPlacer(_layout);
        Shells.NewToplevel += OnNewToplevel;
        Shells.NewPopup += OnNewPopup;
        _services.Require<XdgToplevelSource>().ActivateRequested += window =>
        {
            if (CardOf(window) is { } card)
            {
                Show(card);
            }
        };
        _tokens.Redeemed += (_, surface, shell) =>
        {
            if (shell)
            {
                _shellActivated.Add(surface);
            }
        };
        _scales = _services.Find<FractionalScaleManager>();
        if (_scales is not null && _services.Find<CompositorGlobal>() is { } compositor)
        {
            compositor.SurfaceCreated += surface => _scales.AnnounceScale(surface, MaxScale());
        }
    }

    private double MaxScale()
    {
        var scale = 1.0;
        foreach (var view in _views)
        {
            scale = Math.Max(scale, view.Output.Scale);
        }

        return scale;
    }

    private void AnnounceScales()
    {
        if (_scales is null)
        {
            return;
        }

        _scene.CollectSurfaces(_presence);
        foreach (var (surface, _) in _presence)
        {
            var home = OwnerOf(surface)?.Home ?? (_views.Count > 0 ? _views[0] : null);
            if (home is null)
            {
                continue;
            }

            foreach (var view in _views)
            {
                surface.SetOutputPresence(view.Global, ReferenceEquals(view, home));
            }

            _scales.AnnounceScale(surface, home.Output.Scale);
        }
    }

    internal Card? OwnerOf(Surface? surface)
    {
        for (var candidate = surface; candidate is not null; candidate = candidate.SubsurfaceRole?.Parent)
        {
            if (_owners.TryGetValue(candidate, out var card))
            {
                return card;
            }
        }

        return null;
    }

    private Card? CardOf(XdgToplevelWindow window)
    {
        foreach (var card in _cards)
        {
            if (ReferenceEquals(card.Xdg, window))
            {
                return card;
            }
        }

        return null;
    }

    internal Card? Focused
    {
        get
        {
            if (Seat.Keyboard.Focus is not { } focus)
            {
                return null;
            }

            return OwnerOf(focus);
        }
    }

    internal LunaView ViewAt(double x, double y)
    {
        foreach (var view in _views)
        {
            var box = view.Box;
            if (x >= box.X && y >= box.Y && x < box.Right && y < box.Bottom)
            {
                return view;
            }
        }

        return PrimaryView;
    }

    internal LunaView PlacementView() => ViewAt(_seat.PointerX, _seat.PointerY);

    private void OnNewToplevel(XdgToplevelWindow window)
    {
        var parts = new CardScene(_limbo, _shadowTile, _black);
        var scene = new SceneSurface(parts.Content, window.Surface);
        var card = new Card(window, window, null, parts, scene);
        _cards.Add(card);
        scene.Destroyed += card.Dispose;
        var configured = false;
        window.WmCapabilities = XdgWmCapabilities.Fullscreen;
        window.Configuring += () =>
        {
            if (configured)
            {
                return;
            }

            configured = true;
            PrepareCard(card);
        };
        window.FullscreenRequested += fullscreen => SetFullscreen(card, fullscreen);
        window.MaximizeRequested += _ => window.RequestConfigure();
        window.MinimizeRequested += () =>
        {
            if (card.Home is { } home && ReferenceEquals(home.Deck.ActiveCard, card))
            {
                EnterCardView(home);
            }
        };
        window.TitleChanged += () => TitleChanged(card);
        window.Xdg.Mapped += () => Map(card);
        window.Xdg.Unmapped += () => Unmap(card);
        window.Destroyed += () =>
        {
            Unmap(card);
            _closing.Remove(card);
            _cards.Remove(card);
        };
    }

    private void PrepareCard(Card card)
    {
        ApplyQuirks(card);
        var view = card.Home ?? PlacementView();
        card.Home = view;
        var parent = card.Xdg?.Parent is { } xdgParent ? CardOf(xdgParent) : null;
        if (parent is not null)
        {
            var (width, height) = card.Handle.NaturalSize;
            if (width > 0 && height > 0)
            {
                card.Resize(width, height, 0, 0);
            }

            return;
        }

        var rule = _config.RuleFor(card.Facts);
        if (rule?.Fullscreen == true)
        {
            card.Fullscreen = true;
        }

        SizeCard(card, view);
        card.UseShader(_renderer);
    }

    private void SizeCard(Card card, LunaView view)
    {
        var box = card.Fullscreen ? view.FullBox : AppBoxFor(view, card);
        card.Resize(box.Width, box.Height, view.Box.X + box.X, view.Box.Y + box.Y);
        if (card.Xdg is not null)
        {
            card.Handle.SetFullscreen(card.Fullscreen);
            card.Handle.SetMaximized(!card.Fullscreen);
        }
        else
        {
            card.Handle.SetMaximized(true);
        }
    }

    private void ResizeCards(LunaView view)
    {
        foreach (var stack in view.Deck)
        {
            foreach (var card in stack)
            {
                var box = card.Fullscreen ? view.FullBox : AppBoxFor(view, card);
                if (card.Width != box.Width || card.Height != box.Height)
                {
                    SizeCard(card, view);
                }
            }
        }
    }

    private void SetFullscreen(Card card, bool fullscreen)
    {
        if (card.IsTransient)
        {
            card.Xdg?.RequestConfigure();
            return;
        }

        card.Fullscreen = fullscreen;
        if (card.Home is { } view)
        {
            SizeCard(card, view);
            SyncBarVisibility(view);
            LayoutCards(view, animate: true);
        }

        card.Xdg?.RequestConfigure();
        _report.Line($"FULLSCREEN {card.Id} {(fullscreen ? "on" : "off")}");
    }

    private void Map(Card card)
    {
        if (card.Mapped)
        {
            return;
        }

        card.Mapped = true;
        if (card.Surface is { } surface)
        {
            _owners[surface] = card;
        }

        ResolveMenu(card);
        var parentCard = card.Xdg?.Parent is { } xdgParent ? CardOf(xdgParent) : card.X11?.TransientFor is { } x11Parent ? CardOfHandle(x11Parent) : null;
        if (parentCard is not null)
        {
            MapTransient(card, parentCard);
            return;
        }

        var view = card.Home ?? PlacementView();
        var rule = _config.RuleFor(card.Facts);
        if (rule?.Output is { Length: > 0 } outputName)
        {
            foreach (var each in _views)
            {
                if (each.Output.Name == outputName)
                {
                    view = each;
                }
            }
        }

        if (!ReferenceEquals(view, card.Home))
        {
            card.Home = view;
            SizeCard(card, view);
        }

        card.ShellLaunched = IsShellLaunched(card);
        var sibling = SiblingFor(view, card, rule?.Stack ?? StackChoice.Auto);
        view.Deck.Add(card, sibling);
        card.Slot.Reparent(view.Cards);
        card.Slot.Enabled = true;
        _capture.Stack.RaiseChanged();
        ClearGhost(view);
        card.Jump(LaunchPose(view));
        view.InCardView = false;
        SyncBarVisibility(view);
        LayoutCards(view, animate: true, spec: LunaMotion.CardMaximize);
        Focus(card);
        _report.Line($"CARD + {card.Id} {card.AppId} stack={view.Deck.ActiveIndex} size={view.Deck.ActiveStack!.Count}{(sibling is null ? "" : $" joined={sibling.Id}")}");
    }

    private CardPose LaunchPose(LunaView view)
    {
        var target = view.Metrics.Maximized;
        return target with { Scale = view.Metrics.InactiveScale, Brightness = 1 };
    }

    private bool IsShellLaunched(Card card)
    {
        if (card.Surface is { } surface && _shellActivated.Remove(surface))
        {
            return true;
        }

        var pid = card.Pid;
        return pid > 0 && _launchRoots.Count > 0 && ProcessAncestry.DescendsFrom(pid, _launchRoots.Contains);
    }

    private Card? SiblingFor(LunaView view, Card card, StackChoice choice)
    {
        if (view.Deck.ActiveCard is not { } active || ReferenceEquals(active, card))
        {
            return null;
        }

        var candidate = new CardFacts(card.Pid, card.AppId, card.ShellLaunched);
        var focus = new CardFacts(active.Pid, active.AppId, active.ShellLaunched);
        return CardGrouping.JoinsActive(candidate, focus, ReferenceEquals(Focused, active), view.InCardView, choice) ? active : null;
    }

    private Card? CardOfHandle(Basin.Capabilities.IToplevelHandle handle)
    {
        foreach (var card in _cards)
        {
            if (ReferenceEquals(card.Handle, handle))
            {
                return card;
            }
        }

        return null;
    }

    private void MapTransient(Card card, Card parent)
    {
        card.Parent = parent;
        card.Home = parent.Home;
        parent.Children.Add(card);
        card.Slot.Reparent(parent.Transients);
        card.Slot.Enabled = true;
        CenterTransient(card, parent);
        SyncTransientScrim(parent);
        Focus(card);
        _report.Line($"TRANSIENT + {card.Id} {card.AppId} parent={parent.Id}");
    }

    private static void CenterTransient(Card card, Card parent)
    {
        var (width, height) = card.Handle.NaturalSize;
        if (width <= 0 || height <= 0)
        {
            (width, height) = (Math.Max(1, card.Width), Math.Max(1, card.Height));
        }

        card.Slot.SetPosition(Math.Max(0, (parent.Width - width) / 2), Math.Max(0, (parent.Height - height) / 2));
    }

    private void SyncTransientScrim(Card parent)
    {
        parent.SetScrim(parent.Children.Count > 0);
    }

    private void Unmap(Card card)
    {
        if (!card.Mapped)
        {
            return;
        }

        card.Mapped = false;
        if (card.Surface is { } surface)
        {
            _owners.Remove(surface);
            _shellActivated.Remove(surface);
        }

        if (card.Parent is { } parent)
        {
            parent.Children.Remove(card);
            card.Parent = null;
            SyncTransientScrim(parent);
            if (!card.Slot.IsDestroyed)
            {
                card.Slot.Reparent(_limbo);
            }

            if (parent.Mapped)
            {
                Focus(parent);
            }

            _report.Line($"TRANSIENT - {card.Id}");
            return;
        }

        CancelInteractionsFor(card);
        var view = card.Home;
        var wasActive = view is not null && ReferenceEquals(view.Deck.ActiveCard, card);
        view?.Deck.Remove(card);
        if (!card.Slot.IsDestroyed && !card.Flying)
        {
            card.Slot.Reparent(_limbo);
        }

        _capture.Stack.RaiseChanged();
        _closing.Remove(card);
        _report.Line($"CARD - {card.Id} {card.AppId}");
        if (view is null)
        {
            return;
        }

        if (view.Deck.IsEmpty)
        {
            view.InCardView = true;
            Seat.Keyboard.NotifyClearFocus();
        }
        else if (wasActive && !view.InCardView && view.Deck.ActiveCard is { } next)
        {
            Focus(next);
        }

        SyncBarVisibility(view);
        LayoutCards(view, animate: true, spec: LunaMotion.CardSlide);
    }

    private void RehomeCards(LunaView gone)
    {
        if (_views.Count == 0)
        {
            return;
        }

        var target = _views[0];
        foreach (var stack in gone.Deck.Stacks.ToArray())
        {
            foreach (var card in stack.Cards.ToArray())
            {
                gone.Deck.Remove(card);
                target.Deck.Append(card);
                card.Home = target;
                card.Slot.Reparent(target.Cards);
                SizeCard(card, target);
            }
        }

        LayoutCards(target, animate: false);
    }

    internal void Focus(Card? card)
    {
        if (card is null || card.Surface is not { } surface || !card.Handle.WantsFocus)
        {
            return;
        }

        if (card.Children.Count > 0)
        {
            Focus(card.Children[^1]);
            return;
        }

        if (Focused is { } previous && !ReferenceEquals(previous, card))
        {
            previous.Handle.SetActivated(false);
        }

        card.Handle.SetActivated(true);
        card.FocusStamp = ++_focusClock;
        var root = card.Parent ?? card;
        root.Home?.Deck.Activate(root);
        _capture.Stack.RaiseChanged();
        _seat.ClearChromeFocus();
        if (_locked)
        {
            _lockedFocus = card;
            return;
        }

        Seat.Keyboard.NotifyEnter(surface);
        UpdateTitle(root.Home);
    }

    internal void FocusSurface(Surface surface)
    {
        if (OwnerOf(surface) is { } card && card.Home is { InCardView: false })
        {
            Focus(card);
        }
    }

    internal void Show(Card card)
    {
        var root = card.Parent ?? card;
        if (root.Home is not { } view)
        {
            return;
        }

        view.Deck.Activate(root);
        Maximize(view);
        Focus(card);
    }

    internal void CloseCard(Card card)
    {
        if (_closing.ContainsKey(card))
        {
            return;
        }

        card.Closing = true;
        _closing[card] = _config.CloseTimeoutMs > 0 ? Environment.TickCount64 + _config.CloseTimeoutMs : long.MaxValue;
        card.RequestClose();
        _report.Line($"CLOSE {card.Id} {card.AppId}");
    }

    private void ExpireCloseTimers()
    {
        if (_closing.Count == 0)
        {
            return;
        }

        var now = Environment.TickCount64;
        _killScratch.Clear();
        foreach (var (card, deadline) in _closing)
        {
            if (now >= deadline)
            {
                _killScratch.Add(card);
            }
        }

        foreach (var card in _killScratch)
        {
            _closing.Remove(card);
            Kill(card);
        }
    }

    private void Kill(Card card)
    {
        var pid = card.X11 is not null ? 0 : card.Pid;
        if (pid <= 0 || pid == Environment.ProcessId)
        {
            _log.Debug($"no pid for {card.AppId}; it is not killed");
            return;
        }

        _log.Info($"{card.AppId} did not close in {_config.CloseTimeoutMs}ms; killing pid {pid}");
        try
        {
            System.Diagnostics.Process.GetProcessById(pid).Kill(entireProcessTree: false);
            _report.Line($"KILL {card.Id} {pid}");
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or NotSupportedException or System.ComponentModel.Win32Exception)
        {
            _log.Debug($"pid {pid} was gone already");
        }
    }

    private void AttachXWayland(Basin.XWayland.XWaylandWm wm)
    {
        if (_xwaylandWired)
        {
            _xwayland.Attach(wm);
            return;
        }

        _xwaylandWired = true;
        _xwayland.Letterbox = true;
        _xwayland.PositionsManagedWindows = false;
        _xwayland.ShapeRenderer = _renderer;
        _xwayland.ShapeAllocator = _deviceAllocator;
        _xwayland.ManagedParent = _ =>
        {
            var parts = new CardScene(_limbo, _shadowTile, _black);
            _x11Parts[parts.Content] = parts;
            return parts.Content;
        };
        _xwayland.OverrideRedirectParent = _ => new SceneTree(_overrideRedirect);
        _xwayland.Adopted += OnX11Adopted;
        _xwayland.Removed += OnX11Removed;
        _xwayland.Attach(wm);
    }

    private void OnX11Adopted(Basin.XWayland.XWaylandWindow window, SceneSurface scene, bool managed)
    {
        if (!managed)
        {
            return;
        }

        if ((_xwayland.ParentOf(window) ?? scene.Tree.Parent) is not { } content || !_x11Parts.Remove(content, out var parts))
        {
            return;
        }

        var card = new Card(window, null, window, parts, scene);
        _cards.Add(card);
        _x11Cards[window] = card;
        window.PropertiesChanged += () => ApplyQuirks(card);
        PrepareCard(card);
        Map(card);
    }

    private void OnX11Removed(Basin.XWayland.XWaylandWindow window, SceneSurface scene)
    {
        if (!_x11Cards.Remove(window, out var card))
        {
            return;
        }

        _registrar?.Forget(window.WindowId);

        Unmap(card);
        _closing.Remove(card);
        _cards.Remove(card);
        card.Dispose();
    }

    private void OnNewPopup(XdgPopupWindow popup)
    {
        if (RootCardOf(popup) is not { } card)
        {
            return;
        }

        var scene = _popups.Attach(popup, card.Scene.Tree, constrainBox: () => new Box(0, 0, Math.Max(1, card.Width), Math.Max(1, card.Height)));
        _owners[popup.Surface] = card;
        scene.Destroyed += () => _owners.Remove(popup.Surface);
    }

    private Card? RootCardOf(XdgPopupWindow popup)
    {
        var parent = popup.Parent;
        while (parent is not null)
        {
            switch (parent.Role)
            {
                case XdgToplevelWindow toplevel:
                    return CardOf(toplevel);
                case XdgPopupWindow parentPopup:
                    parent = parentPopup.Parent;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    internal void Launch(string command, string? label = null, string? icon = null, string? desktopId = null)
    {
        var token = _tokens.MintShell();
        var launch = new ProcessLaunch(["sh", "-c", command])
        {
            DesktopId = desktopId,
            Env = new Dictionary<string, string>
            {
                ["XDG_ACTIVATION_TOKEN"] = token,
                ["DESKTOP_STARTUP_ID"] = token,
            },
        };
        if (_processes.Spawn(launch, out var error) is { } process)
        {
            _launchRoots.Add(process.Pid);
            _report.Line($"LAUNCH {label ?? command} pid={process.Pid}");
            ShowGhost(PlacementView(), label ?? command, icon);
            return;
        }

        _log.Warn($"cannot start '{command}': {error}");
    }
}
