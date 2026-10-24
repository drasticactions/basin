using Basin.Capabilities;

namespace Basin.Scene;

/// <summary>
/// Holds the color lookup tables that convert each buffer node of a <see cref="Scene"/> to the color space of one output.
/// The tables fill at setup and when a description changes, never on a frame.
/// </summary>
public sealed class OutputColorTables : IColorLutTable, IDisposable
{
    private readonly Scene _scene;
    private readonly Dictionary<ImageDescription, IColorLut?> _luts = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<ImageDescription> _descriptionScratch = [];
    private IColorLut? _defaultLut;
    private ImageDescription? _colorDescription;
    private IColorTransformResolver? _resolver;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputColorTables"/> class.
    /// It attaches to the scene and builds the tables for every color description in it.
    /// </summary>
    /// <param name="scene">The scene whose buffer nodes the tables convert.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scene"/> is <see langword="null"/>.</exception>
    public OutputColorTables(Scene scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        _scene = scene;
        scene.ColorDescriptionChanged += OnColorDescriptionChanged;
        scene.AttachColorTables(this);
        RebuildLuts();
    }

    /// <summary>
    /// Occurs after <see cref="RebuildLuts"/> when a table existed before or exists after.
    /// </summary>
    public event Action? Rebuilt;

    /// <summary>
    /// Gets or sets the color space of the output. <see langword="null"/> means the default SDR description.
    /// A change in content rebuilds the tables.
    /// </summary>
    public ImageDescription? ColorDescription
    {
        get => _colorDescription;
        set
        {
            if (ReferenceEquals(_colorDescription, value) ||
                (_colorDescription is not null && value is not null &&
                 ImageDescription.ContentComparer.Equals(_colorDescription, value)))
            {
                _colorDescription = value;
                return;
            }

            _colorDescription = value;
            RebuildLuts();
        }
    }

    /// <summary>
    /// Gets or sets the resolver that makes the tables. When it is <see langword="null"/>, the getter returns <see cref="Scene.ColorTransforms"/> of the scene.
    /// A new value rebuilds the tables.
    /// </summary>
    public IColorTransformResolver? Resolver
    {
        get => _resolver ?? _scene.ColorTransforms;
        set
        {
            if (ReferenceEquals(_resolver, value))
            {
                return;
            }

            _resolver = value;
            RebuildLuts();
        }
    }

    /// <summary>
    /// Gets the number of tables that this object holds now. A buffer with no transform adds no table.
    /// </summary>
    public int LutCount
    {
        get
        {
            var count = _defaultLut is null ? 0 : 1;
            foreach (var lut in _luts.Values)
            {
                count += lut is null ? 0 : 1;
            }

            return count;
        }
    }

    /// <inheritdoc/>
    public IColorLut? SolidColorLut => _defaultLut;

    /// <inheritdoc/>
    public IColorLut? LutFor(SceneBuffer node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.ColorDescription is { } description
            ? _luts.GetValueOrDefault(description)
            : _defaultLut;
    }

    /// <summary>
    /// Makes the tables again for every color description in the scene.
    /// When a table existed before or exists after, it raises <see cref="Rebuilt"/>.
    /// </summary>
    public void RebuildLuts()
    {
        if (_disposed)
        {
            return;
        }

        var resolver = Resolver;
        var output = _colorDescription ?? ImageDescription.SdrDefault;
        var before = LutCount;
        _luts.Clear();
        _defaultLut = resolver?.Resolve(ImageDescription.SdrDefault, output);
        _descriptionScratch.Clear();
        _scene.CollectColorDescriptions(_descriptionScratch);
        foreach (var description in _descriptionScratch)
        {
            _luts[description] = resolver?.Resolve(description, output);
        }

        _descriptionScratch.Clear();
        if (before != 0 || LutCount != 0)
        {
            Rebuilt?.Invoke();
        }
    }

    /// <summary>
    /// Detaches from the scene and drops every table. A second call does nothing.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _scene.DetachColorTables(this);
        _scene.ColorDescriptionChanged -= OnColorDescriptionChanged;
        _luts.Clear();
        _defaultLut = null;
    }

    private void OnColorDescriptionChanged(SceneBuffer node)
    {
        if (node.ColorDescription is { } description && !_luts.ContainsKey(description))
        {
            _luts[description] = Resolver?.Resolve(description, _colorDescription ?? ImageDescription.SdrDefault);
        }
    }
}
