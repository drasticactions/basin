using Basin.WindowManager.Protocol;
using Wayland;

namespace Basin.WindowManager;

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

    public void SetOffset(int x, int y)
    {
        _wm.EnsureRender(nameof(SetOffset));
        _proxy.SetOffset(x, y);
    }

    public void SetOffset(Point offset) => SetOffset(offset.X, offset.Y);

    public void SyncNextCommit()
    {
        _wm.EnsureRender(nameof(SyncNextCommit));
        _proxy.SyncNextCommit();
    }

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
