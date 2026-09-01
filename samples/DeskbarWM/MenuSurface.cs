using Basin.WindowManager;
using Basin.WindowManager.Skia;
using Basin.WindowManager.Skia.Protocol;
using SkiaSharp;
using Wayland;

namespace DeskbarWm;

internal sealed class MenuSurface : IDisposable
{
    private const int PaddingX = 10;
    private const int PaddingY = 3;
    private const int SeparatorHeight = 7;
    private const int CheckWidth = 14;
    private const int ArrowWidth = 12;

    private readonly ManagerSurface _surface;
    private string? _lastKey;

    internal MenuSurface(
        WlCompositor compositor,
        WlShm shm,
        ZwlrLayerShellV1 layerShell,
        WlOutput wlOutput,
        WmOutput output,
        RiverWindowManager wm,
        IReadOnlyList<MenuItemEntry> items,
        Point origin,
        bool alignRight = false,
        bool alignBottom = false)
    {
        Output = output;
        Items = items;
        using (var font = new SKFont(Fonts.Sans, Theme.FontSize))
        {
            SurfaceSize = Measure(font);
        }

        if (alignRight)
        {
            origin = origin with { X = origin.X - SurfaceSize.Width };
        }

        if (alignBottom)
        {
            origin = origin with { Y = origin.Y - SurfaceSize.Height };
        }

        var area = output.Area;
        Origin = new Point(
            Math.Clamp(origin.X, 0, Math.Max(area.Width - SurfaceSize.Width, 0)),
            Math.Clamp(origin.Y, 0, Math.Max(area.Height - SurfaceSize.Height, 0)));

        _surface = new ManagerSurface(
            compositor, shm, layerShell, wlOutput, ZwlrLayerShellV1.Layer.Overlay, "deskbar-menu");
        _surface.SetExclusiveZone(-1);
        _surface.SetAnchor(ZwlrLayerSurfaceV1.Anchor.Top | ZwlrLayerSurfaceV1.Anchor.Left);
        _surface.SetSize(SurfaceSize.Width, SurfaceSize.Height);
        _surface.SetMargin(Origin.Y, Origin.X);
        _surface.Configured += wm.RequestManage;
        _surface.CommitInitial();
    }

    public WmOutput Output { get; }

    public IReadOnlyList<MenuItemEntry> Items { get; }

    public Point Origin { get; }

    public Size SurfaceSize { get; }

    public uint SurfaceId => _surface.SurfaceId;

    public int Hovered { get; private set; } = -1;

    private static int ItemHeight => (int)MathF.Ceiling(Theme.FontSize) + (PaddingY * 2) + 4;

    public int? ItemAt(int x, int y)
    {
        if (x < 1 || x >= SurfaceSize.Width - 1)
        {
            return null;
        }

        var offset = 1;
        for (var i = 0; i < Items.Count; i++)
        {
            var height = Items[i].Separator ? SeparatorHeight : ItemHeight;
            if (y >= offset && y < offset + height)
            {
                return Items[i].Separator ? null : i;
            }

            offset += height;
        }

        return null;
    }

    public Rect ItemRect(int index)
    {
        var offset = 1;
        for (var i = 0; i < Items.Count; i++)
        {
            var height = Items[i].Separator ? SeparatorHeight : ItemHeight;
            if (i == index)
            {
                return new Rect(0, offset, SurfaceSize.Width, height);
            }

            offset += height;
        }

        return Rect.Empty;
    }

    public bool UpdateHover(int x, int y)
    {
        var next = ItemAt(x, y) ?? -1;
        if (next != Hovered)
        {
            Hovered = next;
            return true;
        }

        return false;
    }

    public bool Render(int scale)
    {
        if (!_surface.IsConfigured || _surface.ConfiguredSize.IsEmpty)
        {
            return false;
        }

        var key = $"{scale}|{Hovered}";
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

    private Size Measure(SKFont font)
    {
        var width = 60f;
        var height = 2;
        foreach (var item in Items)
        {
            if (item.Separator)
            {
                height += SeparatorHeight;
                continue;
            }

            var itemWidth = font.MeasureText(item.Label) + (PaddingX * 2) + CheckWidth;
            if (item.Children is not null)
            {
                itemWidth += ArrowWidth;
            }

            width = MathF.Max(width, itemWidth);
            height += ItemHeight;
        }

        return new Size((int)MathF.Ceiling(width), height);
    }

    private void Draw(SKCanvas canvas, Size size)
    {
        var panel = new SKColor(216, 216, 216);
        using var paint = new SKPaint();
        paint.IsAntialias = false;
        canvas.Clear(SKColors.Transparent);
        paint.Color = panel;
        canvas.DrawRect(0, 0, size.Width, size.Height, paint);
        paint.Color = Theme.Tint(panel, Theme.Darken2);
        canvas.DrawRect(0, 0, size.Width, 1, paint);
        canvas.DrawRect(0, size.Height - 1, size.Width, 1, paint);
        canvas.DrawRect(0, 0, 1, size.Height, paint);
        canvas.DrawRect(size.Width - 1, 0, 1, size.Height, paint);

        using var font = new SKFont(Fonts.Sans, Theme.FontSize);
        var metrics = font.Metrics;
        var y = 1;
        for (var i = 0; i < Items.Count; i++)
        {
            var item = Items[i];
            if (item.Separator)
            {
                paint.Color = Theme.Tint(panel, Theme.Darken2);
                canvas.DrawRect(4, y + (SeparatorHeight / 2), size.Width - 8, 1, paint);
                paint.Color = Theme.Tint(panel, Theme.Lighten2);
                canvas.DrawRect(4, y + (SeparatorHeight / 2) + 1, size.Width - 8, 1, paint);
                y += SeparatorHeight;
                continue;
            }

            if (i == Hovered && item.Enabled)
            {
                paint.Color = Theme.Tint(panel, Theme.DarkenHalf);
                canvas.DrawRect(1, y, size.Width - 2, ItemHeight, paint);
            }

            paint.IsAntialias = true;
            paint.Color = item.Enabled ? SKColors.Black : new SKColor(150, 150, 150);
            var baseline = y + ((ItemHeight - metrics.Ascent - metrics.Descent) / 2f);
            var centerY = y + (ItemHeight / 2f);
            if (item.Checked)
            {
                paint.StrokeWidth = 1.6f;
                paint.Style = SKPaintStyle.Stroke;
                canvas.DrawLine(PaddingX - 4, centerY, PaddingX - 1, centerY + 3, paint);
                canvas.DrawLine(PaddingX - 1, centerY + 3, PaddingX + 5, centerY - 4, paint);
                paint.Style = SKPaintStyle.Fill;
            }

            canvas.DrawText(item.Label, PaddingX + CheckWidth - 4, baseline, SKTextAlign.Left, font, paint);
            if (item.Children is not null)
            {
                var builder = new SKPathBuilder();
                var ax = size.Width - ArrowWidth + 2;
                builder.MoveTo(ax, centerY - 4);
                builder.LineTo(ax + 5, centerY);
                builder.LineTo(ax, centerY + 4);
                builder.Close();
                using var arrow = builder.Detach();
                canvas.DrawPath(arrow, paint);
            }

            paint.IsAntialias = false;
            y += ItemHeight;
        }
    }
}
