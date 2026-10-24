using Basin.Capabilities;

namespace Basin.Scene;

/// <content>
/// Color management state for the scene.
/// </content>
public sealed partial class Scene
{
    private readonly List<SceneOutput> _outputs = [];
    private readonly List<OutputColorTables> _colorTables = [];
    private IColorTransformResolver? _colorTransforms;

    /// <summary>
    /// Gets the scene outputs that show this scene, in the order they attached.
    /// </summary>
    public IReadOnlyList<SceneOutput> Outputs => _outputs;

    /// <summary>
    /// Gets every output that zoom can magnify: each attached <see cref="SceneOutput"/> and each <see cref="ZoomView"/>.
    /// </summary>
    public IReadOnlyList<IZoomable> Zoomables => _zoomables;

    private readonly List<IZoomable> _zoomables = [];

    /// <summary>
    /// Occurs when the color description of a buffer node changes.
    /// </summary>
    public event Action<SceneBuffer>? ColorDescriptionChanged;

    /// <summary>
    /// Gets or sets the resolver that builds color transforms between descriptions. Setting it rebuilds every attached <see cref="OutputColorTables"/>.
    /// </summary>
    public IColorTransformResolver? ColorTransforms
    {
        get => _colorTransforms;
        set
        {
            if (ReferenceEquals(_colorTransforms, value))
            {
                return;
            }

            _colorTransforms = value;
            foreach (var tables in _colorTables)
            {
                tables.RebuildLuts();
            }
        }
    }

    /// <summary>
    /// Gets the total number of color lookup tables that the attached <see cref="OutputColorTables"/> hold.
    /// </summary>
    public int LutCount
    {
        get
        {
            var count = 0;
            foreach (var tables in _colorTables)
            {
                count += tables.LutCount;
            }

            return count;
        }
    }

    /// <summary>
    /// Sets the color description of every surface buffer in the scene from a callback.
    /// </summary>
    /// <param name="describe">Returns the description for a surface, or <see langword="null"/> to clear it.</param>
    /// <returns>The number of buffers that got a description.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="describe"/> is <see langword="null"/>.</exception>
    public int DescribeSurfaces(Func<Surface, ImageDescription?> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        return DescribeSurfaces(Root, describe);
    }

    /// <summary>
    /// Adds the color description of every buffer node in the scene to a set. Disabled nodes are included.
    /// </summary>
    /// <param name="descriptions">The set to add to. The method does not clear it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="descriptions"/> is <see langword="null"/>.</exception>
    public void CollectColorDescriptions(HashSet<ImageDescription> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        CollectColorDescriptions(Root, descriptions);
    }

    internal void Attach(SceneOutput output)
    {
        _outputs.Add(output);
        _zoomables.Add(output);
    }

    internal void Detach(SceneOutput output)
    {
        _outputs.Remove(output);
        _zoomables.Remove(output);
    }

    internal void AttachZoomable(IZoomable zoomable) => _zoomables.Add(zoomable);

    internal void DetachZoomable(IZoomable zoomable) => _zoomables.Remove(zoomable);

    internal void AttachColorTables(OutputColorTables tables) => _colorTables.Add(tables);

    internal void DetachColorTables(OutputColorTables tables) => _colorTables.Remove(tables);

    internal void NotifyColorDescription(SceneBuffer node) => ColorDescriptionChanged?.Invoke(node);

    private static int DescribeSurfaces(SceneTree tree, Func<Surface, ImageDescription?> describe)
    {
        var described = 0;
        foreach (var node in tree.Children)
        {
            switch (node)
            {
                case SceneBuffer { InputSurface: { } surface } buffer:
                    buffer.ColorDescription = describe(surface);
                    described += buffer.ColorDescription is null ? 0 : 1;
                    break;
                case SceneTree subtree:
                    described += DescribeSurfaces(subtree, describe);
                    break;
            }
        }

        return described;
    }

    private static void CollectColorDescriptions(SceneTree tree, HashSet<ImageDescription> descriptions)
    {
        foreach (var node in tree.Children)
        {
            switch (node)
            {
                case SceneBuffer { ColorDescription: { } description }:
                    descriptions.Add(description);
                    break;
                case SceneTree subtree:
                    CollectColorDescriptions(subtree, descriptions);
                    break;
            }
        }
    }
}
