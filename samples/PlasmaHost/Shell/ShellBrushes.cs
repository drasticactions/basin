using Avalonia.Media;

namespace PlasmaHost.Shell;

public sealed class ShellBrushes
{
    internal ShellBrushes(BreezePalette palette)
    {
        OverlayBackdrop = new SolidColorBrush(palette.WindowBackground);
        TilesBackdrop = new SolidColorBrush(palette.WindowBackground, 0xB4 / 255.0);
        OsdBackground = new SolidColorBrush(palette.WindowBackground, 0xD0 / 255.0);
        Foreground = new SolidColorBrush(palette.WindowForeground);
        Dim = new SolidColorBrush(palette.WindowForegroundInactive);
        Highlight = new SolidColorBrush(palette.Highlight);
        Outline = new SolidColorBrush(palette.WindowForeground, 0x40 / 255.0);
        Fill = new SolidColorBrush(palette.WindowForeground, 0x30 / 255.0);
        TileFill = new SolidColorBrush(palette.WindowForeground, 0x28 / 255.0);
        PipOff = new SolidColorBrush(palette.WindowForeground, 0x60 / 255.0);
    }

    public IBrush OverlayBackdrop { get; }

    public IBrush TilesBackdrop { get; }

    public IBrush OsdBackground { get; }

    public IBrush Foreground { get; }

    public IBrush Dim { get; }

    public IBrush Highlight { get; }

    public IBrush Outline { get; }

    public IBrush Fill { get; }

    public IBrush TileFill { get; }

    public IBrush PipOff { get; }
}
