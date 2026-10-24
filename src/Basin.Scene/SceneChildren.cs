namespace Basin.Scene;

/// <summary>
/// A read-only view of the children of a <see cref="SceneTree"/>, from bottom to top.
/// </summary>
public readonly struct SceneChildren
{
    private static readonly List<SceneNode> Empty = [];
    private readonly List<SceneNode>? _list;

    internal SceneChildren(List<SceneNode> list) => _list = list;

    /// <summary>
    /// Gets the number of children.
    /// </summary>
    public int Count => List.Count;

    private List<SceneNode> List => _list ?? Empty;

    /// <summary>
    /// Gets the child at an index, where 0 is the bottom.
    /// </summary>
    /// <param name="index">The index of the child.</param>
    /// <returns>The child.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is outside the list.</exception>
    public SceneNode this[int index] => List[index];

    /// <summary>
    /// Finds the index of a child.
    /// </summary>
    /// <param name="node">The node to find.</param>
    /// <returns>The index from the bottom, or -1 when the node is not a child.</returns>
    public int IndexOf(SceneNode node) => List.IndexOf(node);

    /// <summary>
    /// Copies the children into a new array, from bottom to top.
    /// </summary>
    /// <returns>The copy.</returns>
    public SceneNode[] ToArray() => [.. List];

    /// <summary>
    /// Gets an enumerator that walks the children from bottom to top.
    /// </summary>
    /// <returns>The enumerator.</returns>
    public Enumerator GetEnumerator() => new(List);

    /// <summary>
    /// Walks the children of a <see cref="SceneTree"/> from bottom to top. The tree must not change during the walk.
    /// </summary>
    public struct Enumerator
    {
        private List<SceneNode>.Enumerator _inner;

        internal Enumerator(List<SceneNode> list) => _inner = list.GetEnumerator();

        /// <summary>
        /// Gets the child at the current position.
        /// </summary>
        public readonly SceneNode Current => _inner.Current;

        /// <summary>
        /// Moves to the next child.
        /// </summary>
        /// <returns><see langword="false"/> after the top child.</returns>
        /// <exception cref="InvalidOperationException">The tree changed during the walk.</exception>
        public bool MoveNext() => _inner.MoveNext();
    }
}
