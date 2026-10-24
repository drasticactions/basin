using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// The decoration role on a surface that the manager draws above or below a window. Dispose it to destroy the role object. The surface stays with the caller.
/// </summary>
public sealed class WmDecoration : IDisposable, ISyncedSurface
{
    private readonly RiverWindowManager _wm;
    private readonly RiverDecorationV1 _proxy;
    private bool _disposed;

    internal WmDecoration(RiverWindowManager wm, RiverDecorationV1 proxy, WlSurface surface)
    {
        _wm = wm;
        _proxy = proxy;
        Surface = surface;
    }

    /// <summary>
    /// Gets the surface that carries the decoration role. The caller owns it.
    /// </summary>
    public WlSurface Surface { get; }

    /// <summary>
    /// Sets the decoration's offset from the top-left corner of the window. The compositor chooses the offset until the manager sets one.
    /// </summary>
    /// <param name="x">The horizontal offset in logical pixels.</param>
    /// <param name="y">The vertical offset in logical pixels.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SetOffset(int x, int y)
    {
        _wm.EnsureRender(nameof(SetOffset));
        _proxy.SetOffset(x, y);
    }

    /// <summary>
    /// Sets the decoration's offset from the top-left corner of the window from a point.
    /// </summary>
    /// <param name="offset">The offset in logical pixels.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SetOffset(Point offset) => SetOffset(offset.X, offset.Y);

    /// <summary>
    /// Holds the surface's next commit until render finish, so its pixels land with the rest of the rendering state. The caller must then call <see cref="Commit"/> before the sequence ends. Otherwise the library commits the surface at render finish and throws.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SyncNextCommit()
    {
        _wm.EnsureRender(nameof(SyncNextCommit));
        _proxy.SyncNextCommit();
        _wm.TrackSyncNextCommit(this);
    }

    /// <summary>
    /// Commits the surface and clears the obligation that <see cref="SyncNextCommit"/> recorded. Use it in place of a direct surface commit, which does not clear the obligation.
    /// </summary>
    public void Commit()
    {
        WmThreadAffinity.Assert();
        Surface.Commit();
        _wm.ClearSyncNextCommit(this);
    }

    /// <summary>
    /// Destroys the decoration role object and drops any open commit obligation. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wm.ClearSyncNextCommit(this);
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }

    /// <inheritdoc/>
    public override string ToString() => $"decoration surface {Surface.Id}";
}
