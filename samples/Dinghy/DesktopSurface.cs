using Basin.WindowManager;
using Dinghy.Protocol;
using SkiaSharp;
using Wayland;

namespace Dinghy;

internal sealed class DesktopSurface : IDisposable
{
    public const int IconSize = 32;
    private const int LabelHeight = 14;
    private const int CellWidth = 64;
    private const int CellHeight = 50;
    private const int Margin = 4;

    private readonly ManagerSurface _surface;
    private readonly List<(ManagedWindow Window, Rect Cell)> _layout = [];
    private readonly List<Rect> _input = [];
    private string? _lastKey;

    internal DesktopSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Bottom, "dinghy-desktop");
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Bottom
            | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public ManagedWindow? SelectedIcon { get; set; }

    public int Columns { get; private set; } = 1;

    public uint SurfaceId => _surface.SurfaceId;

    public int IconCount => _layout.Count;

    public ManagedWindow? IconWindowAt(int index) =>
        index >= 0 && index < _layout.Count ? _layout[index].Window : null;

    public int? SelectedIndex()
    {
        for (var i = 0; i < _layout.Count; i++)
        {
            if (ReferenceEquals(_layout[i].Window, SelectedIcon))
            {
                return i;
            }
        }

        return null;
    }

    public ManagedWindow? IconAt(int x, int y)
    {
        foreach (var (window, cell) in _layout)
        {
            if (cell.Contains(new Point(x, y)))
            {
                return window;
            }
        }

        return null;
    }

    public bool Render(IReadOnlyList<DesktopIcon> icons, int scale)
    {
        if (!_surface.IsConfigured)
        {
            return false;
        }

        var size = _surface.ConfiguredSize;
        if (size.IsEmpty)
        {
            size = Output.Dimensions;
        }

        if (size.IsEmpty)
        {
            return false;
        }

        var key = $"{size.Width}x{size.Height}@{scale}|{SelectedIcon?.GetHashCode() ?? 0}|"
            + string.Join(';', icons.Select(static icon =>
                $"{icon.Window.GetHashCode():x}:{icon.Title}:{icon.Image is not null}"));
        if (key == _lastKey)
        {
            return false;
        }

        var pixels = _surface.Prepare(size.Width, size.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var surface = _surface.CreateCanvas(pixels);
        if (surface is null)
        {
            return false;
        }

        Layout(icons, size);
        Draw(surface.Canvas, icons, size, scale);
        surface.Canvas.Flush();
        _input.Clear();
        foreach (var (_, cell) in _layout)
        {
            _input.Add(cell);
        }

        _surface.SetInputRegion(_input);
        _lastKey = key;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Invalidate() => _lastKey = null;

    public void Dispose() => _surface.Dispose();

    private void Layout(IReadOnlyList<DesktopIcon> icons, Size size)
    {
        _layout.Clear();
        Columns = Math.Max((size.Width - (Margin * 2)) / CellWidth, 1);
        for (var i = 0; i < icons.Count; i++)
        {
            var column = i % Columns;
            var row = i / Columns;
            var cell = new Rect(
                Margin + (column * CellWidth),
                size.Height - Margin - CellHeight - (row * CellHeight),
                CellWidth,
                CellHeight);
            _layout.Add((icons[i].Window, cell));
        }
    }

    private void Draw(SKCanvas canvas, IReadOnlyList<DesktopIcon> icons, Size size, int scale)
    {
        _ = size;
        canvas.Clear(SKColors.Transparent);

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 0; i < _layout.Count; i++)
            {
                var selected = ReferenceEquals(_layout[i].Window, SelectedIcon);
                if (selected == (pass == 1))
                {
                    DrawIcon(canvas, icons[i], _layout[i].Cell, selected, scale);
                }
            }
        }
    }

    private static void DrawIcon(SKCanvas canvas, DesktopIcon icon, Rect cell, bool selected, int scale)
    {
        var iconX = (cell.X + ((CellWidth - IconSize) / 2)) * scale;
        var iconY = cell.Y * scale;
        var iconPx = IconSize * scale;

        using var paint = new SKPaint();
        paint.IsAntialias = false;
        if (icon.Image is { } image)
        {
            canvas.DrawImage(
                image,
                new SKRect(iconX, iconY, iconX + iconPx, iconY + iconPx),
                new SKSamplingOptions(SKFilterMode.Linear),
                paint);
        }
        else
        {
            paint.Color = Theme.Color(selected ? Theme.MenuHighlightBg : Theme.TitlebarBgActive);
            canvas.DrawRect(iconX, iconY, iconPx, iconPx, paint);
            paint.Color = new SKColor(0x40, 0x40, 0x40);
            for (var edge = 0; edge < scale; edge++)
            {
                canvas.DrawRect(iconX + edge, iconY + edge, iconPx - (2 * edge), iconPx - (2 * edge),
                    new SKPaint { IsAntialias = false, Color = paint.Color, IsStroke = true });
            }

            var letter = icon.Title.Length > 0 ? char.ToUpperInvariant(icon.Title[0]).ToString() : "?";
            using var letterFont = new SKFont(Fonts.Sans, IconSize * 0.5f * scale);
            paint.Color = SKColors.White;
            paint.IsAntialias = true;
            var metrics = letterFont.Metrics;
            var baseline = iconY + ((iconPx - (metrics.Descent - metrics.Ascent)) / 2f) - metrics.Ascent;
            canvas.DrawText(letter, iconX + (iconPx / 2f), baseline, SKTextAlign.Center, letterFont, paint);
        }

        var labelSize = Theme.FontSize * 0.8f;
        using var font = new SKFont(Fonts.Sans, labelSize * scale);
        var maxWidth = (selected ? IconSize * 5 : CellWidth) * scale;
        var label = Fonts.Ellipsize(font, icon.Title, maxWidth);
        var textWidth = font.MeasureText(label);
        var centerX = (cell.X + (CellWidth / 2)) * scale;
        var labelTop = (cell.Y + IconSize) * scale;
        var labelMetrics = font.Metrics;
        var labelBaseline = labelTop + ((LabelHeight * scale) - (labelMetrics.Descent - labelMetrics.Ascent)) / 2f
            - labelMetrics.Ascent;

        using var textPaint = new SKPaint();
        textPaint.IsAntialias = true;
        if (selected)
        {
            textPaint.Color = Theme.Color(Theme.MenuHighlightBg);
            var pad = 2 * scale;
            canvas.DrawRect(
                centerX - (textWidth / 2f) - pad,
                labelTop,
                textWidth + (pad * 2),
                LabelHeight * scale,
                textPaint);
            textPaint.Color = Theme.Color(Theme.MenuHighlightText);
        }
        else
        {
            textPaint.Color = Theme.Color(Theme.MenuText);
        }

        canvas.DrawText(label, centerX, labelBaseline, SKTextAlign.Center, font, textPaint);
    }

}
