using Avalonia.Media;

namespace PlasmaHost.Shell;

public sealed class BreezeBrushes
{
    internal BreezeBrushes(BreezePalette palette)
    {
        ActiveBackground = new SolidColorBrush(palette.ActiveBackground);
        InactiveBackground = new SolidColorBrush(palette.InactiveBackground);
        ActiveForeground = new SolidColorBrush(palette.ActiveForeground);
        InactiveForeground = new SolidColorBrush(palette.InactiveForeground);
        Focus = new SolidColorBrush(palette.Focus);
        FocusPressed = new SolidColorBrush(Darken(palette.Focus));
        Negative = new SolidColorBrush(palette.Negative);
        NegativePressed = new SolidColorBrush(Darken(palette.Negative));
        MenuOutline = new SolidColorBrush(palette.ActiveForeground, 0.31);
    }

    public IBrush ActiveBackground { get; }

    public IBrush InactiveBackground { get; }

    public IBrush ActiveForeground { get; }

    public IBrush InactiveForeground { get; }

    public IBrush Focus { get; }

    public IBrush FocusPressed { get; }

    public IBrush Negative { get; }

    public IBrush NegativePressed { get; }

    public IBrush MenuOutline { get; }

    private static Color Darken(Color color) =>
        Color.FromRgb((byte)(color.R * 8 / 10), (byte)(color.G * 8 / 10), (byte)(color.B * 8 / 10));
}
