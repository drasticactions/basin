using Basin.Capabilities;
using Basin.Color;
using Basin.Scene;

namespace Basin.Desktop;

public sealed class OutputColorDriver
{
    private readonly ColorManager _color;
    private readonly ColorOutputConfiguration _configuration;
    private readonly List<(OutputGlobal Global, IOutput Output, SceneOutput? Scene)> _outputs = [];
    private readonly Dictionary<OutputGlobal, ImageDescription> _blendSpaces = [];

    public OutputColorDriver(ColorManager color, ColorOutputConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(color);
        ArgumentNullException.ThrowIfNull(configuration);
        _color = color;
        _configuration = configuration;
        configuration.Applied += _ => Refresh();
    }

    public ImageDescription DescriptionOf(IOutput output) => _configuration.DescriptionOf(output);

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

    public void Remove(OutputGlobal global)
    {
        ArgumentNullException.ThrowIfNull(global);
        _outputs.RemoveAll(entry => entry.Global == global);
        _blendSpaces.Remove(global);
        _color.RemoveOutputDescription(global);
    }

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
