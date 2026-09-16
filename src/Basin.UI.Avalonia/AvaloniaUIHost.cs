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
    private bool _disposed;

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

    public event Action<IUISurface>? SurfaceDamaged;

    internal void RaiseSurfaceDamaged(AvaloniaUISurface surface) => SurfaceDamaged?.Invoke(surface);

    internal BasinPlatformSettings? Settings { get; init; }

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
        return surface;
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
    }

    internal void Forget(AvaloniaUISurface surface) => _surfaces.Remove(surface);

    internal void AnnouncePopup(AvaloniaUISurface popup) => PopupAppeared?.Invoke(popup);

    internal void DismissPopup(AvaloniaUISurface popup) => PopupDismissed?.Invoke(popup);

    private void OnWakeupRequested() => WakeupRequested?.Invoke();
}
