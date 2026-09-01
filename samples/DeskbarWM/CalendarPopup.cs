using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class CalendarPopup : IDisposable
{
    private const int CellSize = 24;
    private const int HeaderHeight = 26;
    private const int Border = 1;

    private readonly ManagerSurface _surface;
    private (DateTime Month, DateTime Today, int Scale)? _lastKey;

    internal CalendarPopup(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm,
        Point origin)
    {
        Output = output;
        Month = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "deskbar-calendar");
        _surface.SetExclusiveZone(-1);
        _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left);
        _surface.SetSize(SurfaceSize.Width, SurfaceSize.Height);
        _surface.SetMargin(origin.Y, origin.X);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public DateTime Month { get; private set; }

    public static Size SurfaceSize => new(
        (CellSize * 7) + (Border * 2) + 8,
        HeaderHeight + (CellSize * 7) + (Border * 2) + 8);

    public bool HandleClick(int x, int y)
    {
        if (y < HeaderHeight + Border)
        {
            if (x < SurfaceSize.Width / 4)
            {
                Month = Month.AddMonths(-1);
                return true;
            }

            if (x > SurfaceSize.Width * 3 / 4)
            {
                Month = Month.AddMonths(1);
                return true;
            }
        }

        return false;
    }

    public bool Render(int scale)
    {
        if (!_surface.IsConfigured || _surface.ConfiguredSize.IsEmpty)
        {
            return false;
        }

        var key = (Month, DateTime.Today, scale);
        if (key == _lastKey)
        {
            return false;
        }

        var size = _surface.ConfiguredSize;
        var pixels = _surface.Prepare(size.Width, size.Height, scale);
        if (pixels == 0)
        {
            return false;
        }

        using var canvas = _surface.CreateCanvas(pixels);
        if (canvas is null)
        {
            return false;
        }

        canvas.Canvas.Scale(scale);
        Draw(canvas.Canvas, size);
        canvas.Canvas.Flush();
        _surface.SetInputRegion(new Rect(0, 0, size.Width, size.Height));
        _lastKey = key;
        return true;
    }

    public void Commit() => _surface.Commit();

    public void Dispose() => _surface.Dispose();

    private void Draw(SKCanvas canvas, Size size)
    {
        var panel = new SKColor(216, 216, 216);
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        canvas.Clear(SKColors.Transparent);
        paint.Color = panel;
        canvas.DrawRect(0, 0, size.Width, size.Height, paint);
        paint.Color = SKColors.Black;
        canvas.DrawRect(0, 0, size.Width, 1, paint);
        canvas.DrawRect(0, size.Height - 1, size.Width, 1, paint);
        canvas.DrawRect(0, 0, 1, size.Height, paint);
        canvas.DrawRect(size.Width - 1, 0, 1, size.Height, paint);

        using var font = new SKFont(Fonts.Sans, Theme.FontSize);
        var metrics = font.Metrics;
        paint.IsAntialias = true;

        var header = Month.ToString("MMMM yyyy");
        var headerWidth = font.MeasureText(header);
        var headerBaseline = ((HeaderHeight - metrics.Ascent - metrics.Descent) / 2f) + 2;
        paint.Color = SKColors.Black;
        canvas.DrawText(header, (size.Width - headerWidth) / 2f, headerBaseline, SKTextAlign.Left, font, paint);
        canvas.DrawText("‹", 8, headerBaseline, SKTextAlign.Left, font, paint);
        canvas.DrawText("›", size.Width - 14, headerBaseline, SKTextAlign.Left, font, paint);

        var gridTop = HeaderHeight + Border + 2;
        var gridLeft = Border + 4;
        string[] initials = ["S", "M", "T", "W", "T", "F", "S"];
        paint.Color = new SKColor(96, 96, 96);
        for (var i = 0; i < 7; i++)
        {
            canvas.DrawText(
                initials[i],
                gridLeft + (i * CellSize) + (CellSize / 2f) - (font.MeasureText(initials[i]) / 2f),
                gridTop + CellSize - 8,
                SKTextAlign.Left,
                font,
                paint);
        }

        var first = new DateTime(Month.Year, Month.Month, 1);
        var startColumn = (int)first.DayOfWeek;
        var days = DateTime.DaysInMonth(Month.Year, Month.Month);
        var today = DateTime.Now.Date;
        for (var day = 1; day <= days; day++)
        {
            var cell = startColumn + day - 1;
            var column = cell % 7;
            var row = (cell / 7) + 1;
            var cx = gridLeft + (column * CellSize) + (CellSize / 2f);
            var cy = gridTop + (row * CellSize) + (CellSize / 2f);
            var date = new DateTime(Month.Year, Month.Month, day);
            if (date == today)
            {
                paint.Style = SKPaintStyle.Stroke;
                paint.StrokeWidth = 1.5f;
                paint.Color = new SKColor(51, 102, 152);
                canvas.DrawCircle(cx, cy, (CellSize / 2f) - 2, paint);
                paint.Style = SKPaintStyle.Fill;
            }

            paint.Color = SKColors.Black;
            var label = day.ToString();
            canvas.DrawText(
                label,
                cx - (font.MeasureText(label) / 2f),
                cy + 4,
                SKTextAlign.Left,
                font,
                paint);
        }

        paint.IsAntialias = false;
    }
}
