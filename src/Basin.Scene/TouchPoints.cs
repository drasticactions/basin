namespace Basin.Scene;

public sealed class TouchPoints
{
    private readonly Dictionary<int, SceneNode> _nodes = [];
    private readonly Dictionary<int, (double X, double Y)> _positions = [];

    public void Down(int id, double x, double y, SceneNode? node)
    {
        _positions[id] = (x, y);
        if (node is not null)
        {
            _nodes[id] = node;
        }
    }

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

    public bool Up(int id)
    {
        _positions.Remove(id);
        return _nodes.Remove(id);
    }

    public void Clear()
    {
        _nodes.Clear();
        _positions.Clear();
    }
}
