using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

public sealed class BasinOutputVisual : CompositionCustomVisualHandler
{
    public static readonly object WakeMessage = new();

    private readonly Func<BasinCompositorHost> _createHost;
    private readonly Action<BasinCompositorHost>? _hostReady;
    private readonly Action<Exception>? _hostFailed;
    private readonly bool _createOwnView;
    private BasinCompositorHost? _host;
    private BasinViewOutput? _view;
    private bool _shutdown;
    private bool _failed;
    private bool _reportedNoLease;
    private bool _probedEgl;

    public BasinOutputVisual(
        Func<BasinCompositorHost> createHost,
        Action<BasinCompositorHost>? hostReady = null,
        bool createOwnView = true,
        Action<Exception>? hostFailed = null)
    {
        ArgumentNullException.ThrowIfNull(createHost);
        _createHost = createHost;
        _hostReady = hostReady;
        _hostFailed = hostFailed;
        _createOwnView = createOwnView;
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_shutdown || _failed)
        {
            return;
        }

        try
        {
            EnsureHost();
        }
        catch (Exception error)
        {
            _failed = true;
            Log.Error($"the compositor host could not start: {error}");
            _hostFailed?.Invoke(error);
            return;
        }

        using var affinity = _host!.Affinity.Adopt();
        try
        {
            var host = _host!;
            if (_view is null)
            {
                if (!_probedEgl && context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } probeFeature)
                {
                    _probedEgl = true;
                    using var probeLease = probeFeature.Lease();
                    if (host.Renderer.BindFrame(probeLease))
                    {
                        host.Renderer.UnbindFrame();
                    }
                }

                BasinVisualFrame.Pump(host, CompositionNow);
                host.InvalidateDirtyViews();
                return;
            }

            _view.Resize(Math.Max(1, (int)EffectiveSize.X), Math.Max(1, (int)EffectiveSize.Y), 1.0);
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null && !_reportedNoLease)
            {
                _reportedNoLease = true;
                Log.Error($"no Skia lease on this backend; nothing is composited");
            }

            BasinVisualFrame.Commit(host, _view, feature, CompositionNow);
            host.InvalidateDirtyViews();
            if (_view.SceneOutput.NeedsRepaint)
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
        using var affinity = _host is { } adopted ? adopted.Affinity.Adopt() : default;
        if (ReferenceEquals(message, WakeMessage))
        {
            if (_shutdown)
            {
                return;
            }

            if (_host is not { } host)
            {
                Invalidate();
                return;
            }

            try
            {
                BasinVisualFrame.Pump(host, System.Diagnostics.Stopwatch.GetElapsedTime(0));
                host.InvalidateDirtyViews();
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
                action();
                if (_host is { } host)
                {
                    BasinVisualFrame.Pump(host, System.Diagnostics.Stopwatch.GetElapsedTime(0));
                    host.Display.FlushClients();
                    host.InvalidateDirtyViews();
                }
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
                TearDown();
            }
            finally
            {
                shutdown.Complete();
            }
        }
    }

    public override void OnAnimationFrameUpdate()
    {
        if (_shutdown || _host is null)
        {
            return;
        }

        using var affinity = _host.Affinity.Adopt();
        try
        {
            BasinVisualFrame.Pump(_host, CompositionNow);
            _host.InvalidateDirtyViews();
            if (_view is not null && _view.SceneOutput.NeedsRepaint)
            {
                Invalidate();
            }
        }
        catch (Exception error)
        {
            Log.Error($"idle dispatch failed: {error}");
        }
    }

    private void EnsureHost()
    {
        if (_host is not null)
        {
            return;
        }

        _host = _createHost();
        if (_createOwnView)
        {
            _view = _host.CreateViewOutput(
                Math.Max(1, (int)EffectiveSize.X), Math.Max(1, (int)EffectiveSize.Y));
            _view.RequestRender = () => Invalidate();
        }

        _hostReady?.Invoke(_host);
    }

    private void TearDown()
    {
        if (_shutdown)
        {
            return;
        }

        _shutdown = true;
        _view?.Dispose();
        _view = null;
        _host?.Dispose();
        _host = null;
    }
}
