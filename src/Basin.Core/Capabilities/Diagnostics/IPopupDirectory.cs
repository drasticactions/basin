namespace Basin.Capabilities;

/// <summary>
/// Lists the popups of a surface for diagnostics.
/// </summary>
public interface IPopupDirectory
{
    /// <summary>
    /// Adds the surface of each mapped popup that <paramref name="owner"/> owns to a list, nested popups included.
    /// </summary>
    /// <param name="owner">The surface that owns the popups.</param>
    /// <param name="into">The list to add the surfaces to. The call does not clear it.</param>
    void CollectPopups(Surface owner, List<Surface> into);
}
