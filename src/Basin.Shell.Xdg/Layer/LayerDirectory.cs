using Basin.Capabilities;

namespace Basin.Shell.Xdg;

public sealed class LayerDirectory : ILayerDirectory, IDisposable
{
    private readonly LayerShell _shell;
    private readonly List<LayerSurface> _layers = [];

    public LayerDirectory(LayerShell shell)
    {
        ArgumentNullException.ThrowIfNull(shell);
        _shell = shell;
        shell.NewSurface += OnNewSurface;
    }

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
