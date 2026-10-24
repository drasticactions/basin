using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// A surface that the manager owns and draws itself, for example a bar or a menu, with its own node in the render list.
/// </summary>
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

    /// <summary>
    /// Gets the surface that carries the role. The caller attaches buffers to it and keeps ownership of it.
    /// </summary>
    public WlSurface Surface { get; }

    /// <summary>
    /// Gets the surface's entry in the render list. The library creates it on first use.
    /// </summary>
    public WmNode Node => _node ??= new WmNode(_wm, _proxy.GetNode());

    /// <summary>
    /// Holds the surface's next commit until render finish. The caller must then call <see cref="Commit"/> before the render sequence ends.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SyncNextCommit()
    {
        _wm.EnsureRender(nameof(SyncNextCommit));
        _proxy.SyncNextCommit();
        _wm.TrackSyncNextCommit(this);
    }

    /// <summary>
    /// Commits the surface and clears the obligation that <see cref="SyncNextCommit"/> recorded. Use it in place of a direct surface commit.
    /// </summary>
    public void Commit()
    {
        WmThreadAffinity.Assert();
        Surface.Commit();
        _wm.ClearSyncNextCommit(this);
    }

    /// <summary>
    /// Destroys the node and the role object and drops any open commit obligation. The surface stays with the caller.
    /// </summary>
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

    /// <inheritdoc/>
    public override string ToString() => $"shell surface {Surface.Id}";

    internal RiverShellSurfaceV1 Proxy => _proxy;
}
