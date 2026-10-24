using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;
using Basin.Scene;
using Basin.Hosted;
using static Basin.Avalonia.ViewLog;

namespace Basin.Avalonia;

/// <summary>
/// The composition visual handler that composites one view output of a shared <see cref="BasinCompositorHost"/>.
/// It processes <see cref="WakeMessage"/>, <see cref="RenderMessage"/>, an <see cref="Action"/> and a <see cref="BasinShutdownMessage"/>.
/// </summary>
public sealed class BasinViewVisual : CompositionCustomVisualHandler
{
    /// <summary>
    /// The message that dispatches the compositor and invalidates the views that are dirty.
    /// </summary>
    public static readonly object WakeMessage = new();

    /// <summary>
    /// The message that requests a render of this view.
    /// </summary>
    public static readonly object RenderMessage = new();

    private readonly BasinCompositorHost _host;
    private readonly Func<BasinCompositorHost, BasinViewOutput> _createView;
    private readonly Action? _drainInput;
    private readonly Action<FrameTick> _onBeforeDispatch;
    private BasinViewOutput? _view;
    private bool _shutdown;
    private bool _drainRegistered;
    private bool _frameFailing;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinViewVisual"/> class with no input drain.
    /// </summary>
    /// <param name="host">The compositor host that the view output belongs to.</param>
    /// <param name="createView">Makes the view output on the first frame or wake.</param>
    public BasinViewVisual(BasinCompositorHost host, Func<BasinCompositorHost, BasinViewOutput> createView)
        : this(host, createView, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinViewVisual"/> class.
    /// </summary>
    /// <param name="host">The compositor host that the view output belongs to.</param>
    /// <param name="createView">Makes the view output on the first frame or wake.</param>
    /// <param name="drainInput">Called before each compositor dispatch to deliver queued input, or <see langword="null"/>.</param>
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

    /// <inheritdoc/>
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

            _frameFailing = false;
        }
        catch (Exception error)
        {
            if (_frameFailing)
            {
                Log.Debug(error, $"frame: render failed again, frame dropped");
            }
            else
            {
                _frameFailing = true;
                Log.Error(error, $"frame: render failed, frame dropped");
            }
        }
    }

    /// <inheritdoc/>
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
                Log.Error(error, $"dispatch: wake dispatch failed");
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
                Log.Error(error, $"dispatch: posted work failed");
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
