using Basin.WindowManager.Protocol;

namespace Basin.WindowManager;

/// <summary>
/// One entry in the compositor's flat render list, last on top. The protocol does not define a node's first position or order, so a manager places each node.
/// </summary>
public sealed class WmNode
{
    private readonly RiverWindowManager _wm;
    private readonly RiverNodeV1 _proxy;

    internal WmNode(RiverWindowManager wm, RiverNodeV1 proxy)
    {
        _wm = wm;
        _proxy = proxy;
    }

    /// <summary>
    /// Sets the node's absolute position. For a window this is the position of its content, not of its borders or decorations.
    /// </summary>
    /// <param name="x">The horizontal coordinate in the compositor's logical space. It can be negative.</param>
    /// <param name="y">The vertical coordinate in the compositor's logical space. It can be negative.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SetPosition(int x, int y)
    {
        _wm.EnsureRender(nameof(SetPosition));
        _proxy.SetPosition(x, y);
    }

    /// <summary>
    /// Sets the node's absolute position from a point.
    /// </summary>
    /// <param name="position">The position in the compositor's logical space.</param>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void SetPosition(Point position) => SetPosition(position.X, position.Y);

    /// <summary>
    /// Places the node above all other nodes.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void PlaceTop()
    {
        _wm.EnsureRender(nameof(PlaceTop));
        _proxy.PlaceTop();
    }

    /// <summary>
    /// Places the node below all other nodes.
    /// </summary>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void PlaceBottom()
    {
        _wm.EnsureRender(nameof(PlaceBottom));
        _proxy.PlaceBottom();
    }

    /// <summary>
    /// Places the node directly above another node.
    /// </summary>
    /// <param name="other">The node to stay below this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
    public void PlaceAbove(WmNode other)
    {
        ArgumentNullException.ThrowIfNull(other);
        _wm.EnsureRender(nameof(PlaceAbove));
        _proxy.PlaceAbove(other._proxy);
    }

    /// <summary>
    /// Places the node directly below another node.
    /// </summary>
    /// <param name="other">The node to stay above this one.</param>
    /// <exception cref="ArgumentNullException"><paramref name="other"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No manage or render sequence is in progress.</exception>
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
