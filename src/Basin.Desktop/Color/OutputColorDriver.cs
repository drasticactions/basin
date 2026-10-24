using Basin.Capabilities;
using Basin.Color;
using Basin.Scene;

namespace Basin.Desktop;

/// <summary>
/// Sends each output's configured image description to the <see cref="ColorManager"/> and to its scene output. It refreshes when the configuration is applied.
/// </summary>
public sealed class OutputColorDriver
{
    private readonly ColorManager _color;
    private readonly ColorOutputConfiguration _configuration;
    private readonly List<(OutputGlobal Global, IOutput Output, SceneOutput? Scene)> _outputs = [];
    private readonly Dictionary<OutputGlobal, ImageDescription> _blendSpaces = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="OutputColorDriver"/> class.
    /// </summary>
    /// <param name="color">The color manager that receives the output descriptions.</param>
    /// <param name="configuration">The configuration that supplies the descriptions.</param>
    /// <exception cref="ArgumentNullException"><paramref name="color"/> or <paramref name="configuration"/> is <see langword="null"/>.</exception>
    public OutputColorDriver(ColorManager color, ColorOutputConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(configuration);
        _color = color;
        _configuration = configuration;
        configuration.Applied += _ => Refresh();
    }

    /// <summary>
    /// Gets the image description that the configuration gives an output.
    /// </summary>
    /// <param name="output">The output to describe.</param>
    /// <returns>The configured description.</returns>
    public ImageDescription DescriptionOf(IOutput output) => _configuration.DescriptionOf(output);

    /// <summary>
    /// Adds an output and sends its description. For a known global, it only replaces a different non-null scene output.
    /// </summary>
    /// <param name="global">The output's global.</param>
    /// <param name="output">The output to describe.</param>
    /// <param name="scene">The scene output that gets the blend space, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="global"/> or <paramref name="output"/> is <see langword="null"/>.</exception>
    public void Add(OutputGlobal global, IOutput output, SceneOutput? scene = null)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(output);
        for (var i = 0; i < _outputs.Count; i++)
        {
            if (_outputs[i].Global == global)
            {
                if (scene is not null && _outputs[i].Scene != scene)
                {
                    _outputs[i] = (global, output, scene);
                    Describe(_outputs[i]);
                }

                return;
            }
        }

        var entry = (global, output, scene);
        _outputs.Add(entry);
        Describe(entry);
    }

    /// <summary>
    /// Removes an output, its blend space and its description in the color manager.
    /// </summary>
    /// <param name="global">The output's global.</param>
    /// <exception cref="ArgumentNullException"><paramref name="global"/> is <see langword="null"/>.</exception>
    public void Remove(OutputGlobal global)
    {
        ArgumentNullException.ThrowIfNull(global);
        _outputs.RemoveAll(entry => entry.Global == global);
        _blendSpaces.Remove(global);
        _color.RemoveOutputDescription(global);
    }

    /// <summary>
    /// Sets the description that the scene output blends in. Without one, the scene output blends in the output's own description.
    /// </summary>
    /// <param name="global">The output's global.</param>
    /// <param name="description">The blend space, or <see langword="null"/> to use the output description.</param>
    /// <exception cref="ArgumentNullException"><paramref name="global"/> is <see langword="null"/>.</exception>
    public void SetBlendSpace(OutputGlobal global, ImageDescription? description)
    {
        ArgumentNullException.ThrowIfNull(global);
        if (description is null)
        {
            _blendSpaces.Remove(global);
        }
        else
        {
            _blendSpaces[global] = description;
        }

        foreach (var entry in _outputs)
        {
            if (entry.Global == global)
            {
                Describe(entry);
            }
        }
    }

    /// <summary>
    /// Sends the description of every added output again.
    /// </summary>
    public void Refresh()
    {
        foreach (var entry in _outputs)
        {
            Describe(entry);
        }
    }

    private void Describe((OutputGlobal Global, IOutput Output, SceneOutput? Scene) entry)
    {
        var description = _configuration.DescriptionOf(entry.Output);
        _color.SetOutputDescription(entry.Global, description);
        if (entry.Scene is { } scene)
        {
            scene.ColorDescription = _blendSpaces.GetValueOrDefault(entry.Global, description);
        }
    }
}
