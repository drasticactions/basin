using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

/// <summary>
/// The decoration role on a surface that the manager draws above or below a window. Dispose it to destroy the role object. The surface stays with the caller.
/// </summary>
public sealed class WmDecoration : IDisposable
{
    private readonly RiverWindowManager _wm;
    private readonly RiverDecorationV1 _proxy;
    private bool _disposed;

    internal WmDecoration(RiverWindowManager wm, RiverDecorationV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;
    }

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
    /// Holds the surface's next commit until render finish, so its pixels land with the rest of the rendering state.
    /// </summary>
    /// <remarks>The caller must commit the surface before the sequence ends. The library does not track this obligation for a decoration, and a missed commit is a protocol error.</remarks>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SyncNextCommit()
    {
        _wm.EnsureRender(nameof(SyncNextCommit));
        _proxy.SyncNextCommit();
    }

    /// <summary>
    /// Destroys the decoration role object. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }
}
