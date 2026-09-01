using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace RetroWm;

internal sealed class DockSurface : IDisposable
{
    private const int CellWidth = 64;
    private const int Margin = 4;
    private const int LabelGap = 2;

    private readonly ManagerSurface _surface;
    private readonly List<(ManagedWindow Window, Rect Cell)> _layout = [];
    private readonly List<DockEntry> _drawn = [];
    private int _drawnScale = -1;
    private int _drawnWidth = -1;
    private ManagedWindow? _drawnSelected;
    private bool _empty = true;

    internal DockSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm)
    {
        Output = output;
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Bottom, "retro-wm-dock");
        _surface.SetAnchor(
            ZwlrLayerSurfaceV1.Anchor.Bottom | ZwlrLayerSurfaceV1.Anchor.Left | ZwlrLayerSurfaceV1.Anchor.Right);
        _surface.SetSize(0, Theme.DockHeight);
        _surface.SetExclusiveZone(Theme.DockHeight);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public ManagedWindow? Selected { get; set; }

    public uint SurfaceId => _surface.SurfaceId;

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

    public bool Render(IReadOnlyList<DockEntry> entries, int scale)
    {
        scale = Math.Max(scale, 1);
        if (!_surface.IsConfigured)
        {
            return false;
        }

        var size = _surface.ConfiguredSize;
        if (size.IsEmpty)
        {
            size = new Size(Output.Dimensions.Width, Theme.DockHeight);
        }

        if (size.IsEmpty)
        {
            return false;
        }

        if (Selected is { Window.IsClosed: true } or { Iconized: false })
        {
            Selected = null;
        }

        if (!Dirty(entries, size.Width, scale))
        {
            return false;
        }

        var pixels = _surface.Prepare(size.Width, Theme.DockHeight, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var surface = _surface.CreateCanvas(pixels);
        if (surface is null)
        {
            return false;
        }

        Layout(entries, size.Width);
        Draw(surface.Canvas, entries, size.Width, scale);
        surface.Canvas.Flush();
        _surface.SetInputRegion(new Rect(0, 0, size.Width, Theme.DockHeight));

        _drawn.Clear();
        _drawn.AddRange(entries);
        _drawnScale = scale;
        _drawnWidth = size.Width;
        _drawnSelected = Selected;
        _empty = false;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Invalidate() => _drawnScale = -1;

    public void Dispose() => _surface.Dispose();

    private bool Dirty(IReadOnlyList<DockEntry> entries, int width, int scale)
    {
        if (_empty || scale != _drawnScale || width != _drawnWidth
            || !ReferenceEquals(_drawnSelected, Selected) || entries.Count != _drawn.Count)
        {
            return true;
        }

        for (var i = 0; i < entries.Count; i++)
        {
            if (!ReferenceEquals(entries[i].Window, _drawn[i].Window)
                || entries[i].Title != _drawn[i].Title
                || !ReferenceEquals(entries[i].Icon, _drawn[i].Icon))
            {
                return true;
            }
        }

        return false;
    }

    private void Layout(IReadOnlyList<DockEntry> entries, int width)
    {
        _layout.Clear();
        for (var i = 0; i < entries.Count; i++)
        {
            var x = Margin + (i * CellWidth);
            if (x + CellWidth > width)
            {
                break;
            }

            _layout.Add((entries[i].Window, new Rect(x, 0, CellWidth, Theme.DockHeight)));
        }
    }

    private void Draw(SKCanvas canvas, IReadOnlyList<DockEntry> entries, int width, int scale)
    {
        var pixelWidth = width * scale;
        var pixelHeight = Theme.DockHeight * scale;

        var background = Theme.DockBackground();
        if (background.Alpha != 0)
        {
            using var fill = new SKPaint();
            fill.IsAntialias = false;
            fill.Color = background;
            canvas.DrawRect(0, 0, pixelWidth, pixelHeight, fill);
        }

        using var paint = new SKPaint();
        paint.IsAntialias = false;
        using var font = new SKFont(Fonts.Sans, Theme.FontSize * 0.9f * scale);
        font.Subpixel = true;
        var metrics = font.Metrics;
        var labelHeight = (int)Math.Ceiling(metrics.Descent - metrics.Ascent);
        using var textPaint = new SKPaint();
        textPaint.IsAntialias = true;

        for (var i = 0; i < _layout.Count && i < entries.Count; i++)
        {
            var entry = entries[i];
            var cell = _layout[i].Cell;
            var iconX = (cell.X + ((cell.Width - Theme.IconSize) / 2)) * scale;
            var iconY = (Theme.DockLabels ? Margin : (Theme.DockHeight - Theme.IconSize) / 2) * scale;
            var iconSize = Theme.IconSize * scale;
            var selected = ReferenceEquals(entry.Window, Selected);

            if (!Theme.DockLabels && selected)
            {
                var pad = 3 * scale;
                var plateX = iconX - pad;
                var plateY = iconY - pad;
                var plateSize = iconSize + (2 * pad);
                paint.Color = Theme.Color(Theme.MenuHighlightBg);
                canvas.DrawRect(plateX, plateY, plateSize, plateSize, paint);
                paint.Color = Theme.Color(Theme.MenuHighlightText);
                canvas.DrawRect(plateX, plateY, plateSize, scale, paint);
                canvas.DrawRect(plateX, plateY + plateSize - scale, plateSize, scale, paint);
                canvas.DrawRect(plateX, plateY, scale, plateSize, paint);
                canvas.DrawRect(plateX + plateSize - scale, plateY, scale, plateSize, paint);
            }

            if (entry.Icon is { } icon)
            {
                canvas.DrawImage(
                    icon,
                    new SKRect(iconX, iconY, iconX + iconSize, iconY + iconSize),
                    new SKSamplingOptions(Theme.IconDither ? SKFilterMode.Nearest : SKFilterMode.Linear),
                    paint);
            }
            else
            {
                DrawDocumentGlyph(canvas, paint, iconX, iconY, scale);
            }

            if (!Theme.DockLabels)
            {
                continue;
            }

            var label = Fonts.Ellipsize(font, entry.Title, (cell.Width - (2 * Margin)) * scale);
            var labelWidth = font.MeasureText(label);
            var labelX = (cell.X * scale) + (((cell.Width * scale) - labelWidth) / 2f);
            var labelTop = iconY + iconSize + (LabelGap * scale);
            paint.Color = Theme.Color(selected ? Theme.MenuHighlightBg : Theme.ChromeBg);
            canvas.DrawRect(
                labelX - scale, labelTop, labelWidth + (2 * scale), labelHeight * scale, paint);
            textPaint.Color = Theme.Color(selected ? Theme.MenuHighlightText : Theme.DockLabel);
            var baseline = labelTop + (((labelHeight * scale) - (metrics.Descent - metrics.Ascent)) / 2f)
                - metrics.Ascent;
            canvas.DrawText(label, labelX, baseline, SKTextAlign.Left, font, textPaint);
        }
    }

    private static void DrawDocumentGlyph(SKCanvas canvas, SKPaint paint, int x, int y, int scale)
    {
        var pageX = x + (5 * scale);
        var pageY = y + (1 * scale);
        var pageWidth = 22 * scale;
        var pageHeight = 30 * scale;
        paint.Color = Theme.Color(Theme.WindowLine);
        canvas.DrawRect(pageX, pageY, pageWidth, pageHeight, paint);
        paint.Color = Theme.Color(Theme.ChromeBg);
        canvas.DrawRect(pageX + scale, pageY + scale, pageWidth - (2 * scale), pageHeight - (2 * scale), paint);
        paint.Color = Theme.Color(Theme.WindowLine);
        for (var line = 0; line < 5; line++)
        {
            canvas.DrawRect(
                pageX + (4 * scale), pageY + ((5 + (line * 5)) * scale), pageWidth - (8 * scale), scale, paint);
        }
    }

}
