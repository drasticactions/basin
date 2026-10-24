using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Basin.Capabilities;
using Basin.Diagnostics;

namespace Basin.UI.Avalonia;

/// <summary>
/// An <see cref="IUIHost"/> that draws chrome surfaces with Avalonia. <see cref="BasinPlatform"/> makes it.
/// Use it on the thread that made it. It finds the controls of its surfaces by name for diagnostics.
/// </summary>
public sealed class AvaloniaUIHost : IUIHost, IUIElementLocator
{
    private readonly ThreadAffinity _thread = ThreadAffinity.Capture();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly BasinDispatcherImpl? _dispatcher;
    private readonly BasinRenderTimer? _renderTimer;
    private readonly BasinPlatformContext _context;
    private readonly List<AvaloniaUISurface> _surfaces = [];
    private bool _accessible;
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

    /// <summary>
    /// Gets a value indicating whether <see cref="BasinPlatform.Attach"/> made the host. An attached host runs inside another Avalonia platform and does not pump it.
    /// </summary>
    public bool IsAttached => _context.Attached;

    /// <summary>
    /// Gets the reporter of the focused chrome text field and of chrome touches.
    /// </summary>
    public AvaloniaTextFields TextFields { get; } = new();

    /// <summary>
    /// Occurs on the Avalonia render thread when a surface of an attached host finished a frame.
    /// The handler must call <see cref="AvaloniaUISurface.PublishDamage"/> on the compositor thread.
    /// </summary>
    public event Action<IUISurface>? SurfaceDamaged;

    internal void RaiseSurfaceDamaged(AvaloniaUISurface surface) => SurfaceDamaged?.Invoke(surface);

    internal BasinPlatformSettings? Settings { get; init; }

    internal CompositorSynchronizationContext? Wake { get; init; }

    /// <summary>
    /// Gets or sets the theme variant that the application sees. On an attached host it is always <see cref="UIThemeVariant.Light"/> and a set does nothing.
    /// </summary>
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

    /// <inheritdoc/>
    public event Action? WakeupRequested;

    /// <summary>
    /// Occurs when Avalonia opens a popup surface, for example a menu or a tooltip.
    /// </summary>
    public event Action<IUISurface>? PopupAppeared;

    /// <summary>
    /// Occurs when Avalonia closes a popup that <see cref="PopupAppeared"/> reported.
    /// </summary>
    public event Action<IUISurface>? PopupDismissed;

    /// <summary>
    /// Gets the buffer kind of the surfaces. It is <see cref="UITargetKind.Dmabuf"/> with a GPU and <see cref="UITargetKind.Memory"/> without one.
    /// </summary>
    public UITargetKind Produces => _context.Gpu is null ? UITargetKind.Memory : UITargetKind.Dmabuf;

    /// <summary>
    /// Gets the delay in milliseconds until the next Avalonia timer is due.
    /// It is <see langword="null"/> when no work is due, when the host is disposed and on an attached host.
    /// </summary>
    public long? NextDueMillis => _disposed || _dispatcher is null ? null : _dispatcher.NextDueMillis;

    /// <inheritdoc/>
    public bool TryLocate(string name, out Box box)
    {
        ArgumentNullException.ThrowIfNull(name);
        _thread.Assert();
        for (var i = _surfaces.Count - 1; i >= 0; i--)
        {
            var surface = _surfaces[i];
            if (surface.VisualRoot is not { } root)
            {
                continue;
            }

            foreach (var control in root.GetVisualDescendants().OfType<Control>())
            {
                if (control.Name != name || !control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0 ||
                    control.TranslatePoint(default, root) is not { } origin)
                {
                    continue;
                }

                box = new Box(
                    (int)Math.Round(surface.PositionX + origin.X),
                    (int)Math.Round(surface.PositionY + origin.Y),
                    (int)Math.Round(control.Bounds.Width),
                    (int)Math.Round(control.Bounds.Height));
                return true;
            }
        }

        box = default;
        return false;
    }

    /// <inheritdoc/>
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
        if (_accessible)
        {
            surface.AttachAccessibility();
        }

        return surface;
    }

    /// <summary>
    /// Gets a value indicating whether every chrome surface is on the accessibility bus.
    /// </summary>
    public bool IsAccessible => _accessible;

    /// <summary>
    /// Checks that the session has an accessibility bus, then puts every chrome surface on it, each as its own AccessKit adapter. A second call does nothing.
    /// </summary>
    /// <returns><see langword="null"/> on success, or the reason that the tree cannot start.</returns>
    public async Task<string?> StartAccessibilityAsync()
    {
        _thread.Assert();
        if (_disposed)
        {
            return "the host is disposed";
        }

        if (_accessible)
        {
            return null;
        }

        if (await AccessibilityBusProbe.ReasonAsync() is { } reason)
        {
            return reason;
        }

        return StartAccessibility();
    }

    /// <summary>
    /// Puts every chrome surface on the accessibility bus, each as its own AccessKit adapter, without a check of the bus. A second call does nothing.
    /// </summary>
    /// <returns><see langword="null"/> on success, or the reason that the tree cannot start.</returns>
    public string? StartAccessibility()
    {
        _thread.Assert();
        if (_disposed)
        {
            return "the host is disposed";
        }

        if (_accessible)
        {
            return null;
        }

        if (AvaloniaUISurface.AccessKitUnavailable() is { } reason)
        {
            return reason;
        }

        _accessible = true;
        foreach (var surface in _surfaces)
        {
            surface.AttachAccessibility();
        }

        return null;
    }

    /// <summary>
    /// Takes every chrome surface off the accessibility bus.
    /// </summary>
    public void StopAccessibility()
    {
        _thread.Assert();
        if (!_accessible)
        {
            return;
        }

        _accessible = false;
        foreach (var surface in _surfaces)
        {
            surface.DetachAccessibility();
        }
    }

    /// <summary>
    /// Runs the due Avalonia jobs, fires the render timer, runs the jobs again and frees idle buffers.
    /// It does nothing on an attached host or after dispose.
    /// </summary>
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

    /// <summary>
    /// Disposes every surface, with its accessibility adapter, and the wake channel. A second call does nothing.
    /// </summary>
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
        _accessible = false;

        Wake?.Dispose();
    }

    internal void Forget(AvaloniaUISurface surface) => _surfaces.Remove(surface);

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
