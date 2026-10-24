using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Maps scene nodes to the UI surfaces that they show, and UI surfaces to the <see cref="UISurfaceNode"/> that owns them.
/// A <see cref="UISurfaceNode"/> adds and removes itself when it gets an index at construction.
/// </summary>
public sealed class UISurfaceIndex
{
    private readonly Dictionary<SceneNode, IUISurface> _surfaces = [];
    private readonly Dictionary<SceneNode, UISurfaceNode> _nodes = [];
    private readonly Dictionary<IUISurface, UISurfaceNode> _owners = [];

    /// <summary>
    /// Gets the number of <see cref="UISurfaceNode"/> instances in the index.
    /// </summary>
    public int Count => _nodes.Count;

    /// <summary>
    /// Records a node and its surface. A node that has no surface yet is not recorded.
    /// </summary>
    /// <param name="node">The node to record. A second add replaces the earlier entries.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
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

    /// <summary>
    /// Removes a node and its surface. A node that is not in the index is ignored.
    /// </summary>
    /// <param name="node">The node to remove.</param>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public void Remove(UISurfaceNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (_surfaces.Remove(node.Node, out var surface))
        {
            _owners.Remove(surface);
        }

        _nodes.Remove(node.Node);
    }

    /// <summary>
    /// Finds the UI surface that a scene node shows.
    /// </summary>
    /// <param name="node">The scene buffer of a <see cref="UISurfaceNode"/>.</param>
    /// <returns>The surface, or <see langword="null"/> when the node is not in the index.</returns>
    public IUISurface? SurfaceOf(SceneNode node) =>
        node is not null && _surfaces.TryGetValue(node, out var surface) ? surface : null;

    /// <summary>
    /// Finds the <see cref="UISurfaceNode"/> that a scene node belongs to.
    /// </summary>
    /// <param name="node">The scene buffer of a <see cref="UISurfaceNode"/>.</param>
    /// <returns>The owning node, or <see langword="null"/> when the node is not in the index.</returns>
    public UISurfaceNode? NodeOf(SceneNode node) =>
        node is not null && _nodes.TryGetValue(node, out var entry) ? entry : null;

    /// <summary>
    /// Finds the <see cref="UISurfaceNode"/> that shows a UI surface.
    /// </summary>
    /// <param name="surface">The surface to look up.</param>
    /// <returns>The owning node, or <see langword="null"/> when the surface is not in the index.</returns>
    public UISurfaceNode? OwnerOf(IUISurface surface) =>
        surface is not null && _owners.TryGetValue(surface, out var owner) ? owner : null;
}
