namespace Basin.Capabilities;

/// <summary>
/// Lists the decorated windows of a window manager with the bounds of their frame parts, for diagnostics.
/// </summary>
public interface IWindowPartsSource
{
    /// <summary>
    /// Adds one entry for each decorated window to a list.
    /// </summary>
    /// <param name="into">The list to add the entries to. The call does not clear it.</param>
    void CollectWindows(List<WindowParts> into);
}
