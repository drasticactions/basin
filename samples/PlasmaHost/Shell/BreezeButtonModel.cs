using Avalonia.Media;
using Basin.Capabilities;

namespace PlasmaHost.Shell;

public sealed class BreezeButtonModel : BreezeModel
{
    private static readonly Geometry CloseGlyph = Geometry.Parse("M 7.5,7.5 L 16.5,16.5 M 7.5,16.5 L 16.5,7.5");
    private static readonly Geometry MinimizeGlyph = Geometry.Parse("M 7.5,9.75 L 12,14.25 L 16.5,9.75");
    private static readonly Geometry MaximizeGlyph = Geometry.Parse("M 7.5,14.25 L 12,9.75 L 16.5,14.25");
    private static readonly Geometry RestoreGlyph = Geometry.Parse("M 7.5,12 L 12,7.5 L 16.5,12 L 12,16.5 Z");
    private static readonly Geometry MenuGlyph =
        Geometry.Parse("M 7.5,8.5 L 16.5,8.5 M 7.5,12 L 16.5,12 M 7.5,15.5 L 16.5,15.5");

    private double _x;
    private double _y;
    private bool _isVisible;
    private bool _hot;
    private bool _pressed;
    private bool _maximized;
    private bool _active = true;
    private IImage? _icon;
    private BreezeBrushes _brushes = new(BreezePalette.Fallback);

    public BreezeButtonModel(FramePart part) => Part = part;

    public FramePart Part { get; }

    public double Size => BreezeMetrics.ButtonHit;

    public double CircleInset => (BreezeMetrics.ButtonHit - BreezeMetrics.ButtonCircle) / 2.0;

    public double CircleSize => BreezeMetrics.ButtonCircle;

    public double X
    {
        get => _x;
        set => Set(ref _x, value);
    }

    public double Y
    {
        get => _y;
        set => Set(ref _y, value);
    }

    public bool IsVisible
    {
        get => _isVisible;
        set => Set(ref _isVisible, value);
    }

    public bool Hot
    {
        get => _hot;
        set
        {
            if (Set(ref _hot, value))
            {
                Raise(nameof(Highlighted));
                Raise(nameof(HighlightOpacity));
                Raise(nameof(Background));
                Raise(nameof(Foreground));
            }
        }
    }

    public bool Pressed
    {
        get => _pressed;
        set
        {
            if (Set(ref _pressed, value))
            {
                Raise(nameof(Highlighted));
                Raise(nameof(HighlightOpacity));
                Raise(nameof(Background));
                Raise(nameof(Foreground));
            }
        }
    }

    public bool Maximized
    {
        get => _maximized;
        set
        {
            if (Set(ref _maximized, value))
            {
                Raise(nameof(Glyph));
            }
        }
    }

    public bool Active
    {
        get => _active;
        set
        {
            if (Set(ref _active, value))
            {
                Raise(nameof(Foreground));
            }
        }
    }

    public IImage? Icon
    {
        get => _icon;
        set
        {
            if (Set(ref _icon, value))
            {
                Raise(nameof(HasIcon));
                Raise(nameof(ShowsGlyph));
            }
        }
    }

    public BreezeBrushes Brushes
    {
        get => _brushes;
        set
        {
            if (Set(ref _brushes, value))
            {
                Raise(nameof(Background));
                Raise(nameof(Foreground));
            }
        }
    }

    public bool HasIcon => _icon is not null;

    public bool ShowsGlyph => _icon is null;

    public bool Highlighted => _hot || _pressed;

    public double HighlightOpacity => Highlighted ? 1.0 : 0.0;

    public IBrush Background
    {
        get
        {
            var negative = Part == FramePart.Close;
            if (_pressed && _hot)
            {
                return negative ? _brushes.NegativePressed : _brushes.FocusPressed;
            }

            return negative ? _brushes.Negative : _brushes.Focus;
        }
    }

    public IBrush Foreground => Highlighted
        ? (_active ? _brushes.ActiveBackground : _brushes.InactiveBackground)
        : (_active ? _brushes.ActiveForeground : _brushes.InactiveForeground);

    public Geometry Glyph => Part switch
    {
        FramePart.Close => CloseGlyph,
        FramePart.Minimize => MinimizeGlyph,
        FramePart.Maximize => _maximized ? RestoreGlyph : MaximizeGlyph,
        _ => MenuGlyph,
    };
}
