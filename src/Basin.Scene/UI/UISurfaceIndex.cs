using Basin.Capabilities;

namespace Basin.Scene;

public sealed class UISurfaceIndex
{
    private readonly Dictionary<SceneNode, IUISurface> _surfaces = [];
    private readonly Dictionary<SceneNode, UISurfaceNode> _nodes = [];
    private readonly Dictionary<IUISurface, UISurfaceNode> _owners = [];

    public int Count => _nodes.Count;

    public void Add(UISurfaceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Surface is { } surface)
        {
            _surfaces[node.Node] = surface;
            _nodes[node.Node] = node;
            _owners[surface] = node;
        }
    }

    public void Remove(UISurfaceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_surfaces.Remove(node.Node, out var surface))
        {
            _owners.Remove(surface);
        }

        _nodes.Remove(node.Node);
    }

    public IUISurface? SurfaceOf(SceneNode node) =>
        node is not null && _surfaces.TryGetValue(node, out var surface) ? surface : null;

    public UISurfaceNode? NodeOf(SceneNode node) =>
        node is not null && _nodes.TryGetValue(node, out var entry) ? entry : null;

    public UISurfaceNode? OwnerOf(IUISurface surface) =>
        surface is not null && _owners.TryGetValue(surface, out var owner) ? owner : null;
}
