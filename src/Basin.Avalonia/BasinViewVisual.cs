using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;
using static Basin.Avalonia.AvaloniaLog;

namespace Basin.Avalonia;

public sealed class BasinViewVisual : CompositionCustomVisualHandler
{
    private readonly BasinCompositorHost _host;
    private readonly Func<BasinCompositorHost, BasinViewOutput> _createView;
    private BasinViewOutput? _view;
    private bool _shutdown;

    public BasinViewVisual(BasinCompositorHost host, Func<BasinCompositorHost, BasinViewOutput> createView)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(createView);
        _host = host;
        _createView = createView;
    }

    public override void OnRender(ImmediateDrawingContext context)
    {
        if (_shutdown)
        {
            return;
        }

        using var affinity = _host.Affinity.Adopt();
        try
        {
            if (_view is null)
            {
                _view = _createView(_host);
                _view.RequestRender = () => Invalidate();
            }

            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            BasinVisualFrame.Commit(_host, _view, feature, CompositionNow);
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
        if (message is BasinShutdownMessage shutdown)
        {
            using var affinity = _host.Affinity.Adopt();
            try
            {
                if (!_shutdown)
                {
                    _shutdown = true;
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
}
