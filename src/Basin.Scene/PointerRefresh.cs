namespace Basin.Scene;

public sealed class PointerRefresh : IDisposable
{
    private readonly Scene _scene;
    private readonly ICompositorEventLoop _loop;
    private readonly Action _refresh;
    private readonly Action _runFromIdle;
    private bool _queued;
    private bool _running;
    private bool _disposed;

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
    }

    public void Request()
    {
        if (_disposed || _queued || _running)
        {
            return;
        }

        _queued = true;
        _loop.AddIdle(_runFromIdle);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _scene.StructureChanged -= Request;
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
