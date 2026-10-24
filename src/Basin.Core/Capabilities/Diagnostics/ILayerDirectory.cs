namespace Basin.Capabilities;

/// <summary>
/// Lists the layer-shell surfaces of a session for diagnostics.
/// </summary>
public interface ILayerDirectory
{
    /// <summary>
    /// Adds one entry for each mapped layer surface to a list.
    /// </summary>
    /// <param name="into">The list to add the entries to. The call does not clear it.</param>
    void CollectLayers(List<LayerInfo> into);
}
