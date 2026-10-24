namespace Basin.Scene;

/// <summary>
/// Coalesces scene structure and input-region changes into one pointer refresh from an idle callback, so that pointer focus follows the scene.
/// </summary>
public sealed class PointerRefresh : IDisposable
{
    private readonly Scene _scene;
    private readonly ICompositorEventLoop _loop;
    private readonly Action _refresh;
    private readonly Action _runFromIdle;
    private bool _queued;
    private bool _running;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PointerRefresh"/> class. It subscribes to <see cref="Scene.StructureChanged"/> and <see cref="Scene.InputRegionChanged"/>.
    /// </summary>
    /// <param name="scene">The scene to watch.</param>
    /// <param name="loop">The loop that runs the idle callback.</param>
    /// <param name="refresh">The callback that re-runs the pointer hit test.</param>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    public PointerRefresh(Scene scene, ICompositorEventLoop loop, Action refresh)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(refresh);

        _scene = scene;
        _loop = loop;
        _refresh = refresh;
        _runFromIdle = RunFromIdle;
        _scene.StructureChanged += Request;
        _scene.InputRegionChanged += Request;
    }

    /// <summary>
    /// Queues one refresh on the next idle callback. It does nothing after disposal, when a refresh is already queued, or while the refresh runs.
    /// </summary>
    public void Request()
    {
        if (_disposed || _queued || _running)
        {
            return;
        }

        _queued = true;
        _loop.AddIdle(_runFromIdle);
    }

    /// <summary>
    /// Unsubscribes from the scene. A refresh that is already queued does not run.
    /// </summary>
    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _scene.StructureChanged -= Request;
            _scene.InputRegionChanged -= Request;
        }
    }

    private void RunFromIdle()
    {
        _queued = false;
        if (_disposed)
        {
            return;
        }

        _running = true;
        try
        {
            _refresh();
        }
        finally
        {
            _running = false;
        }
    }
}
