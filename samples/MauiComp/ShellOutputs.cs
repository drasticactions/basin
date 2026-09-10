using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using MauiComp.Shell;

namespace MauiComp;

internal sealed class ShellOutputs : IDisposable
{
    public const int PanelThickness = 30;

    private readonly AvaloniaUIHost _host;
    private readonly MauiSurfaces _surfaces;
    private readonly ShellLayers _layers;
    private readonly UISurfaceIndex _index;
    private readonly Dictionary<IOutput, ShellElements> _elements = [];
    private bool _disposed;

    public ShellOutputs(AvaloniaUIHost host, MauiSurfaces surfaces, ShellLayers layers, UISurfaceIndex index)
    {
        _host = host;
        _surfaces = surfaces;
        _layers = layers;
        _index = index;
    }

    public string? BackgroundImage { get; set; }

    public IReadOnlyDictionary<IOutput, ShellElements> Elements => _elements;

    public event Action<PanelModel>? PanelCreated;

    public void Create(IOutput output, in Box box, double scale)
    {
        if (_disposed || _elements.ContainsKey(output))
        {
            return;
        }

        var elements = new ShellElements
        {
            BackgroundSurface = new OutputUISurface(_layers.Background, _host, _index) { PreciseDamage = true },
            PanelSurface = new OutputUISurface(_layers.Panel, _host, _index)
            {
                PreciseDamage = true,
                Anchor = (outputBox, _) =>
                    new Box(outputBox.X, outputBox.Bottom - PanelThickness, outputBox.Width, PanelThickness),
            },
        };
        elements.Background.ImagePath = BackgroundImage;
        _elements[output] = elements;
        elements.BackgroundSurface.Realized += surface =>
            elements.BackgroundScope = _surfaces.Attach(
                (AvaloniaUISurface)surface, new BackgroundPage { BindingContext = elements.Background });
        elements.PanelSurface.Realized += surface =>
            elements.PanelScope = _surfaces.Attach(
                (AvaloniaUISurface)surface, new PanelPage { BindingContext = elements.Panel });
        PanelCreated?.Invoke(elements.Panel);
        Place(output, box, scale);
    }

    public void Place(IOutput output, in Box box, double scale)
    {
        if (!_elements.TryGetValue(output, out var elements))
        {
            return;
        }

        elements.BackgroundSurface.Place(box, scale);
        elements.PanelSurface.Place(box, scale);
    }

    public void Remove(IOutput output)
    {
        if (_elements.Remove(output, out var elements))
        {
            elements.Dispose();
        }
    }

    public bool IsStartButtonAt(IUISurface? surface, double x, double y)
    {
        foreach (var elements in _elements.Values)
        {
            if (elements.PanelSurface.Surface is { } panel && ReferenceEquals(panel, surface) &&
                elements.PanelScope?.Page is PanelPage page)
            {
                var bounds = elements.PanelSurface.Bounds;
                return x >= bounds.X && x < bounds.X + page.StartButtonWidth && y >= bounds.Y && y < bounds.Bottom;
            }
        }

        return false;
    }

    public Box TaskIconBox(TaskEntry entry)
    {
        foreach (var elements in _elements.Values)
        {
            if (elements.PanelScope?.Page is PanelPage page && page.TaskBox(entry) is { } box)
            {
                var bounds = elements.PanelSurface.Bounds;
                return new Box(
                    bounds.X + (int)Math.Round(box.X),
                    bounds.Y + (int)Math.Round(box.Y),
                    Math.Max(1, (int)Math.Round(box.Width)),
                    Math.Max(1, (int)Math.Round(box.Height)));
            }
        }

        return default;
    }

    public static Box WorkArea(in Box output) =>
        new(output.X, output.Y, output.Width, output.Height - PanelThickness);

    public void SetClock(string text)
    {
        foreach (var elements in _elements.Values)
        {
            elements.Panel.Clock = text;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var elements in _elements.Values)
        {
            elements.Dispose();
        }

        _elements.Clear();
    }
}
