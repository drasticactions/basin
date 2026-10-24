namespace Basin;

/// <summary>
/// Represents a holder of objects that a renderer made, for example textures, tables or a device reference. A service or a module that implements it is told when the renderer is replaced.
/// </summary>
public interface IRendererBound
{
    /// <summary>
    /// Releases every object of the old renderer and keeps the new one. The old context is current during the call, so make new GPU objects on next use, not here.
    /// </summary>
    /// <param name="replacement">The old and the new renderer.</param>
    void OnRendererReplaced(RendererReplacement replacement);

    /// <summary>
    /// Makes new GPU objects on the new renderer. The old renderer is disposed and the new context is current during the call. The default does nothing.
    /// </summary>
    /// <param name="replacement">The old and the new renderer. The old stack is no longer usable.</param>
    void OnRendererRebuilt(RendererReplacement replacement)
    {
    }
}
