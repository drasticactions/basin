using Basin.Capabilities;

namespace Basin.Shell.Xdg;

/// <summary>
/// The <see cref="ILayerDirectory"/> over one <see cref="LayerShell"/>. It records each layer surface that the shell makes and forgets it when its <see cref="Surface"/> is destroyed.
/// </summary>
public sealed class LayerDirectory : ILayerDirectory, IDisposable
{
    private readonly LayerShell _shell;
    private readonly List<LayerSurface> _layers = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="LayerDirectory"/> class. It records only the layer surfaces that the shell makes after this call.
    /// </summary>
    /// <param name="shell">The layer shell to watch.</param>
    public LayerDirectory(LayerShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        shell.NewSurface += OnNewSurface;
    }

    /// <inheritdoc/>
    public void CollectLayers(List<LayerInfo> into)
    {
        ArgumentNullException.ThrowIfNull(into);
        foreach (var layer in _layers)
        {
            if (layer.Surface.IsMapped)
            {
                into.Add(new LayerInfo(layer.Namespace, layer.Surface, layer.Output?.Output.Name));
            }
        }
    }

    /// <summary>
    /// Stops watching the layer shell and forgets every recorded layer surface.
    /// </summary>
    public void Dispose()
    {
        _shell.NewSurface -= OnNewSurface;
        _layers.Clear();
    }

    private void OnNewSurface(LayerSurface layer)
    {
        _layers.Add(layer);
        layer.Surface.Destroyed += () => _layers.Remove(layer);
    }
}
