namespace Basin.Capabilities;

/// <summary>
/// Collects the nodes that <see cref="IAccessibleChrome.Describe"/> writes. A node's parent is an earlier node, or none for a top-level node.
/// </summary>
public sealed class AccessibleTreeBuilder
{
    private readonly List<AccessibleNode> _nodes = [];
    private readonly List<int> _parents = [];

    /// <summary>
    /// Gets the number of nodes.
    /// </summary>
    public int Count => _nodes.Count;

    /// <summary>
    /// Adds a node.
    /// </summary>
    /// <param name="parent">The index of the parent node, or -1 for a top-level node.</param>
    /// <param name="node">The node.</param>
    /// <returns>The index of the new node, which later nodes can name as their parent.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="parent"/> is not -1 and not the index of an earlier node.</exception>
    public int Add(int parent, in AccessibleNode node)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(parent, -1);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(parent, _nodes.Count);
        _nodes.Add(node);
        _parents.Add(parent);
        return _nodes.Count - 1;
    }

    /// <summary>
    /// Gets a node.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The node.</returns>
    public AccessibleNode NodeAt(int index) => _nodes[index];

    /// <summary>
    /// Gets the parent of a node.
    /// </summary>
    /// <param name="index">The index.</param>
    /// <returns>The index of the parent, or -1 for a top-level node.</returns>
    public int ParentOf(int index) => _parents[index];

    /// <summary>
    /// Removes every node.
    /// </summary>
    public void Clear()
    {
        _nodes.Clear();
        _parents.Clear();
    }
}
