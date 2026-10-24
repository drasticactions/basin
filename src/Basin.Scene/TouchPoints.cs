namespace Basin.Scene;

/// <summary>
/// Tracks touch points by id: the last scene position of each, and the node that each one went down on.
/// </summary>
public sealed class TouchPoints
{
    private readonly Dictionary<int, SceneNode> _nodes = [];
    private readonly Dictionary<int, (double X, double Y)> _positions = [];

    /// <summary>
    /// Starts or restarts a touch point.
    /// </summary>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="y">The vertical scene coordinate, in logical pixels.</param>
    /// <param name="node">The node under the point, or <see langword="null"/>. A <see langword="null"/> node keeps any node that the id had before.</param>
    public void Down(int id, double x, double y, SceneNode? node)
    {
        _positions[id] = (x, y);
        if (node is not null)
        {
            _nodes[id] = node;
        }
    }

    /// <summary>
    /// Records a new position and maps it into the local coordinates of the node that the point went down on.
    /// </summary>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The horizontal scene coordinate, in logical pixels.</param>
    /// <param name="y">The vertical scene coordinate, in logical pixels.</param>
    /// <param name="localX">The horizontal node-local coordinate, or 0 on failure.</param>
    /// <param name="localY">The vertical node-local coordinate, or 0 on failure.</param>
    /// <returns><see langword="false"/> when the point has no node, the node is destroyed, or the point cannot be mapped.</returns>
    public bool TryMotion(int id, double x, double y, out double localX, out double localY)
    {
        _positions[id] = (x, y);
        if (!_nodes.TryGetValue(id, out var node) || node.IsDestroyed)
        {
            localX = 0;
            localY = 0;
            return false;
        }

        return node.TryMapSceneToLocal(x, y, out localX, out localY);
    }

    /// <summary>
    /// Gets the last scene position of a touch point.
    /// </summary>
    /// <param name="id">The touch point id.</param>
    /// <param name="x">The horizontal scene coordinate, or 0 when the id is unknown.</param>
    /// <param name="y">The vertical scene coordinate, or 0 when the id is unknown.</param>
    /// <returns><see langword="true"/> when the id is down.</returns>
    public bool TryGetPosition(int id, out double x, out double y)
    {
        if (_positions.TryGetValue(id, out var position))
        {
            (x, y) = position;
            return true;
        }

        x = 0;
        y = 0;
        return false;
    }

    /// <summary>
    /// Ends a touch point.
    /// </summary>
    /// <param name="id">The touch point id.</param>
    /// <returns><see langword="true"/> when the point had a node.</returns>
    public bool Up(int id)
    {
        _positions.Remove(id);
        return _nodes.Remove(id);
    }

    /// <summary>
    /// Forgets every touch point, for example after a touch cancel.
    /// </summary>
    public void Clear()
    {
        _nodes.Clear();
        _positions.Clear();
    }
}
