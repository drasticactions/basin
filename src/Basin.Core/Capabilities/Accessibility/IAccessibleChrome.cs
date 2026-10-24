namespace Basin.Capabilities;

/// <summary>
/// Chrome without an accessibility tree of its own, such as a server-side frame or an immediate-mode panel, that describes itself as nodes.
/// The compositor's accessibility tree asks it again after <see cref="Changed"/>.
/// </summary>
public interface IAccessibleChrome
{
    /// <summary>
    /// Occurs on the compositor loop when the nodes that <see cref="Describe"/> writes change.
    /// </summary>
    event Action? Changed;

    /// <summary>
    /// Writes the chrome's nodes. Call it on the compositor loop.
    /// </summary>
    /// <param name="tree">The builder to write into.</param>
    void Describe(AccessibleTreeBuilder tree);
}
