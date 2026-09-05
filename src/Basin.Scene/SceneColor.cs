using Basin.Capabilities;

namespace Basin.Scene;

public sealed partial class Scene
{
    private readonly List<SceneOutput> _outputs = [];
    private IColorTransformResolver? _colorTransforms;

    public IReadOnlyList<SceneOutput> Outputs => _outputs;

    public event Action<SceneBuffer>? ColorDescriptionChanged;

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
            foreach (var output in _outputs)
            {
                output.RebuildLuts();
            }
        }
    }

    public int LutCount
    {
        get
        {
            var count = 0;
            foreach (var output in _outputs)
            {
                count += output.LutCount;
            }

            return count;
        }
    }

    public int DescribeSurfaces(Func<Surface, ImageDescription?> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        return DescribeSurfaces(Root, describe);
    }

    public void CollectColorDescriptions(HashSet<ImageDescription> descriptions)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        CollectColorDescriptions(Root, descriptions);
    }

    internal void Attach(SceneOutput output) => _outputs.Add(output);

    internal void Detach(SceneOutput output) => _outputs.Remove(output);

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
