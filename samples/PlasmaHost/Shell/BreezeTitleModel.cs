using Avalonia;
using Avalonia.Media;
using Basin.Capabilities;

namespace PlasmaHost.Shell;

public sealed class BreezeTitleModel : BreezeModel
{
    private readonly BreezeButtonModel[] _buttons =
    [
        new(FramePart.Menu),
        new(FramePart.Minimize),
        new(FramePart.Maximize),
        new(FramePart.Close),
    ];

    private string _title = string.Empty;
    private bool _active = true;
    private bool _maximized;
    private double _width;
    private double _titleLeft;
    private double _titleRight;
    private BreezeBrushes _brushes = new(BreezePalette.Fallback);

    public IReadOnlyList<BreezeButtonModel> Buttons => _buttons;

    public double Height => BreezeMetrics.TitleHeight;

    public string Title
    {
        get => _title;
        set => Set(ref _title, value);
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (!Set(ref _active, value))
            {
                return;
            }

            foreach (var button in _buttons)
            {
                button.Active = value;
            }

            Raise(nameof(Background));
            Raise(nameof(Foreground));
        }
    }

    public bool Maximized
    {
        get => _maximized;
        set
        {
            if (!Set(ref _maximized, value))
            {
                return;
            }

            foreach (var button in _buttons)
            {
                button.Maximized = value;
            }

            Raise(nameof(Corners));
        }
    }

    public double Width
    {
        get => _width;
        set => Set(ref _width, value);
    }

    public double TitleLeft
    {
        get => _titleLeft;
        set
        {
            if (Set(ref _titleLeft, value))
            {
                Raise(nameof(TitleMargin));
            }
        }
    }

    public double TitleRight
    {
        get => _titleRight;
        set
        {
            if (Set(ref _titleRight, value))
            {
                Raise(nameof(TitleMargin));
            }
        }
    }

    public BreezeBrushes Brushes
    {
        get => _brushes;
        set
        {
            if (!Set(ref _brushes, value))
            {
                return;
            }

            foreach (var button in _buttons)
            {
                button.Brushes = value;
            }

            Raise(nameof(Background));
            Raise(nameof(Foreground));
        }
    }

    public Thickness TitleMargin => new(_titleLeft, 0, Math.Max(0, _width - _titleRight), 0);

    public CornerRadius Corners => _maximized
        ? default
        : new CornerRadius(BreezeMetrics.CornerRadius, BreezeMetrics.CornerRadius, 0, 0);

    public IBrush Background => _active ? _brushes.ActiveBackground : _brushes.InactiveBackground;

    public IBrush Foreground => _active ? _brushes.ActiveForeground : _brushes.InactiveForeground;

    public BreezeButtonModel ButtonFor(FramePart part)
    {
        foreach (var button in _buttons)
        {
            if (button.Part == part)
            {
                return button;
            }
        }

        return _buttons[0];
    }
}
