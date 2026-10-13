using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;
using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.UI.Avalonia;

public sealed class AvaloniaUIHost : IUIHost
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly BasinDispatcherImpl? _dispatcher;
    private readonly BasinRenderTimer? _renderTimer;
    private readonly BasinPlatformContext _context;
    private readonly List<AvaloniaUISurface> _surfaces = [];
    private object? _atSpi;
    private bool _disposed;
    private int _wakePosted;

    internal AvaloniaUIHost(
        BasinDispatcherImpl dispatcher,
        BasinRenderTimer renderTimer,
        BasinPlatformContext context)
    {
        _dispatcher = dispatcher;
        _renderTimer = renderTimer;
        _context = context;
        _context.Host = this;
        _dispatcher.WakeupRequested += OnWakeupRequested;
    }

    internal AvaloniaUIHost(BasinPlatformContext context)
    {
        _context = context;
        _context.Host = this;
    }

    public bool IsAttached => _context.Attached;

    public AvaloniaTextFields TextFields { get; } = new();

    public event Action<IUISurface>? SurfaceDamaged;

    internal void RaiseSurfaceDamaged(AvaloniaUISurface surface) => SurfaceDamaged?.Invoke(surface);

    internal BasinPlatformSettings? Settings { get; init; }

    internal CompositorSynchronizationContext? Wake { get; init; }

    public UIThemeVariant Theme
    {
        get => Settings?.Variant ?? UIThemeVariant.Light;
        set
        {
            _thread.Assert();
            if (Settings is { } settings)
            {
                settings.Variant = value;
            }
        }
    }

    public event Action? WakeupRequested;

    public event Action<IUISurface>? PopupAppeared;

    public event Action<IUISurface>? PopupDismissed;

    public UITargetKind Produces => _context.Gpu is null ? UITargetKind.Memory : UITargetKind.Dmabuf;

    public long? NextDueMillis => _disposed || _dispatcher is null ? null : _dispatcher.NextDueMillis;

    public IUISurface? CreateSurface(in UISurfaceOptions options)
    {
        _thread.Assert();
        if (_disposed || (options.Target & Produces) == 0)
        {
            return null;
        }

        var impl = new BasinWindowImpl(_context);
        if (!impl.Resize(options.Width, options.Height, options.Scale, global::Avalonia.Controls.WindowResizeReason.Layout))
        {
            impl.Dispose();
            return null;
        }

        var surface = new AvaloniaUISurface(impl, ownsRoot: true, this);
        _surfaces.Add(surface);
        if (_atSpi is not null)
        {
            Expose(surface);
        }

        return surface;
    }

    public bool IsAccessible => _atSpi is not null;

    public async Task<string?> StartAccessibilityAsync()
    {
        _thread.Assert();
        if (_disposed)
        {
            return "the host is disposed";
        }

        if (_atSpi is not null)
        {
            return null;
        }

        if (Environment.GetEnvironmentVariable("DBUS_SESSION_BUS_ADDRESS") is not { Length: > 0 })
        {
            return "this session has no session bus, so the accessibility bus cannot be found";
        }

        object server;
        try
        {
            server = AtSpiAccess.Create();
        }
        catch (Exception error) when (error is MissingMemberException or TypeLoadException or FileNotFoundException or FileLoadException)
        {
            return $"this Avalonia build has no AT-SPI server the host can reach ({error.Message})";
        }

        try
        {
            await AtSpiAccess.StartAsync(server);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            await DisposeQuietly(server);
            return $"the accessibility bus is not reachable ({error.Message})";
        }

        if (_disposed)
        {
            await DisposeQuietly(server);
            return "the host is disposed";
        }

        _atSpi = server;
        foreach (var surface in _surfaces)
        {
            Expose(surface);
        }

        return null;
    }

    public async Task StopAccessibilityAsync()
    {
        _thread.Assert();
        if (_atSpi is not { } server)
        {
            return;
        }

        _atSpi = null;
        foreach (var surface in _surfaces)
        {
            surface.Root?.RemoveHandler(global::Avalonia.Input.InputElement.GotFocusEvent, OnGotFocus);
        }

        await DisposeQuietly(server);
    }

    private void OnGotFocus(object? sender, global::Avalonia.Input.FocusChangedEventArgs e)
    {
        if (e.Source is global::Avalonia.Controls.Control focused)
        {
            Dispatcher.UIThread.Post(() => AnnounceFocus(focused), DispatcherPriority.Background);
        }
    }

    private void AnnounceFocus(global::Avalonia.Controls.Control focused)
    {
        if (_atSpi is not { } server || !focused.IsFocused)
        {
            return;
        }

        AvaloniaUISurface? owner = null;
        foreach (var surface in _surfaces)
        {
            if (surface.Root is { } root && (ReferenceEquals(root, focused) || global::Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(root, focused)))
            {
                owner = surface;
                break;
            }
        }

        if (owner is null || RootPeer(owner) is not { } rootPeer)
        {
            return;
        }

        var leaf = global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(focused);
        try
        {
            if (AtSpiAccess.TryGetAttachedNode(server, leaf) is not null)
            {
                return;
            }

            var chain = new List<global::Avalonia.Automation.Peers.AutomationPeer>();
            for (var peer = leaf; peer is not null && !ReferenceEquals(peer, rootPeer); peer = peer.GetParent())
            {
                chain.Add(peer);
            }

            if (AtSpiAccess.TryGetAttachedNode(server, rootPeer) is not { } parent)
            {
                return;
            }

            for (var i = chain.Count - 1; i >= 0; i--)
            {
                var node = AtSpiAccess.TryGetAttachedNode(server, chain[i]) ?? AtSpiAccess.GetOrCreateNode(server, chain[i]);
                if (!AtSpiAccess.AttachNode(server, node, parent))
                {
                    return;
                }

                parent = node;
            }

            AtSpiAccess.EmitFocusChange(server, parent);
        }
        catch (Exception error) when (error is MissingMemberException or TypeLoadException)
        {
            BasinLog.For("avalonia").Debug($"AT-SPI focus announcement unavailable: {error.Message}");
        }
    }

    public void Pump()
    {
        _thread.Assert();
        if (_disposed || _dispatcher is null || _renderTimer is null)
        {
            return;
        }

        _dispatcher.Pump();
        _renderTimer.Fire(_clock.Elapsed);
        _dispatcher.Pump();

        var now = _clock.ElapsedMilliseconds;
        for (var i = 0; i < _surfaces.Count; i++)
        {
            _surfaces[i].Trim(now);
        }
    }

    public void Dispose()
    {
        _thread.Assert();
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_dispatcher is not null)
        {
            _dispatcher.WakeupRequested -= OnWakeupRequested;
        }

        foreach (var surface in _surfaces.ToArray())
        {
            surface.Dispose();
        }

        _surfaces.Clear();
        if (_atSpi is { } server)
        {
            _atSpi = null;
            _ = DisposeQuietly(server);
        }

        Wake?.Dispose();
    }

    internal void Forget(AvaloniaUISurface surface)
    {
        if (_surfaces.Remove(surface) && _atSpi is { } server && RootPeer(surface) is { } peer)
        {
            AtSpiAccess.RemoveWindow(server, peer);
        }
    }

    private void Expose(AvaloniaUISurface surface)
    {
        if (_atSpi is { } server && RootPeer(surface) is { } peer)
        {
            AtSpiAccess.AddWindow(server, peer);
            surface.Root!.AddHandler(global::Avalonia.Input.InputElement.GotFocusEvent, OnGotFocus, global::Avalonia.Interactivity.RoutingStrategies.Bubble, handledEventsToo: true);
            surface.Reactivate();
        }
    }

    private static global::Avalonia.Automation.Peers.AutomationPeer? RootPeer(AvaloniaUISurface surface)
    {
        if (surface.Root is not { } root)
        {
            return null;
        }

        var peer = global::Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(root);
        return peer.GetAutomationRoot() ?? peer;
    }

    private static async Task DisposeQuietly(object server)
    {
        try
        {
            await AtSpiAccess.DisposeAsync(server);
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            BasinLog.For("avalonia").Debug($"AT-SPI server dispose: {error.Message}");
        }
    }

    internal void AnnouncePopup(AvaloniaUISurface popup) => PopupAppeared?.Invoke(popup);

    internal void DismissPopup(AvaloniaUISurface popup) => PopupDismissed?.Invoke(popup);

    private void OnWakeupRequested()
    {
        if (Wake is not { } wake || wake.IsOnLoopThread)
        {
            WakeupRequested?.Invoke();
            return;
        }

        if (Interlocked.Exchange(ref _wakePosted, 1) == 0)
        {
            wake.Post(static state => ((AvaloniaUIHost)state!).WakeOnLoop(), this);
        }
    }

    private void WakeOnLoop()
    {
        Volatile.Write(ref _wakePosted, 0);
        if (!_disposed)
        {
            WakeupRequested?.Invoke();
        }
    }
}
