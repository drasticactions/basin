using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Basin;
using Basin.Capabilities;
using Basin.Scene;
using Basin.UI.Avalonia;
using Westonia.Shell;

using Basin.Diagnostics;

namespace Westonia;

internal sealed class AvaloniaShell : IDisposable
{
    public const int PanelThickness = 32;

    private readonly AvaloniaUIHost _host;
    private readonly ShellLayers _layers;
    private readonly WestonIni _ini;
    private readonly BasinLogger _log;
    private readonly Dictionary<IOutput, ShellElements> _elements = [];
    private readonly Bitmap? _backgroundImage;
    private readonly UISurfaceIndex _index;
    private readonly List<LauncherModel> _launchers = [];
    private bool _disposed;

    public AvaloniaShell(
        AvaloniaUIHost host,
        ShellLayers layers,
        WestonIni ini,
        BasinLogger log,
        Action<string> spawn,
        UISurfaceIndex index)
    {
        _host = host;
        _index = index;
        _layers = layers;
        _ini = ini;
        _log = log;
        _backgroundImage = LoadBackground(ini.Shell.BackgroundImage, log);

        foreach (var launcher in ini.Launchers)
        {
            var path = launcher.Path;
            _launchers.Add(new LauncherModel(
                launcher.DisplayName,
                LoadIcon(launcher.Icon, log),
                () => spawn(path)));
        }
    }

    public PanelPosition PanelPosition { get; set; }

    public IReadOnlyDictionary<IOutput, ShellElements> Elements => _elements;

    public void Create(IOutput output, int width, int height, double scale)
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
                Anchor = (box, _) => PanelBox(box),
            },
        };
        _elements[output] = elements;
        elements.BackgroundSurface.Realized += surface =>
            ((AvaloniaUISurface)surface).Content = new BackgroundView { DataContext = elements.Background };
        elements.PanelSurface.Realized += surface =>
            ((AvaloniaUISurface)surface).Content = new PanelView { DataContext = elements.Panel };

        elements.Background.Fill = new SolidColorBrush(Argb(_ini.Shell.BackgroundColor));
        elements.Background.Image = _backgroundImage;
        elements.Background.Stretch = _ini.Shell.BackgroundType switch
        {
            BackgroundType.Scale => Stretch.Fill,
            BackgroundType.ScaleCrop => Stretch.UniformToFill,
            BackgroundType.ScaleFit => Stretch.Uniform,
            BackgroundType.Centered => Stretch.None,
            _ => Stretch.None,
        };

        var panelColor = Argb(_ini.Shell.PanelColor);
        elements.Panel.PanelBrush = new SolidColorBrush(panelColor);
        elements.Panel.Variant = ContrastVariant(panelColor, Argb(_ini.Shell.BackgroundColor));
        elements.Panel.ClockVisible = _ini.Shell.ClockFormat != ClockFormat.None;
        foreach (var launcher in _launchers)
        {
            elements.Panel.Launchers.Add(launcher);
        }

        elements.Panel.ClockDock = PanelPosition switch
        {
            PanelPosition.Left or PanelPosition.Right => Avalonia.Controls.Dock.Bottom,
            _ => Avalonia.Controls.Dock.Right,
        };
        elements.Panel.Orientation = PanelPosition switch
        {
            PanelPosition.Left or PanelPosition.Right => Avalonia.Layout.Orientation.Vertical,
            _ => Avalonia.Layout.Orientation.Horizontal,
        };

        Place(output, 0, 0, width, height, scale);
    }

    public void Place(IOutput output, int x, int y, int width, int height, double scale)
    {
        if (!_elements.TryGetValue(output, out var elements))
        {
            return;
        }

        var box = new Box(x, y, width, height);
        elements.BackgroundSurface.Place(box, scale);
        if (PanelPosition != PanelPosition.None)
        {
            elements.PanelSurface.Place(box, scale);
        }
    }

    public void Remove(IOutput output)
    {
        if (_elements.Remove(output, out var elements))
        {
            elements.Dispose();
        }
    }

    public void SetClock(string text)
    {
        foreach (var elements in _elements.Values)
        {
            elements.Panel.Clock = text;
        }
    }

    public Box WorkArea(int x, int y, int width, int height)
    {
        var (panelWidth, panelHeight) = PanelSize(width, height);
        return PanelPosition switch
        {
            PanelPosition.Top => new Box(x, y + panelHeight, width, height - panelHeight),
            PanelPosition.Bottom => new Box(x, y, width, height - panelHeight),
            PanelPosition.Left => new Box(x + panelWidth, y, width - panelWidth, height),
            PanelPosition.Right => new Box(x, y, width - panelWidth, height),
            _ => new Box(x, y, width, height),
        };
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
        _backgroundImage?.Dispose();
    }

    private Box PanelBox(in Box output)
    {
        var (width, height) = PanelSize(output.Width, output.Height);
        var (x, y) = PanelOrigin(output.X, output.Y, output.Width, output.Height);
        return new Box(x, y, width, height);
    }

    private (int Width, int Height) PanelSize(int width, int height) => PanelPosition switch
    {
        PanelPosition.Left or PanelPosition.Right => (PanelThickness, height),
        PanelPosition.None => (width, 0),
        _ => (width, PanelThickness),
    };

    private (int X, int Y) PanelOrigin(int x, int y, int width, int height) => PanelPosition switch
    {
        PanelPosition.Bottom => (x, y + height - PanelThickness),
        PanelPosition.Right => (x + width - PanelThickness, y),
        _ => (x, y),
    };

    private static Avalonia.Styling.ThemeVariant ContrastVariant(Color over, Color under)
    {
        var alpha = over.A / 255.0;
        var r = ((over.R * alpha) + (under.R * (1 - alpha))) / 255.0;
        var g = ((over.G * alpha) + (under.G * (1 - alpha))) / 255.0;
        var b = ((over.B * alpha) + (under.B * (1 - alpha))) / 255.0;
        var luminance = (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
        return luminance < 0.5 ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light;
    }

    private static Color Argb(uint value) => Color.FromArgb(
        (byte)(value >> 24),
        (byte)(value >> 16),
        (byte)(value >> 8),
        (byte)value);

    private static Bitmap? LoadBackground(string? path, BasinLogger log) => LoadIcon(path, log) as Bitmap;

    private static IImage? LoadIcon(string? path, BasinLogger log)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return new Bitmap(path);
        }
        catch (Exception error)
        {
            log.Warn($"cannot load {path}: {error.Message}");
            return null;
        }
    }
}
