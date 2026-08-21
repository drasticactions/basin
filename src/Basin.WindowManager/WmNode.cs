using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

public sealed class WmNode
{
    private readonly RiverWindowManager _wm;
    private readonly RiverNodeV1 _proxy;

    internal WmNode(RiverWindowManager wm, RiverNodeV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;
    }

    public void SetPosition(int x, int y)
    {
        _wm.EnsureRender(nameof(SetPosition));
        _proxy.SetPosition(x, y);
    }

    public void SetPosition(Point position) => SetPosition(position.X, position.Y);

    public void PlaceTop()
    {
        _wm.EnsureRender(nameof(PlaceTop));
        _proxy.PlaceTop();
    }

    public void PlaceBottom()
    {
        _wm.EnsureRender(nameof(PlaceBottom));
        _proxy.PlaceBottom();
    }

    public void PlaceAbove(WmNode other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _wm.EnsureRender(nameof(PlaceAbove));
        _proxy.PlaceAbove(other._proxy);
    }

    public void PlaceBelow(WmNode other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _wm.EnsureRender(nameof(PlaceBelow));
        _proxy.PlaceBelow(other._proxy);
    }

    internal void DestroyProxy()
    {
        if (!_proxy.IsDestroyed)
        {
            _proxy.Destroy();
        }
    }
}
