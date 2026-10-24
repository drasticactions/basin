namespace Basin.Capabilities;

/// <summary>
/// Finds a named element of the compositor's own UI surfaces for diagnostics, so a test or an agent can press it without fixed coordinates.
/// </summary>
public interface IUIElementLocator
{
    /// <summary>
    /// Finds a visible element by its name.
    /// </summary>
    /// <param name="name">The name that the UI toolkit gives the element.</param>
    /// <param name="box">The element's box in layout coordinates, or an empty box when no visible element has the name.</param>
    /// <returns><see langword="true"/> when a visible element has the name.</returns>
    bool TryLocate(string name, out Box box);
}
