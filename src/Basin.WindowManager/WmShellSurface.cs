using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

public sealed class WmShellSurface : IDisposable
{
    private readonly RiverWindowManager _wm;
    private readonly RiverShellSurfaceV1 _proxy;
    private WmNode? _node;
    private bool _disposed;

    internal WmShellSurface(RiverWindowManager wm, RiverShellSurfaceV1 proxy, WlSurface surface)
    {
        _wm = wm;
        _proxy = proxy;
        Surface = surface;
        _wm.RegisterShellSurface(proxy, this);
    }

    public WlSurface Surface { get; }

    public WmNode Node => _node ??= new WmNode(_wm, _proxy.GetNode());

    public void SyncNextCommit()
    {
        _wm.EnsureRender(nameof(SyncNextCommit));
        _proxy.SyncNextCommit();
        _wm.TrackSyncNextCommit(this);
    }

    public void Commit()
    {
        WmThreadAffinity.Assert();
        Surface.Commit();
        _wm.ClearSyncNextCommit(this);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wm.ClearSyncNextCommit(this);
        _wm.UnregisterShellSurface(_proxy);
        _node?.DestroyProxy();
        _node = null;
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }

    public override string ToString() => $"shell surface {Surface.Id}";

    internal RiverShellSurfaceV1 Proxy => _proxy;
}
