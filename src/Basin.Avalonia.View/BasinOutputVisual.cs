using Avalonia;
using Avalonia.Media;
using Avalonia.Rendering.Composition;
using Avalonia.Skia;
using Basin.Diagnostics;
using Basin.Hosted;
using static Basin.Avalonia.ViewLog;

namespace Basin.Avalonia;

/// <summary>
/// The composition visual handler that creates a <see cref="BasinCompositorHost"/>, pumps it and composites its view output.
/// It processes <see cref="WakeMessage"/>, <see cref="StartMessage"/>, an <see cref="Action"/> and a <see cref="BasinShutdownMessage"/>.
/// </summary>
public sealed class BasinOutputVisual : CompositionCustomVisualHandler
{
    /// <summary>
    /// The message that dispatches the compositor once. Before the host exists it requests a render, which creates the host.
    /// </summary>
    public static readonly object WakeMessage = new();

    /// <summary>
    /// The message that creates the host and starts dispatch without a render.
    /// </summary>
    public static readonly object StartMessage = new();

    private readonly Func<BasinCompositorHost> _createHost;
    private readonly Action<BasinCompositorHost>? _hostReady;
    private readonly Action<Exception>? _hostFailed;
    private readonly bool _createOwnView;
    private BasinCompositorHost? _host;
    private BasinViewOutput? _view;
    private bool _shutdown;
    private bool _failed;
    private bool _reportedNoLease;
    private bool _frameFailing;
    private bool _probedEgl;
    private nint _earlyEglDisplay;

    /// <summary>
    /// Initializes a new instance of the <see cref="BasinOutputVisual"/> class.
    /// </summary>
    /// <param name="createHost">Makes the compositor host on the composition thread. The handler disposes the host at shutdown.</param>
    /// <param name="hostReady">Called on the composition thread after the host is created.</param>
    /// <param name="createOwnView"><see langword="true"/> to create a view output that fills the visual.</param>
    /// <param name="hostFailed">Called on the composition thread when <paramref name="createHost"/> throws.</param>
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

    /// <inheritdoc/>
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
            Log.Error(error, $"host: compositor host did not start");
            _hostFailed?.Invoke(error);
            return;
        }

        using var affinity = _host!.Affinity.Adopt();
        try
        {
            var host = _host!;
            if (!_probedEgl && context.TryGetFeature<ISkiaSharpApiLeaseFeature>() is { } probeFeature)
            {
                _probedEgl = true;
                using var probeLease = probeFeature.Lease();
                if (host.Renderer.BindFrame(probeLease))
                {
                    host.Renderer.UnbindFrame();
                }
            }

            if (_view is null)
            {
                BasinVisualFrame.Pump(host, CompositionNow);
                host.InvalidateDirtyViews();
                KeepPolling(host);
                return;
            }

            _view.Resize(Math.Max(1, (int)EffectiveSize.X), Math.Max(1, (int)EffectiveSize.Y), 1.0);
            var feature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (feature is null && !_reportedNoLease)
            {
                _reportedNoLease = true;
                Log.Error($"view: backend has no Skia lease, nothing is composited");
            }

            BasinVisualFrame.Commit(host, _view, feature, CompositionNow);
            host.InvalidateDirtyViews();
            if (_view.SceneOutput.NeedsRepaint)
            {
                Invalidate();
            }

            KeepPolling(host);
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

    private void KeepPolling(BasinCompositorHost host)
    {
        if (!host.Wake.IsWatching)
        {
            RegisterForNextAnimationFrameUpdate();
        }
    }

    /// <inheritdoc/>
    public override void OnMessage(object message)
    {
        if (ReferenceEquals(message, StartMessage))
        {
            Start();
            return;
        }

        if (message is BasinEglDisplayMessage egl)
        {
            _earlyEglDisplay = egl.Display;
            return;
        }

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
                Log.Error(error, $"dispatch: posted work failed");
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

    /// <inheritdoc/>
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

            KeepPolling(_host);
        }
        catch (Exception error)
        {
            Log.Error(error, $"dispatch: idle dispatch failed");
        }
    }

    private void Start()
    {
        if (_shutdown || _failed || _host is not null)
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
            Log.Error(error, $"host: compositor host did not start");
            _hostFailed?.Invoke(error);
            return;
        }

        using var affinity = _host!.Affinity.Adopt();
        try
        {
            BasinVisualFrame.Pump(_host, CompositionNow);
            _host.InvalidateDirtyViews();
            KeepPolling(_host);
        }
        catch (Exception error)
        {
            Log.Error(error, $"dispatch: first dispatch failed");
        }
    }

    private void EnsureHost()
    {
        if (_host is not null)
        {
            return;
        }

        _host = _createHost();
        if (_earlyEglDisplay != 0)
        {
            _host.Renderer.ToolkitLendsContext = true;
            _host.Renderer.TryEnableEgl(_earlyEglDisplay);
        }

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
