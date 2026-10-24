namespace Basin.Shell.Xdg;

/// <summary>
/// The service that manages the layer surfaces of a <see cref="LayerShell"/>. <see cref="LayerShellModule"/> requires one and refuses to install without it.
/// </summary>
public interface ILayerShellDriver
{
    /// <summary>
    /// Connects the driver to the layer shell. The module calls it once, when it installs the global.
    /// </summary>
    /// <param name="shell">The layer shell that the module created.</param>
    void Attach(LayerShell shell);
}
