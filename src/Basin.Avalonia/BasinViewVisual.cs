using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Hosted;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

public sealed class BasinViewVisual : CompositionCustomVisualHandler
{
    public static readonly object WakeMessage = new();

    public static readonly object RenderMessage = new();

    private readonly BasinCompositorHost _host;
    private readonly Func<BasinCompositorHost, BasinViewOutput> _createView;
    private readonly Action? _drainInput;
    private readonly Action<FrameTick> _onBeforeDispatch;
    private BasinViewOutput? _view;
    private bool _shutdown;
    private bool _drainRegistered;

    public BasinViewVisual(BasinCompositorHost host, Func<BasinCompositorHost, BasinViewOutput> createView)
        : this(host, createView, null)
    {
    }

    public BasinViewVisual(
        BasinCompositorHost host,
        Func<BasinCompositorHost, BasinViewOutput> createView,
        Action? drainInput)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(createView);
        _host = host;
        _createView = createView;
        _drainInput = drainInput;
        _onBeforeDispatch = _ => _drainInput?.Invoke();
    }

    internal BasinViewOutput? View => _view;

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_shutdown)
        {
            return;
        }

        using var affinity = _host.Affinity.Adopt();
        try
        {
            EnsureView();
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            BasinVisualFrame.Commit(_host, _view!, feature, CompositionNow);
            if (_view!.SceneOutput.NeedsRepaint)
            {
                Invalidate();
            }
        }
        catch (Exception error)
        {
            Log.Error($"frame dropped: {error}");
        }
    }

    public override void OnMessage(object message)
    {
        using var affinity = _host.Affinity.Adopt();
        if (ReferenceEquals(message, RenderMessage))
        {
            if (!_shutdown)
            {
                Invalidate();
            }

            return;
        }

        if (ReferenceEquals(message, WakeMessage))
        {
            if (_shutdown)
            {
                return;
            }

            try
            {
                EnsureView();
                BasinVisualFrame.Pump(_host, System.Diagnostics.Stopwatch.GetElapsedTime(0));
                _host.Display.FlushClients();
                _host.InvalidateDirtyViews();
            }
            catch (Exception error)
            {
                Log.Error($"wake dispatch failed: {error}");
            }

            return;
        }

        if (message is Action action)
        {
            if (_shutdown)
            {
                return;
            }

            try
            {
                EnsureView();
                action();
                BasinVisualFrame.Pump(_host, System.Diagnostics.Stopwatch.GetElapsedTime(0));
                _host.Display.FlushClients();
                _host.InvalidateDirtyViews();
            }
            catch (Exception error)
            {
                Log.Error($"posted work failed: {error}");
            }

            return;
        }

        if (message is BasinShutdownMessage shutdown)
        {
            try
            {
                if (!_shutdown)
                {
                    _shutdown = true;
                    if (_drainRegistered)
                    {
                        _host.Session.BeforeDispatch -= _onBeforeDispatch;
                        _drainRegistered = false;
                    }

                    _view?.Dispose();
                    _view = null;
                }
            }
            finally
            {
                shutdown.Complete();
            }
        }
    }

    private void EnsureView()
    {
        if (_view is not null)
        {
            return;
        }

        _view = _createView(_host);
        _view.RequestRender = () => Invalidate();
        if (_drainInput is not null && !_drainRegistered)
        {
            _drainRegistered = true;
            _host.Session.BeforeDispatch += _onBeforeDispatch;
        }
    }
}
